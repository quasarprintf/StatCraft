using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Maps;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataParsing;
using StatCraft.ViewModels.Windows.Filters;

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
        _filters = new DataPageFiltersViewModel(_buildRepository, new System.Collections.ObjectModel.ObservableCollection<Models.GameData.Attributes.AttributeDefinition>(), new StatCraft.Services.Factories.FilterSlotFactory());
        // The mandatory date range starts on today; the GetFilter tests use fixed-date games, so they
        // clear it rather than depend on when they run.
        _filters.DateSlot.FromDate = null;
        _filters.DateSlot.ToDate = null;
    }

    [Fact]
    public void ExtraFilterSlots_AreHiddenByDefault_ExceptTheMandatoryProfileAndDateRange()
    {
        Assert.All(_filters.ExtraFilterSlots, slot => Assert.True(slot.Filter == null || slot.Filter.Mandatory || !slot.IsApplied));
        Assert.Equal(5, _filters.HiddenExtraFilterSlots.Count());
        Assert.Equal([_filters.ProfileSlot, _filters.DateSlot], _filters.VisibleExtraFilterSlots);
    }

    [Fact]
    public void AddCommand_MakesSlotVisible()
    {
        _filters.MapSlot.AddCommand.Execute(null);

        Assert.True(_filters.MapSlot.IsApplied);
        Assert.Contains(_filters.MapSlot, _filters.VisibleExtraFilterSlots);
        Assert.DoesNotContain(_filters.MapSlot, _filters.HiddenExtraFilterSlots.Select(s => s.Filter));
    }

    [Fact]
    public void RemoveCommand_HidesSlotAndClearsSelection()
    {
        Map altitude = new() { Name = "Altitude LE" };
        Map deathaura = new() { Name = "Deauthaura LE" };
        _filters.RefreshMapOptions([altitude, deathaura]);
        _filters.MapSlot.AddCommand.Execute(null);
        CheckboxFilterOptionViewModel<Map> option = _filters.MapSlot.Options[0];
        option.IsChecked = true;

        _filters.MapSlot.RemoveCommand.Execute(null);

        Assert.False(_filters.MapSlot.IsApplied);
        Assert.All(_filters.MapSlot.Options, o => Assert.False(o.IsChecked));
    }

    [Fact]
    public void RemoveCommand_OnNumericRangeSlot_ClearsMinAndMax()
    {
        _filters.MmrSlot.Min = 1000;
        _filters.MmrSlot.Max = 2000;

        _filters.MmrSlot.RemoveCommand.Execute(null);

        Assert.False(_filters.MmrSlot.IsApplied);
        Assert.Null(_filters.MmrSlot.Min);
        Assert.Null(_filters.MmrSlot.Max);
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
    public void RefreshMapOptions_PreservesCheckedStateByName()
    {
        Map altitude = new() { Name = "Altitude", Id = 1 };
        Map deathaura = new() { Name = "Deauthaura", Id = 2 };
        Map leylines = new() { Name = "Ley Lines", Id = 3 };
        _filters.RefreshMapOptions([altitude, deathaura]);
        _filters.MapSlot.Options.Single(o => o.Label == "Altitude").IsChecked = true;

        _filters.RefreshMapOptions([altitude, deathaura, leylines]);

        Assert.True(_filters.MapSlot.Options.Single(o => o.Label == "Altitude").IsChecked);
        Assert.False(_filters.MapSlot.Options.Single(o => o.Label == "Ley Lines").IsChecked);
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
        _filters.MatchupSlot.AddCommand.Execute(null);
        _filters.MatchupSlot.Options.Single(o => o.Value == (Race.Terran, checkedOpponent)).IsChecked = true;

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
        _filters.MatchupSlot.AddCommand.Execute(null);
        _filters.MatchupSlot.Options.Single(o => o.Value == (Race.Terran, checkedOpponent)).IsChecked = true;

        Assert.Equal(expected, _filters.GetFilter().MatchesFilter(game));
    }

    private static GamePlayer Opponent(char race) =>
        new() { Name = "Foe", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3000 }, Race = race, Random = false };

    private static GameData CreateGame(char selfRace, GamePlayer[] opponents)
    {
        ParsedReplayData replay = new()
        {
            GameLengthSeconds = 600,
            ReplayPath = "replay.SC2Replay",
            ReplayTimestamp = new DateTimeOffset(2026, 1, 15, 18, 30, 0, TimeSpan.Zero),
            Win = 1m,
            Player = new GamePlayer { Name = "Me", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3000 }, Race = selfRace, Random = false, BuildIds = [] },
            Allies = [],
            Opponents = opponents,
        };
        return new GameData { Map = new Map { Name = "Altitude LE" }, ReplayData = replay };
    }

    private CheckboxFilterOptionViewModel<Sc2Profile> ProfileOption(int profileId) =>
        _filters.ProfileSlot.Options.Single(o => o.Value.Id == profileId);

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
