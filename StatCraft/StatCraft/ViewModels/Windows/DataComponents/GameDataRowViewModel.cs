using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
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

        public string MapName { get; }
        public string PlayedAt { get; }
        public string ResultLabel { get; }
        public IBrush ResultColor { get; }
        public string GameLength { get; }
        public string Matchup { get; }
        public IReadOnlyList<ColoredCharacter> MatchupCharacters { get; }
        public string OpponentName { get; }

        [ObservableProperty] private string _notes;

        // Left side of the row-details split: the session user's own build selection.
        public PlayerBuildTrackerViewModel SelfTracker { get; }

        // Right side: one tab per ally/opponent, each with their own build selection.
        public ObservableCollection<OtherPlayerBuildViewModel> OtherPlayers { get; } = [];

        private readonly GameDataRepository _repository;

        internal GameDataRowViewModel(GameData game, GameDataRepository repository,
            Func<Race?, Matchups, ObservableCollection<BuildNode>?> getBuildTree)
        {
            _game = game;
            _repository = repository;

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

            // Allies share the self player's own opponents (same enemy team), so their build tree uses
            // the same matchup; opponents face the self player's team instead, so their matchup is
            // resolved from the reverse side.
            Matchups selfSideMatchups = MatchupResolver.FromOpponents(replay.Opponents);
            Matchups opponentSideMatchups = MatchupResolver.FromOpponents([replay.Player, .. replay.Allies]);

            SelfTracker = new PlayerBuildTrackerViewModel(replay.Player, repository, getBuildTree(replay.Player.Race.AsRace(), selfSideMatchups));

            foreach (GamePlayer ally in replay.Allies)
                OtherPlayers.Add(new OtherPlayerBuildViewModel("Ally", ally, repository, getBuildTree(ally.Race.AsRace(), selfSideMatchups)));
            foreach (GamePlayer opponent in replay.Opponents)
                OtherPlayers.Add(new OtherPlayerBuildViewModel("Opponent", opponent, repository, getBuildTree(opponent.Race.AsRace(), opponentSideMatchups)));
        }

        partial void OnNotesChanged(string value) => _repository.UpdateGameNotes(_game.GameId!.Value, value);

        // Re-derives every player's attribute editors for their currently selected builds without
        // changing any selection — called after DataPageViewModel reloads the cached build tree, so an
        // attribute added to (or removed from) a selected build or one of its ancestors on the Builds tab
        // is picked up here on the Data tab too.
        public void RefreshAttributeEditors()
        {
            SelfTracker.RefreshAttributeEditors();
            foreach (OtherPlayerBuildViewModel other in OtherPlayers)
                other.Tracker.RefreshAttributeEditors();
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
            'P' => Styles.Colors.ProtossGreen,
            'T' => Styles.Colors.TerranBlue,
            'Z' => Styles.Colors.ZergRed,
            _ => Brushes.Gray,
        };
    }
}
