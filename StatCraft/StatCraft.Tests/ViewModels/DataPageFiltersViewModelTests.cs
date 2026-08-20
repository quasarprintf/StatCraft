using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Maps;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataFiltering;
using StatCraft.ViewModels.Windows.DataComponents;

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
        _filters = new DataPageFiltersViewModel(_buildRepository);
    }

    [Fact]
    public void ExtraFilterSlots_AreHiddenByDefault()
    {
        Assert.All(_filters.ExtraFilterSlots, slot => Assert.False(slot.IsVisible));
        Assert.Equal(5, _filters.HiddenExtraFilterSlots.Count());
        Assert.Empty(_filters.VisibleExtraFilterSlots);
    }

    [Fact]
    public void AddCommand_MakesSlotVisible()
    {
        _filters.MapSlot.AddCommand.Execute(null);

        Assert.True(_filters.MapSlot.IsVisible);
        Assert.Contains(_filters.MapSlot, _filters.VisibleExtraFilterSlots);
        Assert.DoesNotContain(_filters.MapSlot, _filters.HiddenExtraFilterSlots);
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

        Assert.False(_filters.MapSlot.IsVisible);
        Assert.All(_filters.MapSlot.Options, o => Assert.False(o.IsChecked));
    }

    [Fact]
    public void RemoveCommand_OnNumericRangeSlot_ClearsMinAndMax()
    {
        _filters.MmrSlot.Min = 1000;
        _filters.MmrSlot.Max = 2000;

        _filters.MmrSlot.RemoveCommand.Execute(null);

        Assert.False(_filters.MmrSlot.IsVisible);
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
        Assert.Equal(DateTime.Today, _filters.FromDate);
        Assert.Equal(DateTime.Today, _filters.ToDate);
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

    [Fact]
    public void BuildCriteria_ReflectsCheckedOptions()
    {
        Map altitude = new() { Name = "Altitude" };
        _filters.RefreshMapOptions([altitude]);
        _filters.MapSlot.Options[0].IsChecked = true;
        _filters.MmrSlot.Min = 1000;
        _filters.MmrSlot.Max = 2000;

        CheckboxFilterOptionViewModel<GameOutcome> winOption = _filters.OutcomeSlot.Options.Single(o => o.Label == "Win");
        winOption.IsChecked = true;

        CheckboxFilterOptionViewModel<(Race, Race)> tvzOption = _filters.MatchupSlot.Options.Single(o => o.Value == (Race.Terran, Race.Zerg));
        tvzOption.IsChecked = true;

        GameFilterCriteria criteria = _filters.BuildCriteria();

        Assert.True(criteria.Maps!.SetEquals([altitude]));
        Assert.Equal(1000, criteria.MinOpponentMmr);
        Assert.Equal(2000, criteria.MaxOpponentMmr);
        Assert.True(criteria.Outcomes!.SetEquals([GameOutcome.Win]));
        Assert.True(criteria.MatchupPairs!.SetEquals([(Race.Terran, Race.Zerg)]));
    }

    [Fact]
    public void BuildCriteria_BuildFilter_ExpandsCheckedBuildToItsSubtree()
    {
        BuildNode parent = new() { Name = "Parent", PlayerRace = Race.Terran };
        _buildRepository.InsertBuild(parent, null, 0);
        BuildNode child = new() { Name = "Child", PlayerRace = Race.Terran };
        _buildRepository.InsertBuild(child, parent.Id, 0);

        DataPageFiltersViewModel filters = new(_buildRepository);
        CheckboxFilterOptionViewModel<BuildNode> parentOption = filters.BuildSlot.Options.Single(o => o.Label.Contains("Parent"));
        parentOption.IsChecked = true;

        GameFilterCriteria criteria = filters.BuildCriteria();

        Assert.True(criteria.BuildIds!.SetEquals([parent.Id, child.Id]));
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
