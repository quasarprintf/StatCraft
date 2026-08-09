using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
using StatCraft.Services.BackgroundService;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;

namespace StatCraft.ViewModels
{
    // Owns one player's build selection(s) and derived attribute editors within a game — reused for the
    // session user as well as every ally/opponent, since GameBuilds/GameAttributeValues are tied to a
    // GamePlayer's own id, not to the game as a whole.
    public partial class PlayerBuildTrackerViewModel : ViewModelBase
    {
        public string TabHeader => _player.Name;

        // Null for the session user's own tracker (no tab, so nothing reads this); set by
        // GameDataRowViewModel for allies/opponents so their tab header can be colored by side.
        public IBrush? NameColor { get; }

        private readonly GamePlayer _player;
        private readonly GameDataRepository _repository;
        private readonly ObservableCollection<BuildNode> _buildTree;
        private readonly ILogger _logger;

        [ObservableProperty] private string _selectedBuildsSummary = "";

        // Slots are always [...persisted selections, one trailing blank] — selecting a build in the
        // trailing slot appends a new blank after it, and clearing a non-trailing slot removes it.
        public ObservableCollection<BuildSelectionSlotViewModel> BuildSlots { get; } = [];
        public ObservableCollection<AttributeValue> AttributeEditors { get; } = [];

        internal PlayerBuildTrackerViewModel(GamePlayer player, GameDataRepository repository, ObservableCollection<BuildNode>? buildTree, ILogger logger, IBrush? nameColor = null)
        {
            _player = player;
            _repository = repository;
            _buildTree = buildTree ?? [];
            _logger = logger;
            NameColor = nameColor;

            if (buildTree == null)
            {
                // Matchup couldn't be resolved (e.g. FFA/unusual team layout) — show a single disabled
                // picker, same as before multi-select existed.
                BuildSlots.Add(new BuildSelectionSlotViewModel(null, logger));
            }
            else
            {
                foreach (int buildId in player.BuildIds)
                {
                    BuildNode? node = BuildPathHelper.FindPath(_buildTree, buildId)?.LastOrDefault();
                    if (node == null)
                        continue; // build was deleted since this player was tagged with it

                    // Set SelectedBuildNode before subscribing SelectionChanged, so hydrating a saved
                    // selection doesn't trigger the append/remove/persist logic meant for user edits.
                    BuildSelectionSlotViewModel slot = new(buildTree, logger) { SelectedBuildNode = node };
                    slot.SelectionChanged += OnSlotSelectionChanged;
                    BuildSlots.Add(slot);
                }
                AppendBlankSlot();
            }

            UpdateSelectedBuildsSummary();
            RebuildAttributeEditors();
        }

        private void AppendBlankSlot()
        {
            BuildSelectionSlotViewModel slot = new(_buildTree, _logger);
            slot.SelectionChanged += OnSlotSelectionChanged;
            BuildSlots.Add(slot);
        }

        private void OnSlotSelectionChanged(BuildSelectionSlotViewModel slot, BuildNode? previousValue)
        {
            // Picking a build that's already covered by another selected slot adds nothing: an exact
            // duplicate would violate GameBuilds' UNIQUE(GamePlayerId, BuildId) constraint on persist, and
            // picking an ancestor of an already-selected build (e.g. selecting A when A->B is already
            // selected elsewhere) is redundant, since B's own path already includes A's attributes.
            // Revert instead of letting either through; reverting fires this handler again with the
            // (already-valid) previous value.
            if (slot.SelectedBuildNode != null && IsRedundantSelection(slot.SelectedBuildNode, slot))
            {
                slot.SelectedBuildNode = previousValue;
                return;
            }

            // The opposite direction is meaningful, but makes any ancestor already selected elsewhere
            // redundant (e.g. selecting A->B when A is already selected elsewhere) — clear those slots'
            // selections, which recurses back into this method and removes them the same way clearing one
            // by hand would.
            if (slot.SelectedBuildNode != null)
                ClearSubsumedAncestorSlots(slot);

            int index = BuildSlots.IndexOf(slot);
            bool isLast = index == BuildSlots.Count - 1;

            if (slot.SelectedBuildNode != null && isLast)
            {
                AppendBlankSlot();
            }
            else if (slot.SelectedBuildNode == null && !isLast)
            {
                slot.SelectionChanged -= OnSlotSelectionChanged;
                BuildSlots.RemoveAt(index);
            }

            List<int> buildIds = BuildSlots
                .Select(s => s.SelectedBuildNode?.Id)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();
            _player.BuildIds = buildIds;
            TryUpdateGameBuilds(buildIds);

            UpdateSelectedBuildsSummary();
            RebuildAttributeEditors();
        }

        // The four DB writes below are all "best effort, keep the in-memory state as the source of
        // truth regardless" — e.g. a build/attribute referenced here having since been deleted (a
        // FOREIGN KEY violation) shouldn't leave the whole page unusable over one row failing to
        // persist. Logged so a real inconsistency is visible instead of just silently not saving.
        private void TryUpdateGameBuilds(List<int> buildIds)
        {
            try
            {
                _repository.UpdateGameBuilds(_player.GamePlayerId!.Value, buildIds);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to persist build selection for \"{_player.Name}\" (GamePlayerId={_player.GamePlayerId}): {ex}");
            }
        }

        private void TryUpsertAttributeValue(int buildAttributeId, string value)
        {
            try
            {
                _repository.UpsertAttributeValue(_player.GamePlayerId!.Value, buildAttributeId, value);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to persist attribute value (BuildAttributeId={buildAttributeId}) for \"{_player.Name}\" (GamePlayerId={_player.GamePlayerId}): {ex}");
            }
        }

        private void TryDeleteAttributeValue(int buildAttributeId)
        {
            try
            {
                _repository.DeleteAttributeValue(_player.GamePlayerId!.Value, buildAttributeId);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to delete attribute value (BuildAttributeId={buildAttributeId}) for \"{_player.Name}\" (GamePlayerId={_player.GamePlayerId}): {ex}");
            }
        }

        // True if candidate is already selected in another slot, or is an ancestor of what's selected in
        // another slot — either way, that other slot's own root-to-leaf path already covers candidate.
        private bool IsRedundantSelection(BuildNode candidate, BuildSelectionSlotViewModel slot) =>
            BuildSlots.Any(other => other != slot && other.SelectedBuildNode != null &&
                FindPathOrLog(other.SelectedBuildNode).Any(n => n.Id == candidate.Id));

        // Clears the selection of any other slot whose selected build is a strict ancestor of slot's
        // newly selected build — that ancestor's own attributes are already covered by slot's longer path.
        private void ClearSubsumedAncestorSlots(BuildSelectionSlotViewModel slot)
        {
            HashSet<int> ancestorIds = FindPathOrLog(slot.SelectedBuildNode!)
                .Select(n => n.Id)
                .Where(id => id != slot.SelectedBuildNode!.Id)
                .ToHashSet();

            foreach (BuildSelectionSlotViewModel other in BuildSlots
                .Where(s => s != slot && s.SelectedBuildNode != null && ancestorIds.Contains(s.SelectedBuildNode.Id))
                .ToList())
            {
                other.SelectedBuildNode = null;
            }
        }

        // A selected build not being found in this game's own build tree shouldn't happen (it can only
        // have gotten into BuildSlots by having come from this same tree in the first place), but a
        // missing path here would otherwise throw and take the whole app down over one game's build
        // selection. Logged so a real inconsistency doesn't pass by silently, and treated as "no
        // ancestors/duplicates to worry about" so the user's action can still go through.
        private List<BuildNode> FindPathOrLog(BuildNode node)
        {
            List<BuildNode>? path = BuildPathHelper.FindPath(_buildTree, node.Id);
            if (path != null)
                return path;

            _logger.LogError($"Build \"{node.Name}\" (Id={node.Id}) selected for this game was not found in the current build tree.");
            return [];
        }

        private void UpdateSelectedBuildsSummary() =>
            SelectedBuildsSummary = string.Join("; ", BuildSlots
                .Where(s => s.SelectedBuildNode != null)
                .Select(s => s.SelectedBuildLabel));

        // Re-derives the attribute editors for the currently selected builds without changing any
        // selection — called after DataPageViewModel reloads the cached build tree, so an attribute
        // added to (or removed from) a selected build or one of its ancestors on the Builds tab is
        // picked up here on the Data tab too.
        public void RefreshAttributeEditors() => RebuildAttributeEditors();

        // Deduplicated union of every selected build's root-to-leaf path, so a shared ancestor
        // contributes its attributes exactly once no matter how many selected builds pass through it.
        private void RebuildAttributeEditors()
        {
            List<int> oldIds = AttributeEditors.Select(e => e.Definition.Id).ToList();

            List<BuildNode> unionPath = new();
            HashSet<int> seen = new();
            foreach (BuildNode leaf in BuildSlots.Select(s => s.SelectedBuildNode).OfType<BuildNode>())
            {
                List<BuildNode>? path = BuildPathHelper.FindPath(_buildTree, leaf.Id);
                if (path == null)
                    continue;
                foreach (BuildNode node in path)
                    if (seen.Add(node.Id))
                        unionPath.Add(node);
            }
            List<AttributeValue> newPathAttrs = BuildPathHelper.FlattenAttributes(unionPath);
            List<int> newIds = newPathAttrs.Select(a => a.Definition.Id).ToList();

            // Left every selected path: drop the stored value from the DB, but leave it in
            // _player.AttributeValues (in-memory) so re-selecting the build within this session restores it.
            foreach (int leftId in oldIds.Except(newIds))
                TryDeleteAttributeValue(leftId);

            AttributeEditors.Clear();
            foreach (AttributeValue template in newPathAttrs)
            {
                AttributeValue editor = template.Clone();
                GameAttributeValue? cached = _player.AttributeValues.FirstOrDefault(v => v.BuildAttributeId == template.Definition.Id);
                if (cached != null)
                {
                    editor.ApplyStoredValue(cached.Value);
                    TryUpsertAttributeValue(template.Definition.Id, cached.Value);
                }
                else
                {
                    string defaultValue = editor.Serialize() ?? "";
                    _player.AttributeValues.Add(new GameAttributeValue { BuildAttributeId = template.Definition.Id, Value = defaultValue });
                    TryUpsertAttributeValue(template.Definition.Id, defaultValue);
                }

                editor.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(AttributeValue.NumericValue)
                        or nameof(AttributeValue.BoolValue)
                        or nameof(AttributeValue.PercentValue)
                        or nameof(AttributeValue.SelectedValue))
                    {
                        string value = editor.Serialize() ?? "";
                        GameAttributeValue? existing = _player.AttributeValues.FirstOrDefault(v => v.BuildAttributeId == template.Definition.Id);
                        if (existing != null)
                            existing.Value = value;
                        else
                            _player.AttributeValues.Add(new GameAttributeValue { BuildAttributeId = template.Definition.Id, Value = value });
                        TryUpsertAttributeValue(template.Definition.Id, value);
                    }
                };
                AttributeEditors.Add(editor);
            }
        }
    }
}
