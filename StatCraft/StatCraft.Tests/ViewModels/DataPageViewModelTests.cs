using System.Net.Http;
using System.Threading;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Maps;
using StatCraft.Models.GameData.Race;
using StatCraft.Models.Util;
using StatCraft.Services.BackgroundService;
using StatCraft.Services.BattlenetApi;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;
using StatCraft.Styles;
using StatCraft.Tests.Mocks;
using StatCraft.ViewModels.Windows;
using StatCraft.ViewModels.Windows.DataComponents;
using StatCraft.ViewModels.Windows.DataComponents.GameRow;
using StatCraft.ViewModels.Windows.Filters;

namespace StatCraft.Tests;

// Reproduces a real-session bug report: the Data tab's table would sometimes jump back to the top
// while the user was scrolled through it. DataPageViewModel.ApplyFilters used to Clear() and rebuild
// every GameDataRowViewModel from scratch on every reload — including the background reload that runs
// whenever a replay is imported (see OnGameParsed) — which raises a collection Reset the DataGrid
// responds to by resetting its scroll position, for reasons that had nothing to do with anything the
// user had touched. These pin the fix: reloading has to reuse the same row instance for a game that's
// still in view, only touching rows that actually entered or left the filtered set.
public class DataPageViewModelTests : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly GameDataRepository _gameDataRepository;
    private readonly MapRepository _mapRepository;
    private readonly AttributeRepository _attributeRepository;
    private readonly AccountRepository _accountRepository;
    private readonly BuildRepository _buildRepository;
    private readonly ReplayWatcherService _replayWatcherService;
    private readonly SettingsRepository _settingsRepository;
    private DataPageViewModel _viewModel;
    private readonly int _sc2ProfileId;
    private readonly Sc2Profile _profile;

    public DataPageViewModelTests()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempRoot);
        _dbPath = Path.Combine(tempRoot, "statcraft.db");
        string settingsPath = Path.Combine(tempRoot, "settings.json");

        _accountRepository = new AccountRepository(_dbPath);
        _accountRepository.Initialize();
        _buildRepository = new BuildRepository(_dbPath);
        _buildRepository.Initialize();
        // Before GameDataRepository, whose MapName -> MapId migration writes into the Maps table.
        _mapRepository = new MapRepository(_dbPath);
        _mapRepository.Initialize();
        _gameDataRepository = new GameDataRepository(_dbPath);
        _gameDataRepository.Initialize();
        _attributeRepository = new AttributeRepository(_dbPath);
        _attributeRepository.Initialize();

        BattleNetAccount account = new()
        {
            BattleTag = "Player#1234", AccountSub = "sub-1", EncryptedAccessToken = [1],
            TokenExpiresAtUtc = DateTimeOffset.UtcNow, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        _accountRepository.InsertAccount(account);
        _profile = new Sc2Profile { BattleNetAccountId = account.Id, RegionId = "1", RealmId = "1", ProfileId = 111, Name = "Player" };
        _accountRepository.UpsertProfile(_profile);
        _sc2ProfileId = _profile.Id;

        _settingsRepository = new SettingsRepository(settingsPath);
        _replayWatcherService = new ReplayWatcherService(new MockLogger());

        _viewModel = CreateViewModel();
    }

    // Separate from the constructor because some filters snapshot the database when the page is built —
    // the Build slot reads the whole build tree once — so a test needing those has to seed first and
    // then rebuild the page.
    private DataPageViewModel CreateViewModel()
    {
        Sc2LadderService ladderService = new(new HttpClient(), new StubTokenProvider(), new MockLogger());
        ReplayDataExtractor replayDataExtractor = new();
        ReplayImportService replayImportService = new(new MockLogger(), replayDataExtractor, _gameDataRepository,
            _mapRepository, ladderService);

        return new DataPageViewModel(_settingsRepository, _replayWatcherService, replayImportService,
            _accountRepository, _buildRepository, _gameDataRepository, _attributeRepository, ladderService,
            new MockLogger(), new StatCraft.Services.Factories.FilterSlotFactory(), replayDataExtractor);
    }

    // The "Use Team Colors" setting can be toggled mid-session — already-visible rows must pick it up
    // immediately (via DataPageViewModel.OnSettingsChanged -> GameDataRowViewModel.RefreshTeamColors)
    // rather than only the next time a row happens to get rebuilt.
    [Fact]
    public async Task TogglingUseTeamColors_AfterRowsAreAlreadyVisible_UpdatesTheirTabColorsImmediately()
    {
        InsertGame();
        await _viewModel.SetActiveProfile(_profile);

        GameDataRowViewModel row = Assert.Single(_viewModel.Games);
        PlayerBuildTrackerViewModel opponentTracker = Assert.Single(row.OtherPlayers);

        _settingsRepository.Save(new AppSettingsData { UseTeamColors = true });

        Assert.Same(Colors.OpponentRed, opponentTracker.NameColor);
    }

    [Fact]
    public async Task ReloadingGames_WithAnAdditionalGame_KeepsExistingRowsAsTheSameInstance()
    {
        GameData game1 = InsertGame();
        await _viewModel.SetActiveProfile(_profile);

        GameDataRowViewModel originalRow = Assert.Single(_viewModel.Games);
        Assert.Equal(game1.GameId, originalRow.GameId);

        InsertGame();
        await _viewModel.SetActiveProfile(_profile);

        Assert.Equal(2, _viewModel.Games.Count);
        GameDataRowViewModel? survivingRow = _viewModel.Games.FirstOrDefault(g => g.GameId == game1.GameId);
        Assert.Same(originalRow, survivingRow);
    }

    #region Filtering (ported from GameDataFilterTests)

    // These used to call GameDataFilter.Matches directly. The Games tab no longer uses it — ApplyFilters
    // runs DataPageFiltersViewModel.GetFilter() — so they go through the page itself instead: insert
    // games, load the profile, set the filters the way the filter bar would, and assert on what ends up
    // in Games. That way they cover whatever the tab actually filters with, not a parallel implementation.

    [Fact]
    public async Task Filter_NothingApplied_ShowsEveryGame()
    {
        InsertGame(map: InsertMap("Altitude LE"));
        InsertGame(map: InsertMap("Deathaura LE"), win: 0m);

        await LoadGamesWithNoDateRange();

        Assert.Equal(2, _viewModel.Games.Count);
    }

    [Theory]
    [InlineData(15, true)]
    [InlineData(10, false)]
    [InlineData(20, false)]
    public async Task Filter_DateRange_IsInclusiveOnBothEnds(int day, bool expected)
    {
        // Local noon, so the game sits squarely inside its calendar day in whatever timezone runs this.
        InsertGame(playedAt: new DateTimeOffset(new DateTime(2026, 1, day, 12, 0, 0, DateTimeKind.Local)));
        await LoadGamesWithNoDateRange();

        _viewModel.Filters.FromDate = new DateTime(2026, 1, 15);
        _viewModel.Filters.ToDate = new DateTime(2026, 1, 15);

        Assert.Equal(expected, _viewModel.Games.Count == 1);
    }

    [Fact]
    public async Task Filter_MapNotChecked_IsExcluded()
    {
        GameData altitudeGame = InsertGame(map: InsertMap("Altitude LE"));
        // A second game on another map, so that map exists as an option to check.
        InsertGame(map: InsertMap("Deathaura LE"));
        await LoadGamesWithNoDateRange();

        _viewModel.Filters.MapSlot.AddCommand.Execute(null);
        _viewModel.Filters.MapSlot.Options.Single(o => o.Label == "Deathaura LE").IsChecked = true;

        Assert.DoesNotContain(_viewModel.Games, r => r.GameId == altitudeGame.GameId);
    }

    [Fact]
    public async Task Filter_MapChecked_IsKept()
    {
        GameData altitudeGame = InsertGame(map: InsertMap("Altitude LE"));
        await LoadGamesWithNoDateRange();

        _viewModel.Filters.MapSlot.AddCommand.Execute(null);
        _viewModel.Filters.MapSlot.Options.Single(o => o.Label == "Altitude LE").IsChecked = true;

        Assert.Contains(_viewModel.Games, r => r.GameId == altitudeGame.GameId);
    }

    [Theory]
    [InlineData(GameOutcome.Loss, false)]
    [InlineData(GameOutcome.Win, true)]
    public async Task Filter_Outcome_KeepsOnlyCheckedOutcomes(GameOutcome checkedOutcome, bool expected)
    {
        InsertGame(win: 1m);
        await LoadGamesWithNoDateRange();

        _viewModel.Filters.OutcomeSlot.AddCommand.Execute(null);
        _viewModel.Filters.OutcomeSlot.Options.Single(o => o.Value == checkedOutcome).IsChecked = true;

        Assert.Equal(expected, _viewModel.Games.Count == 1);
    }

    // A team game has one matchup per opponent and should show if ANY of them is checked. Only TvP is
    // checked here, but one of the two opponents is Protoss. Guards the fix for a regression where the
    // matchup filter required every opponent's matchup to be checked (see also
    // DataPageFiltersViewModelTests.GetFilter_Matchup_TeamGame_MatchesWhenAnyOpponentsMatchupIsChecked).
    [Fact]
    public async Task Filter_MatchupPairs_OrsAcrossOpponents()
    {
        InsertGame(selfRace: 'T', opponents: [Opponent('Z', 3000), Opponent('P', 3000)]);
        await LoadGamesWithNoDateRange();

        CheckMatchup(Race.Terran, Race.Protoss);

        Assert.Single(_viewModel.Games);
    }

    [Fact]
    public async Task Filter_MatchupPairs_NoOpponentMatchesTheCheckedPair_IsExcluded()
    {
        InsertGame(selfRace: 'T', opponents: [Opponent('Z', 3000)]);
        await LoadGamesWithNoDateRange();

        CheckMatchup(Race.Terran, Race.Protoss);

        Assert.Empty(_viewModel.Games);
    }

    [Fact]
    public async Task Filter_OpponentMmrRange_OrsAcrossOpponents()
    {
        InsertGame(opponents: [Opponent('Z', 2000), Opponent('P', 3500)]);
        await LoadGamesWithNoDateRange();

        SetOpponentMmrRange(3000, 4000);

        Assert.Single(_viewModel.Games);
    }

    [Fact]
    public async Task Filter_OpponentMmrRange_NoOpponentInRange_IsExcluded()
    {
        InsertGame(opponents: [Opponent('Z', 2000)]);
        await LoadGamesWithNoDateRange();

        SetOpponentMmrRange(3000, 4000);

        Assert.Empty(_viewModel.Games);
    }

    // The Games twin of MapsPageViewModelTests.ValueOptionAddedElsewhere_PatchesTheFilterSlotPreservingWhatWasChecked.
    // The checkbox slot keeps its own option list, built when the slot is created, so an option added on
    // the Attributes tab has to be patched in without dropping what the user already checked. The Data tab
    // syncs attributes lazily — OnAttributesChanged only marks the cache dirty — so each repository change
    // is followed by the NotifyActivated() the tab does when it becomes visible.
    [Fact]
    public void ValueOptionAddedElsewhere_PatchesTheFilterSlotPreservingWhatWasChecked()
    {
        AttributeDefinition attribute = new(AttributeScope.Game) { Name = "Style", Type = AttributeType.Values };
        _attributeRepository.InsertAttribute(attribute, 0);
        _attributeRepository.InsertValueOption(attribute.Id, "Rush");
        _viewModel.NotifyActivated();

        CheckboxFilterSlotViewModel<AttributeValue, string?> slot = GameAttributeSlot("Style");
        slot.Options.Single(o => o.Value == "Rush").IsChecked = true;

        _attributeRepository.InsertValueOption(attribute.Id, "Macro");
        _viewModel.NotifyActivated();

        Assert.Equal(["Rush", "Macro"], slot.Options.Select(o => o.Value));
        Assert.True(slot.Options.Single(o => o.Value == "Rush").IsChecked);
        Assert.False(slot.Options.Single(o => o.Value == "Macro").IsChecked);
    }

    // A game attribute's filter lives in a menu item, wrapped in an AttributeFilterSlotViewModel that
    // projects a game onto its value row; the checkbox slot holding the options is the one inside that.
    private CheckboxFilterSlotViewModel<AttributeValue, string?> GameAttributeSlot(string title)
    {
        IFilterSlotViewModel<GameData> wrapper =
            _viewModel.Filters.GameAttributeSlots.Single(m => m.DisplayText == title).Filter!;
        return Assert.IsType<CheckboxFilterSlotViewModel<AttributeValue, string?>>(
            ((IWrappedFilterSlotViewModel)wrapper).WrappedFilter);
    }

    // Recreated from the deleted GameDataFilterTests, which were the only cover for build filtering. The
    // rule is unchanged — checking a build matches games that picked it, or any build beneath it — but
    // the implementation inverted: instead of expanding a checked build down to its subtree, the tab now
    // walks each game's chosen builds up through their ancestors and tests set membership.
    [Fact]
    public async Task Filter_Build_CheckedBuild_KeepsAGameThatPickedIt()
    {
        BuildNode build = InsertBuild("4 Gate");
        GameData game = InsertGameWithBuild(build);

        await LoadGamesWithNoDateRange();
        CheckBuild(build);

        Assert.Contains(_viewModel.Games, r => r.GameId == game.GameId);
    }

    // The case the subtree expansion existed for: the game picked the child, the filter checks the parent.
    [Fact]
    public async Task Filter_Build_CheckedBuild_KeepsAGameThatPickedADescendant()
    {
        BuildNode parent = InsertBuild("4 Gate");
        BuildNode child = InsertBuild("4 Gate into Blink", parent);
        GameData game = InsertGameWithBuild(child);

        await LoadGamesWithNoDateRange();
        CheckBuild(parent);

        Assert.Contains(_viewModel.Games, r => r.GameId == game.GameId);
    }

    // The other direction must not match: picking the parent is not picking the child.
    [Fact]
    public async Task Filter_Build_CheckedDescendant_ExcludesAGameThatPickedTheParent()
    {
        BuildNode parent = InsertBuild("4 Gate");
        BuildNode child = InsertBuild("4 Gate into Blink", parent);
        InsertGameWithBuild(parent);

        await LoadGamesWithNoDateRange();
        CheckBuild(child);

        Assert.Empty(_viewModel.Games);
    }

    [Fact]
    public async Task Filter_Build_UncheckedBuild_IsExcluded()
    {
        BuildNode picked = InsertBuild("4 Gate");
        BuildNode other = InsertBuild("Cannon Rush");
        InsertGameWithBuild(picked);

        await LoadGamesWithNoDateRange();
        CheckBuild(other);

        Assert.Empty(_viewModel.Games);
    }

    private BuildNode InsertBuild(string name, BuildNode? parent = null)
    {
        BuildNode node = new() { Name = name, PlayerRace = Race.Protoss };
        _buildRepository.InsertBuild(node, parent?.Id, 0);
        return node;
    }

    // The Build slot reads the build tree once, when the page is constructed, so the page is rebuilt
    // after the builds exist — otherwise neither the options nor the id lookup would know about them.
    private GameData InsertGameWithBuild(BuildNode build)
    {
        GameData game = InsertGame();
        _gameDataRepository.UpdateGameBuilds(game.ReplayData.Player.GamePlayerId!.Value, [build.Id]);
        _viewModel = CreateViewModel();
        return game;
    }

    private void CheckBuild(BuildNode build)
    {
        _viewModel.Filters.BuildSlot.AddCommand.Execute(null);
        _viewModel.Filters.BuildSlot.Options.Single(o => o.Value.Id == build.Id).IsChecked = true;
    }

    // SetActiveProfile resets the date range to today, per spec. These tests set dates themselves (or
    // want none), so they clear it rather than depend on the games happening to be dated today.
    private async Task LoadGamesWithNoDateRange()
    {
        await _viewModel.SetActiveProfile(_profile);
        _viewModel.Filters.FromDate = null;
        _viewModel.Filters.ToDate = null;
    }

    private void CheckMatchup(Race self, Race opponent)
    {
        _viewModel.Filters.MatchupSlot.AddCommand.Execute(null);
        _viewModel.Filters.MatchupSlot.Options.Single(o => o.Value == (self, opponent)).IsChecked = true;
    }

    private void SetOpponentMmrRange(decimal min, decimal max)
    {
        _viewModel.Filters.MmrSlot.AddCommand.Execute(null);
        _viewModel.Filters.MmrSlot.Min = min;
        _viewModel.Filters.MmrSlot.Max = max;
    }

    // Games store their map by id, so a game's map has to be a real row for it to come back on load.
    private Map InsertMap(string name)
    {
        Map map = new() { Name = name };
        _mapRepository.InsertMap(map);
        return map;
    }

    private static GamePlayer Opponent(char race, long mmr) =>
        new() { Name = "Foe", Clan = "", Mmr = new PlayerMmr { ParsedMmr = mmr }, Race = race, Random = false };

    #endregion

    private GameData InsertGame(Map? map = null, decimal win = 1m, char selfRace = 'Z',
        GamePlayer[]? opponents = null, DateTimeOffset? playedAt = null)
    {
        ParsedReplayData replay = new()
        {
            GameLengthSeconds = 600,
            ReplayPath = Guid.NewGuid() + ".SC2Replay",
            ReplayTimestamp = playedAt ?? DateTimeOffset.Now,
            Win = win,
            Player = new GamePlayer { Name = "Me", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3000 }, Race = selfRace, Random = false },
            Allies = [],
            Opponents = opponents ?? [new GamePlayer { Name = "Foe", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3100 }, Race = 'T', Random = false }],
        };
        GameData game = new() { Map = map, ReplayData = replay };
        _gameDataRepository.InsertGame(game, _sc2ProfileId);
        return game;
    }

    public async ValueTask DisposeAsync()
    {
        await _replayWatcherService.Stop();
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private sealed class StubTokenProvider() : BlizzardAppTokenProvider(null!, null!, null!, new MockLogger())
    {
        public override Task<string?> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
