using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;

namespace StatCraft.ViewModels
{
    // Wraps one GameData for display/editing in the Data page's table. Every public member is a plain
    // scalar or a public-typed collection, so GameData/ParsedReplayData (both internal) never leak
    // through a public property.
    public partial class GameDataRowViewModel : ViewModelBase
    {
        private readonly GameData _game;
        private readonly GameDataRepository _repository;

        public string MapName { get; }
        public string ResultLabel { get; }
        public string GameLength { get; }
        public string PlayerRace { get; }
        public string OpponentSummary { get; }
        public bool IsBuildPickerEnabled { get; }

        [ObservableProperty] private string _notes;
        [ObservableProperty] private BuildNode? _selectedBuildNode;
        [ObservableProperty] private string _selectedBuildLabel = "(no build selected)";

        public ObservableCollection<BuildNode> BuildTree { get; }
        public ObservableCollection<GameAttributeEditorViewModel> AttributeEditors { get; } = [];

        internal GameDataRowViewModel(GameData game, GameDataRepository repository, ObservableCollection<BuildNode>? buildTree)
        {
            _game = game;
            _repository = repository;
            BuildTree = buildTree ?? [];
            IsBuildPickerEnabled = buildTree != null;

            ParsedReplayData replay = game.ReplayData;
            MapName = replay.MapName;
            ResultLabel = replay.Win == 1m ? "Win" : replay.Win == 0m ? "Loss" : "Draw";
            GameLength = TimeSpan.FromSeconds(replay.GameLengthSeconds).ToString(@"mm\:ss");
            PlayerRace = replay.Player.Race.ToString();
            OpponentSummary = string.Join(", ", replay.Opponents.Select(o => $"{o.Name} ({o.Race})"));
            _notes = game.Notes;

            // Setting SelectedBuildNode (when a build was previously saved) triggers OnSelectedBuildNodeChanged
            // below, which sets SelectedBuildLabel and populates AttributeEditors. If there's no saved build,
            // the field initializers above already leave things in the correct "nothing selected" state.
            if (game.BuildId.HasValue)
                SelectedBuildNode = BuildPathHelper.FindPath(BuildTree, game.BuildId.Value)?.LastOrDefault();
        }

        partial void OnNotesChanged(string value) => _repository.UpdateGameNotes(_game.GameId!.Value, value);

        partial void OnSelectedBuildNodeChanged(BuildNode? oldValue, BuildNode? newValue)
        {
            _game.BuildId = newValue?.Id;
            _repository.UpdateGameBuild(_game.GameId!.Value, newValue?.Id);
            SelectedBuildLabel = newValue == null ? "(no build selected)" : BuildLabel(newValue);
            RebuildAttributeEditors(oldValue, newValue);
        }

        private string BuildLabel(BuildNode node) =>
            string.Join(" > ", BuildPathHelper.FindPath(BuildTree, node.Id)!.Select(n => n.Name));

        private void RebuildAttributeEditors(BuildNode? oldSelection, BuildNode? newSelection)
        {
            List<int> oldIds = oldSelection == null
                ? []
                : BuildPathHelper.FlattenAttributes(BuildPathHelper.FindPath(BuildTree, oldSelection.Id)!).Select(a => a.Id).ToList();

            List<BuildAttribute> newPathAttrs = newSelection == null
                ? []
                : BuildPathHelper.FlattenAttributes(BuildPathHelper.FindPath(BuildTree, newSelection.Id)!);

            List<int> newIds = newPathAttrs.Select(a => a.Id).ToList();

            // Left the path: drop the stored value from the DB, but leave it in _game.AttributeValues
            // (in-memory) so switching back within this session restores it.
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
