using System.Net.Http;
using System.Threading;
using StatCraft.Models.Analytics;
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
            new MockLogger(), replayDataExtractor);
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

    // These used to call GameDataFilter.Matches directly. The Games tab no longer uses it, so they go
    // through the page itself instead: insert games, load the profile, set the filters the way the filter
    // bar would, and assert on what ends up in Games. Filters are driven through FilterPanel, so these
    // don't depend on how the page holds them either.

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

        DateFilter.FromDate = new DateTime(2026, 1, 15);
        DateFilter.ToDate = new DateTime(2026, 1, 15);

        Assert.Equal(expected, _viewModel.Games.Count == 1);
    }

    [Fact]
    public async Task Filter_MapNotChecked_IsExcluded()
    {
        GameData altitudeGame = InsertGame(map: InsertMap("Altitude LE"));
        // A second game on another map, so that map exists as an option to check.
        InsertGame(map: InsertMap("Deathaura LE"));
        await LoadGamesWithNoDateRange();

        Filters.Add("Map").Check("Deathaura LE");

        Assert.DoesNotContain(_viewModel.Games, r => r.GameId == altitudeGame.GameId);
    }

    [Fact]
    public async Task Filter_MapChecked_IsKept()
    {
        GameData altitudeGame = InsertGame(map: InsertMap("Altitude LE"));
        await LoadGamesWithNoDateRange();

        Filters.Add("Map").Check("Altitude LE");

        Assert.Contains(_viewModel.Games, r => r.GameId == altitudeGame.GameId);
    }

    [Theory]
    [InlineData("Loss", false)]
    [InlineData("Win", true)]
    public async Task Filter_Outcome_KeepsOnlyCheckedOutcomes(string checkedOutcome, bool expected)
    {
        InsertGame(win: 1m);
        await LoadGamesWithNoDateRange();

        Filters.Add("Outcome").Check(checkedOutcome);

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

        Filters.Add("Matchup").Check("TvP");

        Assert.Single(_viewModel.Games);
    }

    [Fact]
    public async Task Filter_MatchupPairs_NoOpponentMatchesTheCheckedPair_IsExcluded()
    {
        InsertGame(selfRace: 'T', opponents: [Opponent('Z', 3000)]);
        await LoadGamesWithNoDateRange();

        Filters.Add("Matchup").Check("TvP");

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

    [Theory]
    [InlineData(2999, false)]
    [InlineData(3000, true)]
    [InlineData(4000, true)]
    [InlineData(4001, false)]
    public async Task Filter_OpponentMmrRange_IsInclusiveOnBothEnds(long mmr, bool expected)
    {
        InsertGame(opponents: [Opponent('Z', mmr)]);
        await LoadGamesWithNoDateRange();

        SetOpponentMmrRange(3000, 4000);

        Assert.Equal(expected, _viewModel.Games.Count == 1);
    }

    // The range is re-read every time it changes, so editing it after it has already filtered swaps the
    // old range out rather than combining with it or keeping the one first applied.
    [Fact]
    public async Task Filter_OpponentMmrRange_EditedAfterFiltering_UsesOnlyTheNewRange()
    {
        GameData low = InsertGame(opponents: [Opponent('Z', 2000)]);
        GameData high = InsertGame(opponents: [Opponent('Z', 3500)]);
        await LoadGamesWithNoDateRange();
        SetOpponentMmrRange(3000, 4000);
        Assert.Equal(high.GameId, Assert.Single(_viewModel.Games).GameId);

        FilterHandle mmr = Filters.Applied("Opponent MMR");
        mmr.Min = 1000;
        mmr.Max = 2500;

        Assert.Equal(low.GameId, Assert.Single(_viewModel.Games).GameId);

        mmr.Remove();
        Assert.Equal(2, _viewModel.Games.Count);
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

        FilterHandle filter = Filters.Offered("Style").Check("Rush");

        _attributeRepository.InsertValueOption(attribute.Id, "Macro");
        _viewModel.NotifyActivated();

        Assert.Equal(["Rush", "Macro"], filter.OptionLabels);
        Assert.Equal(["Rush"], filter.CheckedLabels);
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

    // Build options are labelled with their place in the tree (race prefix, indentation), so match on the
    // name the label ends with rather than the exact decoration.
    private void CheckBuild(BuildNode build)
    {
        FilterHandle filter = Filters.Add("Build");
        filter.Check(filter.OptionLabels.Single(l => l.EndsWith(build.Name)));
    }

    // SetActiveProfile resets the date range to today, per spec. These tests set dates themselves (or
    // want none), so they clear it rather than depend on the games happening to be dated today.
    private async Task LoadGamesWithNoDateRange()
    {
        await _viewModel.SetActiveProfile(_profile);
        DateFilter.FromDate = null;
        DateFilter.ToDate = null;
    }

    private void SetOpponentMmrRange(decimal min, decimal max)
    {
        FilterHandle filter = Filters.Add("Opponent MMR");
        filter.Min = min;
        filter.Max = max;
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

    #region Filter bar

    // Behaviour of the filter bar and its "+ Filters" menu as a user sees it, pinned ahead of moving the
    // Maps, Builds and Data tabs onto one shared filter menu.

    // Re-created whenever a test rebuilds the page, so it always reads the page currently under test.
    private FilterPanel Filters
    {
        get
        {
            if (_filtersPage != _viewModel)
            {
                _filtersPage = _viewModel;
                _filters = FilterPanel.Of(_viewModel);
            }
            return _filters!;
        }
    }
    private FilterPanel? _filters;
    private DataPageViewModel? _filtersPage;

    // Profile and date range are mandatory filters: always in the filter bar, first, and never in the menu.
    private FilterHandle ProfileFilter => Filters.Applied("Profile");

    private FilterHandle DateFilter => Filters.Applied("Date");

    private static readonly string[] AlwaysApplied = ["Profile", "Date"];
    private static readonly string[] BuiltInFilterTitles = ["Map", "Matchup", "Outcome", "Opponent MMR", "Build"];

    [Fact]
    public void BuiltInFilters_StartUnappliedAndAreOfferedInTheirFixedOrder()
    {
        Assert.Equal(AlwaysApplied, Filters.AppliedTitles);
        Assert.Equal(BuiltInFilterTitles, Filters.AddableTitles);
    }

    // Everything these filter on comes from the replay itself, so there's never an unset value to include.
    [Fact]
    public void BuiltInFilters_DoNotOfferIncludeUnset()
    {
        Assert.All(BuiltInFilterTitles, title => Assert.False(Filters.Offered(title).AllowIncludeUnset));
        Assert.False(DateFilter.AllowIncludeUnset);
        Assert.False(ProfileFilter.AllowIncludeUnset);
    }

    [Fact]
    public void ProfileFilter_IsAMandatoryCheckboxFilterListingTheLinkedProfiles()
    {
        FilterHandle profile = ProfileFilter;

        Assert.True(profile.IsCheckboxFilter);
        Assert.True(profile.Mandatory);
        Assert.Equal([_profile.DisplayName], profile.OptionLabels);
    }

    // Checking and unchecking profiles changes which games are loaded, so it has to keep working now that
    // the profile filter sits in the filter bar alongside the filters that only re-filter loaded games.
    [Fact]
    public async Task ProfileFilter_CheckingAndUncheckingAProfile_LoadsAndUnloadsItsGames()
    {
        Sc2Profile other = InsertOtherProfile();
        InsertGame(profileId: other.Id);
        await LoadGamesWithNoDateRange();
        Assert.Empty(_viewModel.Games);

        ProfileFilter.Check(other.DisplayName);
        Assert.Single(_viewModel.Games);

        ProfileFilter.Uncheck(other.DisplayName);
        Assert.Empty(_viewModel.Games);
    }

    [Fact]
    public void DateFilter_IsAMandatoryDateRangeThatStartsOnToday()
    {
        FilterHandle date = DateFilter;

        Assert.True(date.IsDateRangeFilter);
        Assert.True(date.Mandatory);
        Assert.Equal(DateTime.Today, date.FromDate);
        Assert.Equal(DateTime.Today, date.ToDate);
    }

    // Loading games without starting a session (checking a profile directly) still only shows today's
    // games, because a new page's date range already starts on today.
    [Fact]
    public void DateFilter_OnANewPage_ShowsOnlyTodaysGames()
    {
        InsertGame(playedAt: DateTimeOffset.Now);
        InsertGame(playedAt: DateTimeOffset.Now.AddDays(-2));
        _viewModel.Filters.RefreshProfileOptions(_accountRepository.GetAllProfiles());

        ProfileFilter.Check(_profile.DisplayName);

        Assert.Single(_viewModel.Games);
    }

    // Local noon on each day, so the games sit squarely inside their calendar days in any timezone.
    [Theory]
    [InlineData(true, false, 9, false)]
    [InlineData(true, false, 10, true)]
    [InlineData(true, false, 30, true)]
    [InlineData(false, true, 1, true)]
    [InlineData(false, true, 20, true)]
    [InlineData(false, true, 21, false)]
    public async Task DateFilter_WithOnlyOneEndSet_IsOpenOnTheOtherSide(bool setFrom, bool setTo, int day, bool expected)
    {
        InsertGame(playedAt: new DateTimeOffset(new DateTime(2026, 1, day, 12, 0, 0, DateTimeKind.Local)));
        await LoadGamesWithNoDateRange();

        if (setFrom)
            DateFilter.FromDate = new DateTime(2026, 1, 10);
        if (setTo)
            DateFilter.ToDate = new DateTime(2026, 1, 20);

        Assert.Equal(expected, _viewModel.Games.Count == 1);
    }

    // The range compares calendar days, so a game late on the last day is still in it.
    [Fact]
    public async Task DateFilter_IncludesAGameLateOnTheLastDay()
    {
        InsertGame(playedAt: new DateTimeOffset(new DateTime(2026, 1, 15, 23, 30, 0, DateTimeKind.Local)));
        await LoadGamesWithNoDateRange();

        DateFilter.FromDate = new DateTime(2026, 1, 15);
        DateFilter.ToDate = new DateTime(2026, 1, 15);

        Assert.Single(_viewModel.Games);
    }

    [Fact]
    public void AddingAFilter_MovesItFromTheAddMenuToTheFilterBar()
    {
        Filters.Add("Outcome");

        Assert.Equal([.. AlwaysApplied, "Outcome"], Filters.AppliedTitles);
        Assert.Equal(["Map", "Matchup", "Opponent MMR", "Build"], Filters.AddableTitles);
    }

    // The bar keeps the same fixed order as the menu, not the order filters were added in.
    [Fact]
    public void AppliedFilters_ShowInTheirFixedOrderRegardlessOfTheOrderAdded()
    {
        Filters.Add("Build");
        Filters.Add("Outcome");
        Filters.Add("Map");

        Assert.Equal([.. AlwaysApplied, "Map", "Outcome", "Build"], Filters.AppliedTitles);
    }

    [Fact]
    public async Task RemovingAFilter_ReturnsItToTheAddMenuStopsItConstrainingAndClearsIt()
    {
        InsertGame(map: InsertMap("Altitude LE"));
        InsertGame(map: InsertMap("Deathaura LE"));
        await LoadGamesWithNoDateRange();
        Filters.Add("Map").Check("Altitude LE");
        Assert.Single(_viewModel.Games);

        Filters.Applied("Map").Remove();

        Assert.Equal(2, _viewModel.Games.Count);
        Assert.Equal(AlwaysApplied, Filters.AppliedTitles);
        Assert.Equal(BuiltInFilterTitles, Filters.AddableTitles);
        Assert.Empty(Filters.Add("Map").CheckedLabels);
    }

    [Fact]
    public async Task RemovingTheMmrFilter_ClearsItsRange()
    {
        InsertGame(opponents: [Opponent('Z', 2000)]);
        await LoadGamesWithNoDateRange();
        SetOpponentMmrRange(3000, 4000);
        Assert.Empty(_viewModel.Games);

        Filters.Applied("Opponent MMR").Remove();

        Assert.Single(_viewModel.Games);
        FilterHandle readded = Filters.Add("Opponent MMR");
        Assert.Null(readded.Min);
        Assert.Null(readded.Max);
    }

    // Adding a filter only shows its controls; until something is checked or entered, it shows every game.
    [Theory]
    [InlineData("Map")]
    [InlineData("Matchup")]
    [InlineData("Outcome")]
    [InlineData("Opponent MMR")]
    public async Task AddedFilterWithNoCriteria_StillShowsEveryGame(string title)
    {
        InsertGame(map: InsertMap("Altitude LE"));
        InsertGame(map: InsertMap("Deathaura LE"), win: 0m, selfRace: 'P');
        await LoadGamesWithNoDateRange();

        Filters.Add(title);

        Assert.Equal(2, _viewModel.Games.Count);
    }

    // Separate from the theory above because only games that picked a build have anything for the build
    // filter to look at; one without a build is excluded as soon as the filter is applied.
    [Fact]
    public async Task AddedBuildFilterWithNoCriteria_StillShowsEveryGameThatPickedABuild()
    {
        BuildNode gate = InsertBuild("4 Gate");
        BuildNode cannon = InsertBuild("Cannon Rush");
        InsertGameWithBuild(gate);
        InsertGameWithBuild(cannon);
        await LoadGamesWithNoDateRange();

        Filters.Add("Build");

        Assert.Equal(2, _viewModel.Games.Count);
    }

    [Fact]
    public async Task UncheckingTheLastOption_StopsTheFilterConstraining()
    {
        InsertGame(map: InsertMap("Altitude LE"));
        InsertGame(map: InsertMap("Deathaura LE"));
        await LoadGamesWithNoDateRange();
        FilterHandle filter = Filters.Add("Map").Check("Altitude LE");
        Assert.Single(_viewModel.Games);

        filter.Uncheck("Altitude LE");

        Assert.Equal(2, _viewModel.Games.Count);
    }

    [Fact]
    public async Task SeveralCheckedOptions_KeepGamesMatchingAnyOfThem()
    {
        InsertGame(map: InsertMap("Altitude LE"));
        InsertGame(map: InsertMap("Deathaura LE"));
        InsertGame(map: InsertMap("Ley Lines"));
        await LoadGamesWithNoDateRange();

        Filters.Add("Map").Check("Altitude LE", "Ley Lines");

        Assert.Equal(2, _viewModel.Games.Count);
    }

    [Fact]
    public async Task DifferentFilters_MustAllMatch()
    {
        Map altitude = InsertMap("Altitude LE");
        GameData altitudeWin = InsertGame(map: altitude, win: 1m);
        InsertGame(map: altitude, win: 0m);
        InsertGame(map: InsertMap("Deathaura LE"), win: 1m);
        await LoadGamesWithNoDateRange();

        Filters.Add("Map").Check("Altitude LE");
        Filters.Add("Outcome").Check("Win");

        Assert.Equal(altitudeWin.GameId, Assert.Single(_viewModel.Games).GameId);
    }

    // The map filter only offers maps that loaded games were actually played on, alphabetically.
    [Fact]
    public async Task MapFilter_OffersTheDistinctMapsOfTheLoadedGames()
    {
        Map deathaura = InsertMap("Deathaura LE");
        InsertGame(map: deathaura);
        InsertGame(map: deathaura);
        InsertGame(map: InsertMap("Altitude LE"));
        InsertMap("Never Played LE");

        await LoadGamesWithNoDateRange();

        Assert.Equal(["Altitude LE", "Deathaura LE"], Filters.Offered("Map").OptionLabels);
    }

    // Reloading from the database (a profile change, a session start) rebuilds the map options from the
    // new set of games; what was checked has to survive that and keep filtering.
    [Fact]
    public async Task MapFilter_CheckedMap_StaysCheckedAndFilteringAcrossAReload()
    {
        InsertGame(map: InsertMap("Altitude LE"));
        InsertGame(map: InsertMap("Deathaura LE"));
        await LoadGamesWithNoDateRange();
        Filters.Add("Map").Check("Altitude LE");

        InsertGame(map: InsertMap("Ley Lines"));
        await LoadGamesWithNoDateRange();

        FilterHandle filter = Filters.Applied("Map");
        Assert.Equal(["Altitude LE", "Deathaura LE", "Ley Lines"], filter.OptionLabels);
        Assert.Equal(["Altitude LE"], filter.CheckedLabels);
        Assert.Single(_viewModel.Games);
    }

    // Per spec, starting a session collapses the profile filter to that profile and the date range to
    // today — and nothing else. Filters the user added stay added and keep constraining.
    [Fact]
    public async Task BeginningASession_LeavesOtherFiltersApplied()
    {
        InsertGame(win: 1m);
        InsertGame(win: 0m);
        await LoadGamesWithNoDateRange();
        Filters.Add("Outcome").Check("Win");

        await _viewModel.SetActiveProfile(_profile);

        Assert.Equal([.. AlwaysApplied, "Outcome"], Filters.AppliedTitles);
        Assert.Equal(["Win"], Filters.Applied("Outcome").CheckedLabels);
        Assert.Single(_viewModel.Games);
    }

    [Fact]
    public async Task BeginningASession_ResetsTheDateRangeToToday()
    {
        InsertGame(playedAt: DateTimeOffset.Now.AddDays(-2));
        await LoadGamesWithNoDateRange();
        Assert.Single(_viewModel.Games);

        await _viewModel.SetActiveProfile(_profile);

        Assert.Empty(_viewModel.Games);
    }

    [Fact]
    public async Task ProfileFilter_CheckingAnotherProfile_LoadsItsGamesToo()
    {
        Sc2Profile other = InsertOtherProfile();
        InsertGame();
        InsertGame(profileId: other.Id);
        await LoadGamesWithNoDateRange();
        Assert.Single(_viewModel.Games);

        ProfileFilter.Check(other.DisplayName);

        Assert.Equal(2, _viewModel.Games.Count);
    }

    [Fact]
    public async Task ProfileFilter_UncheckingEveryProfile_ShowsNoGames()
    {
        InsertGame();
        await LoadGamesWithNoDateRange();

        ProfileFilter.Uncheck(_profile.DisplayName);

        Assert.Empty(_viewModel.Games);
    }

    // The win rate describes exactly the games showing, so filtering changes it.
    [Fact]
    public async Task WinRateLabel_CountsOnlyTheFilteredGames()
    {
        InsertGame(win: 1m);
        InsertGame(win: 0m);
        await LoadGamesWithNoDateRange();
        Assert.Equal(new WinLossRecord(1, 1, 0).Label, _viewModel.WinRateLabel);

        Filters.Add("Outcome").Check("Win");

        Assert.Equal(new WinLossRecord(1, 0, 0).Label, _viewModel.WinRateLabel);
    }

    #endregion

    #region Game attribute filters

    // A game attribute filter sorts games into the same three cases as a map attribute filter (see
    // MapsPageViewModelTests): no value row is excluded outright, an unset row is left to Include unset
    // (never offered on this tab, so always excluded), and a set value has to match.

    [Fact]
    public void GameAttributes_AreOfferedInTheAddMenuAfterTheBuiltInFilters()
    {
        _attributeRepository.InsertAttribute(new(AttributeScope.Game) { Name = "Style", Type = AttributeType.Values }, 0);
        _viewModel = CreateViewModel();

        Assert.Equal([.. BuiltInFilterTitles, "Style"], Filters.AddableTitles);
    }

    // Pins the fix for the add menu not being told when a game attribute appeared: the menu is only
    // re-read when the page announces a change, so without it the new attribute never showed up.
    [Fact]
    public void GameAttributeAddedElsewhere_IsOfferedOnceTheTabIsActivated()
    {
        FilterPanel filters = Filters;

        _attributeRepository.InsertAttribute(new(AttributeScope.Game) { Name = "Style", Type = AttributeType.Values }, 0);
        _viewModel.NotifyActivated();

        Assert.Equal([.. BuiltInFilterTitles, "Style"], filters.AddableTitles);
    }

    [Theory]
    [InlineData("Rush", true)]
    [InlineData("Macro", false)]
    public async Task GameAttributeFilter_CheckedOption_KeepsOnlyGamesHoldingThatValue(string gameValue, bool expected)
    {
        AttributeDefinition style = InsertStyleAttribute();
        GameData game = InsertGame();
        SaveGameValue(game, style, v => v.SelectedValue = gameValue);
        _viewModel = CreateViewModel();
        await LoadGamesWithNoDateRange();

        Filters.Add("Style").Check("Rush");

        Assert.Equal(expected, _viewModel.Games.Count == 1);
    }

    [Fact]
    public async Task GameAttributeFilter_ExcludesAGameWithNoValueRow()
    {
        InsertStyleAttribute();
        InsertGame();
        _viewModel = CreateViewModel();
        await LoadGamesWithNoDateRange();

        Filters.Add("Style").Check("Rush");

        Assert.Empty(_viewModel.Games);
    }

    // A mandatory attribute gives every loaded game a row, unset until filled in.
    [Fact]
    public async Task GameAttributeFilter_ExcludesAGameWhoseValueIsUnset()
    {
        AttributeDefinition style = new(AttributeScope.Game) { Name = "Style", Type = AttributeType.Values, IsMandatory = true };
        _attributeRepository.InsertAttribute(style, 0);
        _attributeRepository.InsertValueOption(style.Id, "Rush");
        InsertGame();
        _viewModel = CreateViewModel();
        await LoadGamesWithNoDateRange();
        Assert.Single(_viewModel.Games);

        Filters.Add("Style").Check("Rush");

        Assert.Empty(_viewModel.Games);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task GameAttributeFilter_Bool_KeepsOnlyGamesWithTheChosenValue(bool gameValue, bool expected)
    {
        AttributeDefinition proxy = new(AttributeScope.Game) { Name = "Proxy", Type = AttributeType.Bool };
        _attributeRepository.InsertAttribute(proxy, 0);
        GameData game = InsertGame();
        SaveGameValue(game, proxy, v => v.BoolValue = gameValue);
        _viewModel = CreateViewModel();
        await LoadGamesWithNoDateRange();

        Filters.Add("Proxy").BoolValue = true;

        Assert.Equal(expected, _viewModel.Games.Count == 1);
    }

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public async Task GameAttributeFilter_Numeric_RangeIsInclusiveOnBothEnds(int gameValue, bool expected)
    {
        AttributeDefinition apm = new(AttributeScope.Game) { Name = "Apm", Type = AttributeType.Numeric };
        _attributeRepository.InsertAttribute(apm, 0);
        GameData game = InsertGame();
        SaveGameValue(game, apm, v => v.NumericValue = gameValue);
        _viewModel = CreateViewModel();
        await LoadGamesWithNoDateRange();

        FilterHandle filter = Filters.Add("Apm");
        filter.Min = 50;
        filter.Max = 60;

        Assert.Equal(expected, _viewModel.Games.Count == 1);
    }

    [Fact]
    public async Task GameAttributeFilter_AndBuiltInFilter_MustBothMatch()
    {
        AttributeDefinition style = InsertStyleAttribute();
        SaveGameValue(InsertGame(win: 1m), style, v => v.SelectedValue = "Rush");
        SaveGameValue(InsertGame(win: 0m), style, v => v.SelectedValue = "Rush");
        SaveGameValue(InsertGame(win: 1m), style, v => v.SelectedValue = "Macro");
        _viewModel = CreateViewModel();
        await LoadGamesWithNoDateRange();

        Filters.Add("Style").Check("Rush");
        Filters.Add("Outcome").Check("Win");

        Assert.Single(_viewModel.Games);
    }

    [Fact]
    public async Task RemovingAGameAttributeFilter_StopsItConstraining()
    {
        AttributeDefinition style = InsertStyleAttribute();
        SaveGameValue(InsertGame(), style, v => v.SelectedValue = "Macro");
        _viewModel = CreateViewModel();
        await LoadGamesWithNoDateRange();
        Filters.Add("Style").Check("Rush");
        Assert.Empty(_viewModel.Games);

        Filters.Applied("Style").Remove();

        Assert.Single(_viewModel.Games);
        Assert.Equal(AlwaysApplied, Filters.AppliedTitles);
        Assert.Empty(Filters.Offered("Style").CheckedLabels);
    }

    // A filter slot created by the lazy attribute sync has to be wired up like the original ones, or
    // checking an option on it would never re-filter the games.
    [Fact]
    public async Task GameAttributeAddedElsewhere_Filters()
    {
        InsertGame();
        await LoadGamesWithNoDateRange();

        InsertStyleAttribute();
        _viewModel.NotifyActivated();
        Filters.Add("Style").Check("Rush");

        Assert.Empty(_viewModel.Games);
    }

    [Fact]
    public async Task GameAttributeDeletedElsewhere_WhileApplied_LeavesTheFilterBarAndStopsConstraining()
    {
        AttributeDefinition style = InsertStyleAttribute();
        SaveGameValue(InsertGame(), style, v => v.SelectedValue = "Macro");
        _viewModel = CreateViewModel();
        await LoadGamesWithNoDateRange();
        Filters.Add("Style").Check("Rush");
        Assert.Empty(_viewModel.Games);

        _attributeRepository.DeleteAttribute(style.Id);
        _viewModel.NotifyActivated();

        Assert.Equal(AlwaysApplied, Filters.AppliedTitles);
        Assert.Equal(BuiltInFilterTitles, Filters.AddableTitles);
        Assert.Single(_viewModel.Games);
    }

    [Fact]
    public void GameAttributeRenamedElsewhere_WhileApplied_RenamesItInTheFilterBarAndMenuKeepingItsCriteria()
    {
        InsertStyleAttribute();
        _viewModel = CreateViewModel();
        Filters.Add("Style").Check("Rush");

        AttributeDefinition editedElsewhere = Assert.Single(_attributeRepository.GetAllAttributes(AttributeScope.Game));
        editedElsewhere.Name = "Play Style";
        _attributeRepository.UpdateAttribute(editedElsewhere);
        _viewModel.NotifyActivated();

        Assert.Equal([.. AlwaysApplied, "Play Style"], Filters.AppliedTitles);
        Assert.Equal(["Rush"], Filters.Applied("Play Style").CheckedLabels);

        // The menu entry is renamed too, which shows once the filter is back in the menu.
        Filters.Applied("Play Style").Remove();
        Assert.Equal([.. BuiltInFilterTitles, "Play Style"], Filters.AddableTitles);
    }

    // The slot is rebuilt as the new kind, but whether it was showing survives, and the rebuilt slot is
    // still wired to re-filter the games.
    [Fact]
    public async Task GameAttributeTypeChangedElsewhere_WhileApplied_StaysAppliedAsTheNewKindAndStillFilters()
    {
        AttributeDefinition contested = new(AttributeScope.Game) { Name = "Contested", Type = AttributeType.Numeric };
        _attributeRepository.InsertAttribute(contested, 0);
        SaveGameValue(InsertGame(), contested, v => v.NumericValue = 5);
        _viewModel = CreateViewModel();
        await LoadGamesWithNoDateRange();
        Filters.Add("Contested");
        Assert.Single(_viewModel.Games);

        AttributeDefinition editedElsewhere = Assert.Single(_attributeRepository.GetAllAttributes(AttributeScope.Game));
        editedElsewhere.Type = AttributeType.Bool;
        _attributeRepository.UpdateAttribute(editedElsewhere);
        _viewModel.NotifyActivated();

        Assert.Equal([.. AlwaysApplied, "Contested"], Filters.AppliedTitles);
        FilterHandle filter = Filters.Applied("Contested");
        Assert.True(filter.IsBoolFilter);
        // The game's stored value was numeric, so as a yes/no attribute it's unset and filtered out...
        Assert.Empty(_viewModel.Games);

        // ...until the rebuilt filter is removed, which only re-filters if the new slot is wired up.
        filter.Remove();
        Assert.Single(_viewModel.Games);
    }

    // Game attributes share one submenu, which only lists the attributes not already showing in the
    // filter bar — the same as every other entry in the menu.
    [Fact]
    public void ApplyingAGameAttribute_TakesItOutOfTheMenuWhileTheOthersStay()
    {
        InsertStyleAttribute();
        InsertProxyAttribute();
        _viewModel = CreateViewModel();
        Assert.Equal([.. BuiltInFilterTitles, "Style", "Proxy"], Filters.AddableTitles);

        Filters.Add("Style");

        Assert.Equal([.. BuiltInFilterTitles, "Proxy"], Filters.AddableTitles);
    }

    [Fact]
    public void RemovingAGameAttributeFilter_PutsItBackInTheMenu()
    {
        InsertStyleAttribute();
        InsertProxyAttribute();
        _viewModel = CreateViewModel();
        Filters.Add("Style");

        Filters.Applied("Style").Remove();

        Assert.Equal([.. BuiltInFilterTitles, "Style", "Proxy"], Filters.AddableTitles);
    }

    // With nothing left in it to add, the submenu itself leaves the menu, and comes back when one of its
    // filters is removed.
    [Fact]
    public void ApplyingEveryGameAttribute_TakesTheSubmenuOutOfTheMenuUntilOneIsRemoved()
    {
        InsertStyleAttribute();
        InsertProxyAttribute();
        _viewModel = CreateViewModel();

        Filters.Add("Style");
        Filters.Add("Proxy");
        Assert.Equal(BuiltInFilterTitles, Filters.AddableTitles);

        Filters.Applied("Proxy").Remove();
        Assert.Equal([.. BuiltInFilterTitles, "Proxy"], Filters.AddableTitles);
    }

    [Fact]
    public void GameAttributeAddedElsewhere_WhileTheOthersAreApplied_IsOffered()
    {
        InsertStyleAttribute();
        _viewModel = CreateViewModel();
        Filters.Add("Style");
        Assert.Equal(BuiltInFilterTitles, Filters.AddableTitles);

        InsertProxyAttribute();
        _viewModel.NotifyActivated();

        Assert.Equal([.. BuiltInFilterTitles, "Proxy"], Filters.AddableTitles);
    }

    [Fact]
    public void GameAttributeDeletedElsewhere_WhileNotApplied_LeavesTheMenu()
    {
        InsertStyleAttribute();
        AttributeDefinition proxy = InsertProxyAttribute();
        _viewModel = CreateViewModel();
        Assert.Equal([.. BuiltInFilterTitles, "Style", "Proxy"], Filters.AddableTitles);

        _attributeRepository.DeleteAttribute(proxy.Id);
        _viewModel.NotifyActivated();

        Assert.Equal([.. BuiltInFilterTitles, "Style"], Filters.AddableTitles);
    }

    // The type change rebuilds the applied filter's menu entry; the rebuilt entry must still count as
    // applied and stay out of the menu.
    [Fact]
    public void GameAttributeTypeChangedElsewhere_WhileApplied_StaysOutOfTheMenu()
    {
        AttributeDefinition style = InsertStyleAttribute();
        InsertProxyAttribute();
        _viewModel = CreateViewModel();
        Filters.Add("Style");

        AttributeDefinition editedElsewhere = _attributeRepository.GetAllAttributes(AttributeScope.Game).Single(a => a.Id == style.Id);
        editedElsewhere.Type = AttributeType.Bool;
        _attributeRepository.UpdateAttribute(editedElsewhere);
        _viewModel.NotifyActivated();

        Assert.Equal([.. AlwaysApplied, "Style"], Filters.AppliedTitles);
        Assert.Equal([.. BuiltInFilterTitles, "Proxy"], Filters.AddableTitles);
    }

    private AttributeDefinition InsertProxyAttribute()
    {
        AttributeDefinition attribute = new(AttributeScope.Game) { Name = "Proxy", Type = AttributeType.Bool };
        _attributeRepository.InsertAttribute(attribute, 1);
        return attribute;
    }

    private AttributeDefinition InsertStyleAttribute()
    {
        AttributeDefinition attribute = new(AttributeScope.Game) { Name = "Style", Type = AttributeType.Values };
        _attributeRepository.InsertAttribute(attribute, 0);
        _attributeRepository.InsertValueOption(attribute.Id, "Rush");
        _attributeRepository.InsertValueOption(attribute.Id, "Macro");
        return attribute;
    }

    private void SaveGameValue(GameData game, AttributeDefinition attribute, Action<AttributeValue> setValue)
    {
        AttributeValue value = new(attribute);
        setValue(value);
        _gameDataRepository.SaveGameAttributeValue(game.GameId!.Value, attribute.Id, value.Serialize());
    }

    private Sc2Profile InsertOtherProfile()
    {
        Sc2Profile other = new() { BattleNetAccountId = _profile.BattleNetAccountId, RegionId = "1", RealmId = "1", ProfileId = 222, Name = "Smurf" };
        _accountRepository.UpsertProfile(other);
        return other;
    }

    #endregion

    private GameData InsertGame(Map? map = null, decimal win = 1m, char selfRace = 'Z',
        GamePlayer[]? opponents = null, DateTimeOffset? playedAt = null, int? profileId = null)
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
        _gameDataRepository.InsertGame(game, profileId ?? _sc2ProfileId);
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
