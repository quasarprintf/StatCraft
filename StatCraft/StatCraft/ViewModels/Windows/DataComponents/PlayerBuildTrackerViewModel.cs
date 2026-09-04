using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
using StatCraft.Services.BackgroundService;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace StatCraft.ViewModels.Windows.DataComponents
{
    // Owns one player's build selection(s) and derived attribute editors within a game — reused for the
    // session user as well as every ally/opponent, since GameBuilds/BuildDetailValues are tied to a
    // GamePlayer's own id, not to the game as a whole.
    public partial class PlayerBuildTrackerViewModel : ViewModelBase
    {
        public string TabHeader => _player.Name;

        // Null for self player, since only allies/opponents are tabulated
        [ObservableProperty] private IBrush? _nameColor;

        private readonly GamePlayer _player;
        private readonly GameDataRepository _repository;
        private readonly ObservableCollection<BuildNode> _buildTree;
        private readonly ILogger _logger;
        private readonly bool _isAlly;
        private bool _useTeamColors;

        // The color NameColor falls back to when team colors are off — kept even while team colors are
        // on, resolving in the background same as always, so toggling the setting back off has an
        // up-to-date color ready immediately instead of needing a fresh replay re-read.
        private IBrush? _replayNameColor => _player?.ColorArgb == null ? null : Styles.Colors.FromArgb(_player.ColorArgb.Value);

        [ObservableProperty] private string _selectedBuildsSummary = "";

        // Slots are always [...persisted selections, one trailing blank] — selecting a build in the
        // trailing slot appends a new blank after it, and clearing a non-trailing slot removes it.
        public ObservableCollection<BuildSelectionSlotViewModel> BuildSlots { get; } = [];
        public ObservableCollection<AttributeGroupViewModel> AttributeGroups { get; } = [];

        internal PlayerBuildTrackerViewModel(GamePlayer player, GameDataRepository repository, ObservableCollection<BuildNode>? buildTree, ILogger logger,
            ReplayDataExtractor? replayDataExtractor = null, string? replayPath = null, bool useTeamColors = false, bool isAlly = false)
        {
            _player = player;
            _repository = repository;
            _buildTree = buildTree ?? [];
            _logger = logger;
            _useTeamColors = useTeamColors;
            _isAlly = isAlly;

            if (!player.ColorArgb.HasValue && replayDataExtractor != null && replayPath != null)
                _ = ResolveNameColorFromReplayAsync(replayDataExtractor, replayPath);
            UpdateNameColor();

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

        // Best-effort: an old replay that's since been moved or deleted just leaves NameColor null
        // (falls back to the default tab foreground) rather than failing anything else about the row.
        private async Task ResolveNameColorFromReplayAsync(ReplayDataExtractor replayDataExtractor, string replayPath)
        {
            int? colorArgb = await replayDataExtractor.TryResolvePlayerColorAsync(replayPath, _player.Name);
            if (colorArgb == null)
            {
                _logger.LogWarning($"Could not resolve in-game color for \"{_player.Name}\" from replay: {replayPath}");
                return;
            }

            _player.ColorArgb = colorArgb;
            if (_player.GamePlayerId.HasValue)
                _repository.UpdateGamePlayerColor(_player.GamePlayerId.Value, colorArgb.Value);

            Dispatcher.UIThread.Post(() =>
            {
                UpdateNameColor();
            });
        }

        private void UpdateNameColor() =>
            NameColor = _useTeamColors ? (_isAlly ? Styles.Colors.AllyYellow : Styles.Colors.OpponentRed) : _replayNameColor;

        // Called by GameDataRowViewModel when the "Use Team Colors" setting changes, so an already-open
        // row's tabs update immediately instead of waiting for the row to be rebuilt.
        public void SetUseTeamColors(bool useTeamColors)
        {
            if (_useTeamColors == useTeamColors)
                return;
            _useTeamColors = useTeamColors;
            UpdateNameColor();
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

        private void UpdateSelectedBuildsSummary()
        {
            List<List<BuildNode>> paths = BuildSlots
                .Select(s => s.SelectedBuildNode)
                .OfType<BuildNode>()
                .Select(FindPathOrLog)
                .Where(p => p.Count > 0)
                .ToList();

            SelectedBuildsSummary = FormatBuildsSummary(paths);
        }

        // Selected builds sharing a common ancestor (e.g. "A > B > C", "A > X > Y" and "A > X > Z" — two
        // plans branching off the same early game, one of which branches again later) collapse every
        // shared prefix, at every depth, to "A > B > C, X > Y, Z" instead of repeating each one — this is
        // what keeps the Data tab's Build column from growing with every extra build tried off a shared
        // opening. Done by merging the paths into a prefix tree and rendering each branch point: a node
        // with one child just continues the chain, one with several lists them comma-separated.
        private static string FormatBuildsSummary(List<List<BuildNode>> paths)
        {
            TrieNode root = new(null);
            foreach (List<BuildNode> path in paths)
            {
                TrieNode current = root;
                foreach (BuildNode node in path)
                {
                    TrieNode? child = current.Children.FirstOrDefault(c => c.Build!.Id == node.Id);
                    if (child == null)
                    {
                        child = new TrieNode(node);
                        current.Children.Add(child);
                    }
                    current = child;
                }
            }

            // Top-level entries with no shared ancestor at all read as distinct build choices, so they're
            // separated by "; " rather than ", " — that's reserved for branches that do share a history.
            return string.Join("; ", root.Children.Select(RenderBuildSubtree));
        }

        private static string RenderBuildSubtree(TrieNode node)
        {
            if (node.Children.Count == 0)
                return node.Build!.Name;
            return $"{node.Build!.Name} > {string.Join(", ", node.Children.Select(RenderBuildSubtree))}";
        }

        // Build is null only for the synthetic root every selected path's own root node is attached under.
        private sealed class TrieNode(BuildNode? build)
        {
            public BuildNode? Build { get; } = build;
            public List<TrieNode> Children { get; } = [];
        }

        // Re-derives the attribute editors for the currently selected builds without changing any
        // selection — called after DataPageViewModel reloads the cached build tree, so an attribute
        // added to (or removed from) a selected build or one of its ancestors on the Builds tab is
        // picked up here on the Data tab too.
        public void RefreshAttributeEditors() => RebuildAttributeEditors();

        // Deduplicated union of every selected build's root-to-leaf path, so a shared ancestor
        // contributes its attributes exactly once no matter how many selected builds pass through it.
        // Grouped by owning build (rather than flattened into one list) so the Data tab can show which
        // build each attribute came from — Depth is each node's position within whichever selected
        // path first reached it, root being 0, which is what lets the view indent a nested build's
        // group further than its ancestor's.
        private void RebuildAttributeEditors()
        {
            List<int> oldIds = AttributeGroups.SelectMany(g => g.Attributes).Select(a => a.Definition.Id).ToList();

            List<(BuildNode Node, int Depth)> unionPath = new();
            HashSet<int> seen = new();
            foreach (BuildNode leaf in BuildSlots.Select(s => s.SelectedBuildNode).OfType<BuildNode>())
            {
                List<BuildNode>? path = BuildPathHelper.FindPath(_buildTree, leaf.Id);
                if (path == null)
                    continue;
                for (int depth = 0; depth < path.Count; depth++)
                    if (seen.Add(path[depth].Id))
                        unionPath.Add((path[depth], depth));
            }
            List<int> newIds = unionPath.SelectMany(p => p.Node.Details).Select(a => a.Id).ToList();

            // Left every selected path: drop the stored value from the DB, but leave it in
            // _player.AttributeValues (in-memory) so re-selecting the build within this session restores it.
            foreach (int leftId in oldIds.Except(newIds))
                TryDeleteAttributeValue(leftId);

            AttributeGroups.Clear();
            foreach ((BuildNode node, int depth) in unionPath)
            {
                if (node.Details.Count == 0)
                    continue; // nothing to show for this build — no group at all, rather than an empty one

                ObservableCollection<AttributeValue> groupEditors = [];
                foreach (AttributeDefinition template in node.Details)
                {
                    AttributeValue editor = template.DefaultValue.Clone();
                    GameAttributeValue? cached = _player.AttributeValues.FirstOrDefault(v => v.BuildAttributeId == template.Id);
                    if (cached != null)
                    {
                        editor.ApplyStoredValue(cached.Value);
                    }
                    else
                    {
                        string defaultValue = editor.Serialize() ?? "";
                        _player.AttributeValues.Add(new GameAttributeValue { BuildAttributeId = template.Id, Value = defaultValue });
                        TryUpsertAttributeValue(template.Id, defaultValue);
                    }

                    editor.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName is nameof(AttributeValue.NumericValue)
                            or nameof(AttributeValue.BoolValue)
                            or nameof(AttributeValue.PercentValue)
                            or nameof(AttributeValue.SelectedValue))
                        {
                            string value = editor.Serialize() ?? "";
                            GameAttributeValue? existing = _player.AttributeValues.FirstOrDefault(v => v.BuildAttributeId == template.Id);
                            if (existing != null)
                                existing.Value = value;
                            else
                                _player.AttributeValues.Add(new GameAttributeValue { BuildAttributeId = template.Id, Value = value });
                            TryUpsertAttributeValue(template.Id, value);
                        }
                    };
                    groupEditors.Add(editor);
                }

                AttributeGroups.Add(new AttributeGroupViewModel(node.Name, depth, groupEditors));
            }
        }
    }
}
