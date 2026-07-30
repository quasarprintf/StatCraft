using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;
using StatCraft.Styles;

namespace StatCraft.ViewModels
{
    // Wraps one GameData for display/editing in the Data page's table. Every public member is a plain
    // scalar or a public-typed collection, so GameData/ParsedReplayData (both internal) never leak
    // through a public property.
    public partial class GameDataRowViewModel : ViewModelBase
    {
        private readonly GameData _game;
        private readonly GameDataRepository _repository;
        private readonly ObservableCollection<BuildNode> _buildTree;

        public string MapName { get; }
        public string PlayedAt { get; }
        public string ResultLabel { get; }
        public IBrush ResultColor { get; }
        public string GameLength { get; }
        public string Matchup { get; }
        public IReadOnlyList<ColoredCharacter> MatchupCharacters { get; }
        public string OpponentName { get; }

        [ObservableProperty] private string _notes;
        [ObservableProperty] private string _selectedBuildsSummary = "";

        // Slots are always [...persisted selections, one trailing blank] — selecting a build in the
        // trailing slot appends a new blank after it, and clearing a non-trailing slot removes it.
        public ObservableCollection<BuildSelectionSlotViewModel> BuildSlots { get; } = [];
        public ObservableCollection<GameAttributeEditorViewModel> AttributeEditors { get; } = [];

        internal GameDataRowViewModel(GameData game, GameDataRepository repository, ObservableCollection<BuildNode>? buildTree)
        {
            _game = game;
            _repository = repository;
            _buildTree = buildTree ?? [];

            ParsedReplayData replay = game.ReplayData;
            MapName = replay.MapName;
            PlayedAt = replay.ReplayTimestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            ResultLabel = replay.Win == 1m ? "Win" : replay.Win == 0m ? "Loss" : "Draw";
            ResultColor = replay.Win == 1m ? Styles.Colors.ProtossGreen : replay.Win == 0m ? Styles.Colors.ZergRed : Styles.Colors.TerranBlue;
            GameLength = TimeSpan.FromSeconds(replay.GameLengthSeconds).ToString(@"mm\:ss");
            Matchup = $"{replay.Player.Race}{string.Concat(replay.Allies.Select(a => a.Race))}v{string.Concat(replay.Opponents.Select(o => o.Race))}";
            MatchupCharacters = BuildMatchupCharacters(replay);
            OpponentName = string.Join(", ", replay.Opponents.Select(o => $"{o.FormattedClan} {o.Name}"));
            _notes = game.Notes;

            if (buildTree == null)
            {
                // Matchup couldn't be resolved (e.g. FFA/unusual team layout) — show a single disabled
                // picker, same as before multi-select existed.
                BuildSlots.Add(new BuildSelectionSlotViewModel(null));
            }
            else
            {
                foreach (int buildId in game.BuildIds)
                {
                    BuildNode? node = BuildPathHelper.FindPath(_buildTree, buildId)?.LastOrDefault();
                    if (node == null)
                        continue; // build was deleted since this game was tagged

                    // Set SelectedBuildNode before subscribing SelectionChanged, so hydrating a saved
                    // selection doesn't trigger the append/remove/persist logic meant for user edits.
                    BuildSelectionSlotViewModel slot = new(buildTree) { SelectedBuildNode = node };
                    slot.SelectionChanged += OnSlotSelectionChanged;
                    BuildSlots.Add(slot);
                }
                AppendBlankSlot();
            }

            UpdateSelectedBuildsSummary();
            RebuildAttributeEditors();
        }

        partial void OnNotesChanged(string value) => _repository.UpdateGameNotes(_game.GameId!.Value, value);

        private void AppendBlankSlot()
        {
            BuildSelectionSlotViewModel slot = new(_buildTree);
            slot.SelectionChanged += OnSlotSelectionChanged;
            BuildSlots.Add(slot);
        }

        private void OnSlotSelectionChanged(BuildSelectionSlotViewModel slot, BuildNode? previousValue)
        {
            // GameBuilds has a UNIQUE(GameId, BuildId) constraint — picking a build that's already
            // selected in another slot would violate it on persist. Revert this slot instead of letting
            // that happen; reverting fires this handler again with the (already-valid) previous value.
            if (slot.SelectedBuildNode != null &&
                BuildSlots.Any(s => s != slot && s.SelectedBuildNode?.Id == slot.SelectedBuildNode.Id))
            {
                slot.SelectedBuildNode = previousValue;
                return;
            }

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
            _game.BuildIds = buildIds;
            _repository.UpdateGameBuilds(_game.GameId!.Value, buildIds);

            UpdateSelectedBuildsSummary();
            RebuildAttributeEditors();
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

        private static List<ColoredCharacter> BuildMatchupCharacters(ParsedReplayData replay)
        {
            List<ColoredCharacter> characters = new();

            void AddRace(char race) => characters.Add(new ColoredCharacter(race.ToString(), RaceColor(race)));

            AddRace(replay.Player.Race);
            foreach (GamePlayer ally in replay.Allies)
                AddRace(ally.Race);
            characters.Add(new ColoredCharacter("v", Brushes.Gray));
            foreach (GamePlayer opponent in replay.Opponents)
                AddRace(opponent.Race);

            return characters;
        }

        private static IBrush RaceColor(char race) => race switch
        {
            'P' => Styles.Colors.ProtossGreen,
            'T' => Styles.Colors.TerranBlue,
            'Z' => Styles.Colors.ZergRed,
            _ => Brushes.Gray,
        };

        // Deduplicated union of every selected build's root-to-leaf path, so a shared ancestor
        // contributes its attributes exactly once no matter how many selected builds pass through it.
        private void RebuildAttributeEditors()
        {
            List<int> oldIds = AttributeEditors.Select(e => e.BuildAttributeId).ToList();

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
            List<BuildAttribute> newPathAttrs = BuildPathHelper.FlattenAttributes(unionPath);
            List<int> newIds = newPathAttrs.Select(a => a.Id).ToList();

            // Left every selected path: drop the stored value from the DB, but leave it in
            // _game.AttributeValues (in-memory) so re-selecting the build within this session restores it.
            foreach (int leftId in oldIds.Except(newIds))
                _repository.DeleteAttributeValue(_game.GameId!.Value, leftId);

            AttributeEditors.Clear();
            foreach (BuildAttribute template in newPathAttrs)
            {
                GameAttributeEditorViewModel editor = new(template);
                GameAttributeValue? cached = _game.AttributeValues.FirstOrDefault(v => v.BuildAttributeId == template.Id);
                if (cached != null)
                {
                    editor.ApplyValue(cached.Value);
                    _repository.UpsertAttributeValue(_game.GameId!.Value, template.Id, cached.Value);
                }

                editor.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is nameof(GameAttributeEditorViewModel.NumericValue)
                        or nameof(GameAttributeEditorViewModel.BoolValue)
                        or nameof(GameAttributeEditorViewModel.PercentValue)
                        or nameof(GameAttributeEditorViewModel.SelectedValue))
                    {
                        string value = editor.SerializeValue();
                        GameAttributeValue? existing = _game.AttributeValues.FirstOrDefault(v => v.BuildAttributeId == template.Id);
                        if (existing != null)
                            existing.Value = value;
                        else
                            _game.AttributeValues.Add(new GameAttributeValue { BuildAttributeId = template.Id, Value = value });
                        _repository.UpsertAttributeValue(_game.GameId!.Value, template.Id, value);
                    }
                };
                AttributeEditors.Add(editor);
            }
        }
    }
}
