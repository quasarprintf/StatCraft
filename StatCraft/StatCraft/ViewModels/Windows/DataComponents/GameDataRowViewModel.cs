using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.BackgroundService;
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

        public int GameId => _game.GameId!.Value;

        public string ReplayPath => _game.ReplayData.ReplayPath;

        // Which profile this game belongs to — meaningful once the Data tab's profile filter can merge
        // games from more than one profile into a single table.
        public string ProfileLabel { get; }

        public string MapName { get; }
        public string PlayedAt { get; }
        public string ResultLabel { get; }
        public IBrush ResultColor { get; }
        public string GameLength { get; }

        // User-overridable. Ranked vs Unranked is inferred rather than read from the replay, so it can be wrong
        [ObservableProperty] private GameType _gameType;

        public IReadOnlyList<GameType> GameTypeOptions => AllGameTypes;

        private static readonly GameType[] AllGameTypes = Enum.GetValues<GameType>();

        // Post-game rating as "3024(+24)", with only the delta coloured — the rating itself is left
        // uncoloured so it reads as ordinary text in whichever theme is active. Empty until the rating
        // has been resolved, and permanently empty for games where it never can be (unranked, team
        // games, or no saved API credentials). Not get-only-at-construction like the rest: MMR arrives
        // asynchronously minutes after the row already exists, so it's computed on demand and
        // re-announced by RefreshMmrChange.
        public IReadOnlyList<ColoredCharacter> MmrText
        {
            get
            {
                GamePlayer self = _game.ReplayData.Player;
                if (self.MmrAfter is not { } after || self.MmrChange is not { } change)
                    return [new ColoredCharacter(self.Mmr.ToString())];

                IBrush changeColor = change switch
                {
                    > 0 => Styles.Colors.WinGreen,
                    < 0 => Styles.Colors.LossRed,
                    _ => Brushes.Gray,
                };

                return [new ColoredCharacter(after.ToString()), new ColoredCharacter($"({change:+#;-#;0})", changeColor)];
            }
        }

        public string Matchup { get; }
        public IReadOnlyList<ColoredCharacter> MatchupCharacters { get; }
        public string OpponentName { get; }

        [ObservableProperty] private string _notes;

        // Left side of the row-details split: the session user's own build selection.
        public PlayerBuildTrackerViewModel SelfTracker { get; }

        // Right side: one tab per ally/opponent, each with their own build selection.
        public ObservableCollection<PlayerBuildTrackerViewModel> OtherPlayers { get; } = [];

        private readonly GameDataRepository _repository;

        internal GameDataRowViewModel(GameData game, GameDataRepository repository, string profileLabel,
            Func<Race?, Matchups, ObservableCollection<BuildNode>?> getBuildTree, ILogger logger, ReplayDataExtractor replayDataExtractor)
        {
            _game = game;
            _repository = repository;
            ProfileLabel = profileLabel;

            ParsedReplayData replay = game.ReplayData;
            MapName = game.Map?.Name ?? "";
            PlayedAt = replay.ReplayTimestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            GameOutcome outcome = GameOutcomeExtensions.FromWin(replay.Win);
            ResultLabel = outcome switch { GameOutcome.Win => "Win", GameOutcome.Loss => "Loss", _ => "Draw" };
            ResultColor = outcome switch
            {
                GameOutcome.Win => Styles.Colors.WinGreen,
                GameOutcome.Loss => Styles.Colors.LossRed,
                _ => Styles.Colors.DrawBlue,
            };
            GameLength = TimeSpan.FromSeconds(replay.GameLengthSeconds).ToString(@"mm\:ss");
            // Assigned to the backing field, not the property, so hydrating a row doesn't look like a
            // user edit and write straight back to the database (same reason as _notes below).
            _gameType = game.GameType;
            Matchup = $"{replay.Player.Race}{string.Concat(replay.Allies.Select(a => a.Race))}v{string.Concat(replay.Opponents.Select(o => o.Race))}";
            MatchupCharacters = BuildMatchupCharacters(replay);
            OpponentName = string.Join(", ", replay.Opponents.Select(o => $"({o.Mmr}) {o.FormattedClan}{o.Name}"));
            _notes = game.Notes;

            // Allies share the self player's own opponents (same enemy team), so their build tree uses
            // the same matchup; opponents face the self player's team instead, so their matchup is
            // resolved from the reverse side.
            Matchups selfSideMatchups = MatchupResolver.FromOpponents(replay.Opponents);
            Matchups opponentSideMatchups = MatchupResolver.FromOpponents([replay.Player, .. replay.Allies]);

            SelfTracker = new PlayerBuildTrackerViewModel(replay.Player, repository, getBuildTree(replay.Player.Race.AsRace(), selfSideMatchups), logger);

            foreach (GamePlayer ally in replay.Allies)
                OtherPlayers.Add(new PlayerBuildTrackerViewModel(ally, repository, getBuildTree(ally.Race.AsRace(), selfSideMatchups), logger, replayDataExtractor, replay.ReplayPath));
            foreach (GamePlayer opponent in replay.Opponents)
                OtherPlayers.Add(new PlayerBuildTrackerViewModel(opponent, repository, getBuildTree(opponent.Race.AsRace(), opponentSideMatchups), logger, replayDataExtractor, replay.ReplayPath));
        }

        partial void OnNotesChanged(string value)
        {
            // Kept on the underlying GameData too, so anything re-reading it in this session (filters,
            // re-wrapped rows) sees the edit rather than the value the game was first loaded with — same
            // reason as OnGameTypeChanged below. Without this, a filter change after typing notes rebuilds
            // this row from the still-stale _game.Notes and the edit looks like it silently vanished, even
            // though it was correctly persisted to the DB the whole time.
            _game.Notes = value;
            _repository.UpdateGameNotes(_game.GameId!.Value, value);
        }

        partial void OnGameTypeChanged(GameType value)
        {
            // Kept on the underlying GameData too, so anything re-reading it in this session (filters,
            // re-wrapped rows) sees the override rather than the original inference.
            _game.GameType = value;
            _repository.UpdateGameType(_game.GameId!.Value, value);
        }

        // Re-derives every player's attribute editors for their currently selected builds without
        // changing any selection — called after DataPageViewModel reloads the cached build tree, so an
        // attribute added to (or removed from) a selected build or one of its ancestors on the Builds tab
        // is picked up here on the Data tab too.
        public void RefreshAttributeEditors()
        {
            SelfTracker.RefreshAttributeEditors();
            foreach (PlayerBuildTrackerViewModel other in OtherPlayers)
                other.RefreshAttributeEditors();
        }

        // Called once the post-game MMR poll resolves, since the underlying GamePlayer is mutated
        // directly rather than replaced and so raises no change notification of its own.
        public void RefreshMmrChange() => OnPropertyChanged(nameof(MmrText));

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
