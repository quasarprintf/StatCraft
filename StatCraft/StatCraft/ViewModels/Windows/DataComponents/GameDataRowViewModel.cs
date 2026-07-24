using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        // A brighter, more saturated green than Brushes.Green — used for both a Win result and the
        // Protoss race letter.
        private static readonly IBrush VibrantGreen = new SolidColorBrush(Color.Parse("#00CC1B"));

        private readonly GameData _game;
        private readonly GameDataRepository _repository;

        public string MapName { get; }
        public string ResultLabel { get; }
        public IBrush ResultColor { get; }
        public string GameLength { get; }
        public string Matchup { get; }
        public IReadOnlyList<ColoredCharacter> MatchupCharacters { get; }
        public string OpponentName { get; }
        public bool IsBuildPickerEnabled { get; }

        [ObservableProperty] private string _notes;
        [ObservableProperty] private BuildNode? _selectedBuildNode;
        [ObservableProperty] private string _selectedBuildLabel = DEFAULT_BUILD_TEXT;

        private readonly static string DEFAULT_BUILD_TEXT = "";

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
            ResultColor = replay.Win == 1m ? VibrantGreen : replay.Win == 0m ? Brushes.Red : Brushes.Blue;
            GameLength = TimeSpan.FromSeconds(replay.GameLengthSeconds).ToString(@"mm\:ss");
            Matchup = $"{replay.Player.Race}{string.Concat(replay.Allies.Select(a => a.Race))}v{string.Concat(replay.Opponents.Select(o => o.Race))}";
            MatchupCharacters = BuildMatchupCharacters(replay);
            OpponentName = string.Join(", ", replay.Opponents.Select(o => $"{o.FormattedClan} {o.Name}"));
            _notes = game.Notes;

            // Setting SelectedBuildNode (when a build was previously saved) triggers OnSelectedBuildNodeChanged
            // below, which sets SelectedBuildLabel and populates AttributeEditors. If there's no saved build,
            // the field initializers above already leave things in the correct "nothing selected" state.
            if (game.BuildId.HasValue)
                SelectedBuildNode = BuildPathHelper.FindPath(BuildTree, game.BuildId.Value)?.LastOrDefault();
        }

        partial void OnNotesChanged(string value) => _repository.UpdateGameNotes(_game.GameId!.Value, value);

        [RelayCommand]
        private void SelectBuild(BuildNode node) => SelectedBuildNode = node;

        partial void OnSelectedBuildNodeChanged(BuildNode? oldValue, BuildNode? newValue)
        {
            _game.BuildId = newValue?.Id;
            _repository.UpdateGameBuild(_game.GameId!.Value, newValue?.Id);
            SelectedBuildLabel = newValue == null ? DEFAULT_BUILD_TEXT : BuildLabel(newValue);
            RebuildAttributeEditors(oldValue, newValue);
        }

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
            'P' => VibrantGreen,
            'T' => Brushes.Blue,
            'Z' => Brushes.Red,
            _ => Brushes.Gray,
        };

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
