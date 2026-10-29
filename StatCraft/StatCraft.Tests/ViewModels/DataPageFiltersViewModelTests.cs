using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Maps;
using StatCraft.Models.GameData.Race;
using StatCraft.Models.GameData.Replays;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataParsing;
using StatCraft.ViewModels.Windows.DataComponents;
using StatCraft.Tests.Mocks;
using StatCraft.ViewModels.Windows.Filters;
using System.Collections.ObjectModel;

namespace StatCraft.Tests;

public class DataPageFiltersViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly BuildRepository _buildRepository;
    private readonly DataPageFiltersViewModel _filters;

    public DataPageFiltersViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");
        _buildRepository = new BuildRepository(_dbPath);
        _buildRepository.Initialize();
        _filters = CreateFilters();
    }

    // A fresh set of filters over whatever is in the database now. The build menu reads one tree per
    // matchup as it is built, so a test that seeds builds has to build the menu after seeding them.
    // The mandatory date range starts on today; the GetFilter tests use fixed-date games, so it is cleared
    // rather than left to depend on when they run.
    private DataPageFiltersViewModel CreateFilters()
    {
        _buildTrees.Clear();
        DataPageFiltersViewModel filters = new(BuildTree, new MockLogger(), []);
        filters.DateSlot.FromDate = null;
        filters.DateSlot.ToDate = null;
        return filters;
    }

    // One live collection per matchup, handed out to whoever asks for it, the way DataPageViewModel
    // caches the trees its rows and its build filters share.
    private readonly Dictionary<(Race Player, Matchups Opponent), ObservableCollection<BuildNode>> _buildTrees = new();

    private ObservableCollection<BuildNode>? BuildTree(Race? player, Matchups matchups)
    {
        if (player == null)
            return null;
        if (!_buildTrees.TryGetValue((player.Value, matchups), out ObservableCollection<BuildNode>? tree))
        {
            tree = new ObservableCollection<BuildNode>(_buildRepository.GetBuildsForMatchup(player.Value, matchups));
            _buildTrees[(player.Value, matchups)] = tree;
        }
        return tree;
    }

    // The one build entry offers the same fixed 3x3 of matchups, whether or not any build is filed under
    // them.
    private static readonly string[] BuildMatchupTitles =
        ["ZvZ", "ZvT", "ZvP", "TvZ", "TvT", "TvP", "PvZ", "PvT", "PvP"];

    [Fact]
    public void ExtraFilters_StartOutOfTheBar_ExceptTheMandatoryProfileAndDateRange()
    {
        FilterPanel panel = FilterPanel.Of(_filters);

        // Map, Matchup, Outcome and Opponent MMR, then the build matchups. The Game Attributes submenu is
        // empty with nothing seeded, so it offers nothing and stays hidden.
        Assert.Equal(["Map", "Matchup", "Outcome", "Opponent MMR", .. BuildMatchupTitles], panel.AddableTitles);
        Assert.Equal(["Profile", "Date"], panel.AppliedTitles);
    }

    [Fact]
    public void AddingAnEntry_PutsItsFilterInTheBarAndLeavesTheMenuIfUnique()
    {
        FilterPanel panel = FilterPanel.Of(_filters);

        panel.Add("Map");

        Assert.Contains("Map", panel.AppliedTitles);
        // The map a game was played on is one value, so asking about it twice says nothing new.
        Assert.False(panel.IsOffered("Map"));
    }

    [Fact]
    public void AddingANonUniqueEntry_CanBeDoneMoreThanOnce()
    {
        FilterPanel panel = FilterPanel.Of(_filters);

        panel.Add("Matchup");
        panel.Add("Matchup");

        Assert.Equal(["Matchup", "Matchup"], panel.AppliedTitles.Where(t => t == "Matchup"));
        Assert.True(panel.IsOffered("Matchup"));
    }

    [Fact]
    public void RemovingAFilter_TakesItOutOfTheBarAndOffersItAgain()
    {
        Map altitude = new() { Name = "Altitude LE", Id = 1 };
        Map deathaura = new() { Name = "Deauthaura LE", Id = 2 };
        _filters.RefreshMapOptions([altitude, deathaura]);
        FilterPanel panel = FilterPanel.Of(_filters);
        FilterHandle map = panel.Add("Map").Check("Altitude LE");

        map.Remove();

        Assert.DoesNotContain("Map", panel.AppliedTitles);
        Assert.True(panel.IsOffered("Map"));
        // Adding it again starts from scratch rather than resurrecting the old criteria.
        Assert.Empty(panel.Add("Map").CheckedLabels);
    }

    [Fact]
    public void RemovingAMandatoryFilter_KeepsItInTheBarAndClearsIt()
    {
        FilterPanel panel = FilterPanel.Of(_filters);
        FilterHandle date = panel.Applied("Date");
        date.FromDate = new DateTime(2026, 1, 1);

        date.Remove();

        Assert.Contains("Date", panel.AppliedTitles);
        Assert.Null(date.FromDate);
    }

    // A removed filter is discarded rather than reset, so the bounds can't come back with it.
    [Fact]
    public void RemovingANumericRangeFilter_ThenAddingItAgain_StartsWithNoBounds()
    {
        // The MMR filter wraps a range per opponent, so its bounds are reached the way the page tests do.
        FilterPanel panel = FilterPanel.Of(_filters);
        FilterHandle mmr = panel.Add("Opponent MMR");
        mmr.Min = 1000;
        mmr.Max = 2000;

        mmr.Remove();

        Assert.DoesNotContain("Opponent MMR", panel.AppliedTitles);

        FilterHandle readded = panel.Add("Opponent MMR");

        Assert.Null(readded.Min);
        Assert.Null(readded.Max);
    }

    [Fact]
    public void RefreshProfileOptions_PreservesCheckedStateAcrossRebuild()
    {
        Sc2Profile profileA = new() { Id = 1, Name = "A" };
        Sc2Profile profileB = new() { Id = 2, Name = "B" };
        _filters.RefreshProfileOptions([profileA, profileB]);
        ProfileOption(1).IsChecked = true;

        _filters.RefreshProfileOptions([profileA, profileB]);

        Assert.True(ProfileOption(1).IsChecked);
        Assert.False(ProfileOption(2).IsChecked);
    }

    [Fact]
    public void RefreshMapOptions_PreservesCheckedStateById()
    {
        Map altitude = new() { Name = "Altitude", Id = 1 };
        Map deathaura = new() { Name = "Deauthaura", Id = 2 };
        Map leylines = new() { Name = "Ley Lines", Id = 3 };
        _filters.RefreshMapOptions([altitude, deathaura]);
        FilterHandle map = FilterPanel.Of(_filters).Add("Map").Check("Altitude");

        _filters.RefreshMapOptions([altitude, deathaura, leylines]);

        Assert.Equal(["Altitude"], map.CheckedLabels);
        Assert.Equal(["Altitude", "Deauthaura", "Ley Lines"], map.OptionLabels);
    }

    // The map filter only exists while it is in the bar, so the options it is built with have to be the
    // ones known at that point — including whichever were checked before it was last removed.
    [Fact]
    public void RefreshMapOptions_WithNoMapFilterInTheBar_StillFeedsTheNextOneAdded()
    {
        Map altitude = new() { Name = "Altitude", Id = 1 };
        Map leylines = new() { Name = "Ley Lines", Id = 3 };

        _filters.RefreshMapOptions([altitude, leylines]);

        Assert.Equal(["Altitude", "Ley Lines"], FilterPanel.Of(_filters).Add("Map").OptionLabels);
    }

    [Fact]
    public void SetSingleActiveProfile_ChecksOnlyThatProfileAndResetsDateRangeToToday()
    {
        Sc2Profile profileA = new() { Id = 1, Name = "A" };
        Sc2Profile profileB = new() { Id = 2, Name = "B" };
        _filters.RefreshProfileOptions([profileA, profileB]);
        ProfileOption(2).IsChecked = true;

        _filters.SetSingleActiveProfile(profileA);

        Assert.True(ProfileOption(1).IsChecked);
        Assert.False(ProfileOption(2).IsChecked);
        Assert.Equal(DateTime.Today, _filters.DateSlot.FromDate);
        Assert.Equal(DateTime.Today, _filters.DateSlot.ToDate);
    }

    [Fact]
    public void SetSingleActiveProfile_DoesNotRaiseChangeEvents()
    {
        Sc2Profile profile = new() { Id = 1, Name = "A" };
        bool profileChanged = false;
        bool otherFiltersChanged = false;
        _filters.ProfileSelectionChanged += () => profileChanged = true;
        _filters.OtherFiltersChanged += () => otherFiltersChanged = true;

        _filters.SetSingleActiveProfile(profile);

        Assert.False(profileChanged);
        Assert.False(otherFiltersChanged);
    }

    // A team game has one matchup per opponent, and it should match if ANY of them is checked, the same
    // way MMR ORs across opponents. Guards the fix for a regression from moving the Games tab onto
    // IFilter, where CheckboxFilterSlotViewModel.GetFilter wrapped in a SequentialAllFilter and so
    // required every one of the game's matchups to be checked. The TvT row is the negative control: no
    // opponent is Terran, so it must stay excluded.
    [Theory]
    [InlineData(Race.Protoss, true)]
    [InlineData(Race.Zerg, true)]
    [InlineData(Race.Terran, false)]
    public void GetFilter_Matchup_TeamGame_MatchesWhenAnyOpponentsMatchupIsChecked(Race checkedOpponent, bool expected)
    {
        GameData game = CreateGame(selfRace: 'T', opponents: [Opponent('Z'), Opponent('P')]);
        FilterHandle matchup = FilterPanel.Of(_filters).Add("Matchup");
        matchup.Check($"{Race.Terran.Display()}v{checkedOpponent.Display()}");

        Assert.Equal(expected, _filters.GetFilter().MatchesFilter(game));
    }

    // Control for the test above: with a single opponent, "any" and "all" agree, so this passes either
    // way — if it ever fails, the problem is the harness or another filter, not the matchup aggregation.
    [Theory]
    [InlineData(Race.Protoss, true)]
    [InlineData(Race.Zerg, false)]
    public void GetFilter_Matchup_OneVsOne_MatchesOnlyTheCheckedMatchup(Race checkedOpponent, bool expected)
    {
        GameData game = CreateGame(selfRace: 'T', opponents: [Opponent('P')]);
        FilterHandle matchup = FilterPanel.Of(_filters).Add("Matchup");
        matchup.Check($"{Race.Terran.Display()}v{checkedOpponent.Display()}");

        Assert.Equal(expected, _filters.GetFilter().MatchesFilter(game));
    }

    // The Me/Ally/Opponent radio decides whose builds the filter looks at, and only that group's: the same
    // game and the same build match only for whoever actually played it.
    [Theory]
    [InlineData(PlayerGroup.Me, PlayerGroup.Me, true)]
    [InlineData(PlayerGroup.Me, PlayerGroup.Ally, false)]
    [InlineData(PlayerGroup.Me, PlayerGroup.Opponent, false)]
    [InlineData(PlayerGroup.Ally, PlayerGroup.Me, false)]
    [InlineData(PlayerGroup.Ally, PlayerGroup.Ally, true)]
    [InlineData(PlayerGroup.Ally, PlayerGroup.Opponent, false)]
    [InlineData(PlayerGroup.Opponent, PlayerGroup.Me, false)]
    [InlineData(PlayerGroup.Opponent, PlayerGroup.Ally, false)]
    [InlineData(PlayerGroup.Opponent, PlayerGroup.Opponent, true)]
    public void GetFilter_Build_PlayerGroup_MatchesOnlyTheBuildsOfThatGroup(PlayerGroup playedBy, PlayerGroup asked, bool expected)
    {
        BuildNode build = new() { Name = "4 Gate", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _buildRepository.InsertBuild(build, null, 0);
        DataPageFiltersViewModel filters = CreateFilters();
        // A 2v2, so all three groups have someone in them to have played it.
        GameData game = CreateGame(selfRace: 'P', opponents: [Opponent('P')], allies: [Ally('P')]);
        PlayerIn(game, playedBy).BuildIds = [build.Id];

        FilterHandle filter = FilterPanel.Of(filters).Add("Builds", "PvP");
        filter.PlayerGroup = asked;
        filter.SelectBuild(build.Name);

        Assert.Equal(expected, filters.GetFilter().MatchesFilter(game));
    }

    private static GamePlayer PlayerIn(GameData game, PlayerGroup group) => group switch
    {
        PlayerGroup.Me => game.PlayerDetails[game.ReplayData.Player],
        PlayerGroup.Ally => game.PlayerDetails[game.ReplayData.Allies[0]],
        PlayerGroup.Opponent => game.PlayerDetails[game.ReplayData.Opponents[0]],
        _ => throw new NotImplementedException(),
    };

    private static ReplayPlayer Opponent(char race) =>
        new() { Name = "Foe", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3000 }, Race = race, Random = false };

    private static ReplayPlayer Ally(char race) =>
        new() { Name = "Mate", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3000 }, Race = race, Random = false };

    private static GameData CreateGame(char selfRace, ReplayPlayer[] opponents, ReplayPlayer[]? allies = null)
    {
        ParsedReplayData replay = new()
        {
            Map = new Map { Name = "Altitude LE" },
            GameLengthSeconds = 600,
            ReplayPath = "replay.SC2Replay",
            ReplayTimestamp = new DateTimeOffset(2026, 1, 15, 18, 30, 0, TimeSpan.Zero),
            Win = 1m,
            Player = new ReplayPlayer { Name = "Me", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3000 }, Race = selfRace, Random = false },
            Allies = allies ?? [],
            Opponents = opponents,
        };
        return new GameData(replay);
    }

    private CheckboxFilterOptionViewModel<int> ProfileOption(int profileId) =>
        _filters.ProfileSlot.Options.Single(o => o.Value == profileId);

    public void Dispose()
    {
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
}
