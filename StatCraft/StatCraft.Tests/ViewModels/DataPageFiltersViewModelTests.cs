using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataFiltering;
using StatCraft.ViewModels;

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
        _filters.RefreshMapOptions(["Altitude", "Deathaura"]);
        _filters.MapSlot.AddCommand.Execute(null);
        CheckboxFilterOptionViewModel<string> option = (CheckboxFilterOptionViewModel<string>)_filters.MapSlot.Options[0];
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
        _filters.RefreshMapOptions(["Altitude", "Deathaura"]);
        ((CheckboxFilterOptionViewModel<string>)_filters.MapSlot.Options.Single(o => o.Label == "Altitude")).IsChecked = true;

        _filters.RefreshMapOptions(["Altitude", "Deathaura", "Ley Lines"]);

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
        _filters.RefreshMapOptions(["Altitude"]);
        ((CheckboxFilterOptionViewModel<string>)_filters.MapSlot.Options[0]).IsChecked = true;
        _filters.MmrSlot.Min = 1000;
        _filters.MmrSlot.Max = 2000;

        CheckboxFilterOptionViewModel<GameOutcome> winOption =
            (CheckboxFilterOptionViewModel<GameOutcome>)_filters.OutcomeSlot.Options.Single(o => o.Label == "Win");
        winOption.IsChecked = true;

        CheckboxFilterOptionViewModel<(Race, Race)> tvzOption = _filters.MatchupSlot.Options
            .Cast<CheckboxFilterOptionViewModel<(Race, Race)>>()
            .Single(o => o.Value == (Race.T, Race.Z));
        tvzOption.IsChecked = true;

        GameFilterCriteria criteria = _filters.BuildCriteria();

        Assert.True(criteria.Maps!.SetEquals(["Altitude"]));
        Assert.Equal(1000, criteria.MinOpponentMmr);
        Assert.Equal(2000, criteria.MaxOpponentMmr);
        Assert.True(criteria.Outcomes!.SetEquals([GameOutcome.Win]));
        Assert.True(criteria.MatchupPairs!.SetEquals([(Race.T, Race.Z)]));
    }

    [Fact]
    public void BuildCriteria_BuildFilter_ExpandsCheckedBuildToItsSubtree()
    {
        BuildNode parent = new() { Name = "Parent", PlayerRace = Race.T };
        _buildRepository.InsertBuild(parent, null, 0);
        BuildNode child = new() { Name = "Child", PlayerRace = Race.T };
        _buildRepository.InsertBuild(child, parent.Id, 0);

        DataPageFiltersViewModel filters = new(_buildRepository);
        CheckboxFilterOptionViewModel<BuildNode> parentOption =
            (CheckboxFilterOptionViewModel<BuildNode>)filters.BuildSlot.Options.Single(o => o.Label.Contains("Parent"));
        parentOption.IsChecked = true;

        GameFilterCriteria criteria = filters.BuildCriteria();

        Assert.True(criteria.BuildIds!.SetEquals([parent.Id, child.Id]));
    }

    private CheckboxFilterOptionViewModel<Sc2Profile> ProfileOption(int profileId) =>
        _filters.ProfileSlot.Options.Cast<CheckboxFilterOptionViewModel<Sc2Profile>>().Single(o => o.Value.Id == profileId);

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
