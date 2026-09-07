using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.BackgroundService;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;
using StatCraft.Styles;
using StatCraft.ViewModels.Windows.AttributeComponents;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace StatCraft.ViewModels.Windows.DataComponents.GameRow;

// Wraps one GameData for display/editing in the Data page's table. Every public member is a plain
// scalar or a public-typed collection, so GameData/ParsedReplayData (both internal) never leak
// through a public property.
public partial class GameDataRowViewModel : ViewModelBase
{
    public event EventHandler? RenderHeightChanged;
    [ObservableProperty] private bool _buildsVisible;
    [ObservableProperty] private bool _attributesVisible;

    private readonly GameDataRepository _repository;
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
                return [new ColoredCharacter(self.Mmr.Mmr.ToString())];

            IBrush changeColor = change switch
            {
                > 0 => Styles.Colors.WinGreen,
                < 0 => Styles.Colors.LossRed,
                _ => Brushes.Gray,
            };

            return [new ColoredCharacter(after.ToString()), new ColoredCharacter($"({change:+#;-#;0})", changeColor)];
        }
    }

    [ObservableProperty] private string _notes;
    public IReadOnlyList<ColoredCharacter> MatchupCharacters { get; }
    public ObservableCollection<OpponentRowViewModel> Opponents { get; } = [];

    public PlayerBuildTrackerViewModel SelfTracker { get; }
    public ObservableCollection<PlayerBuildTrackerViewModel> OtherPlayers { get; } = [];
    public AttributeValuesSelectViewModel AttributeValuesSelect { get; set; }
    public string AttributesSummary => ""; //TODO

    internal GameDataRowViewModel(GameData game, GameDataRepository repository, ObservableCollection<AttributeDefinition> allAttributes, string profileLabel,
        Func<Race?, Matchups, ObservableCollection<BuildNode>?> getBuildTree, ILogger logger, ReplayDataExtractor replayDataExtractor,
        bool useTeamColors = false)
    {
        _game = game;
        _repository = repository;
        ProfileLabel = profileLabel;

        AttributeValuesSelect = new AttributeValuesSelectViewModel(allAttributes);
        AttributeValuesSelect.ValueChanged += AttributeValueChanged;
        AttributeValuesSelect.ValueDeleted += AttributeValueDeleted;
        AttributeValuesSelect.Object = game;

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
        MatchupCharacters = BuildMatchupCharacters(replay);
        foreach (GamePlayer opponent in replay.Opponents)
            Opponents.Add(new OpponentRowViewModel(opponent, repository));
        _notes = game.Notes;

        // Allies share the self player's own opponents (same enemy team), so their build tree uses
        // the same matchup; opponents face the self player's team instead, so their matchup is
        // resolved from the reverse side.
        Matchups selfSideMatchups = MatchupResolver.FromOpponents(replay.Opponents);
        Matchups opponentSideMatchups = MatchupResolver.FromOpponents([replay.Player, .. replay.Allies]);

        ObservableCollection<BuildNode>? selfBuildTree = getBuildTree(replay.Player.Race.AsRace(), selfSideMatchups);
        SelfTracker = new PlayerBuildTrackerViewModel(replay.Player, repository, selfBuildTree, logger, useTeamColors: useTeamColors);
        SelfTracker.BuildSlots.CollectionChanged += (_,_) => RenderHeightChanged?.Invoke(this, EventArgs.Empty);

        foreach (GamePlayer ally in replay.Allies)
        {
            ObservableCollection<BuildNode>? buildTree = getBuildTree(ally.Race.AsRace(), selfSideMatchups);
            var buildTracker = new PlayerBuildTrackerViewModel(ally, repository, buildTree, logger, replayDataExtractor, replay.ReplayPath, useTeamColors, isAlly: true);
            OtherPlayers.Add(buildTracker);
            buildTracker.BuildSlots.CollectionChanged += (_,_) => RenderHeightChanged?.Invoke(this, EventArgs.Empty);
        }
        foreach (GamePlayer opponent in replay.Opponents)
        {
            ObservableCollection<BuildNode>? buildTree = getBuildTree(opponent.Race.AsRace(), opponentSideMatchups);
            var buildTracker = new PlayerBuildTrackerViewModel(opponent, repository, buildTree, logger, replayDataExtractor, replay.ReplayPath, useTeamColors, isAlly: false);
            OtherPlayers.Add(buildTracker);
            buildTracker.BuildSlots.CollectionChanged += (_,_) => RenderHeightChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    partial void OnBuildsVisibleChanged(bool value)
    {
        RenderHeightChanged?.Invoke(this, EventArgs.Empty);
    }
    partial void OnAttributesVisibleChanged(bool value)
    {
        RenderHeightChanged?.Invoke(this, EventArgs.Empty);
    }
    private void AttributeValueDeleted(object? s, AttributeValue value)
    {
        if (s is not GameData game)
            return; //TODO: log this, it's unexpected

        // A null value deletes the row — see GameDataRepository.SaveGameAttributeValue.
        _repository.SaveGameAttributeValue(game.GameId!.Value, value.Definition.Id, null);
        RenderHeightChanged?.Invoke(this, EventArgs.Empty);
    }
    private void AttributeValueChanged(object? s, AttributeValue value)
    {
        if (s is not GameData game)
            return; //TODO: log this, it's unexpected

        _repository.SaveGameAttributeValue(game.GameId!.Value, value.Definition.Id, value.Serialize());
        RenderHeightChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnNotesChanged(string value)
    {
        _game.Notes = value;
        _repository.UpdateGameNotes(_game.GameId!.Value, value);
    }

    partial void OnGameTypeChanged(GameType value)
    {
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

    public void RefreshTeamColors(bool useTeamColors)
    {
        foreach (PlayerBuildTrackerViewModel other in OtherPlayers)
            other.SetUseTeamColors(useTeamColors);
    }

    private static List<ColoredCharacter> BuildMatchupCharacters(ParsedReplayData replay)
    {
        List<ColoredCharacter> characters = new();

        void AddRace(char race) => characters.Add(new ColoredCharacter(race.ToString(), RaceColor(race)));

        AddRace(replay.Player.Race);
        foreach (GamePlayer ally in replay.Allies)
            AddRace(ally.Race);
        characters.Add(new ColoredCharacter("v", Brushes.Black));
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
