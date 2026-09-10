using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.Factories;
using StatCraft.ViewModels.Windows;
using StatCraft.ViewModels.Windows.Filters;

namespace StatCraft.Tests;

public class BuildsPageViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly BuildRepository _buildRepo;
    private readonly AttributeRepository _attributeRepo;
    private readonly BuildsPageViewModel _vm;

    public BuildsPageViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");

        _buildRepo = new BuildRepository(_dbPath);
        _buildRepo.Initialize();
        MapRepository mapRepository = new(_dbPath);
        mapRepository.Initialize();
        GameDataRepository gameDataRepository = new(_dbPath);
        gameDataRepository.Initialize();
        _attributeRepo = new AttributeRepository(_dbPath);
        _attributeRepo.Initialize();

        _vm = new BuildsPageViewModel(_buildRepo, _attributeRepo, gameDataRepository, new FilterSlotFactory())
        {
            PlayerRace = Race.Protoss,
        };
        _vm.AddBuildCommand.Execute(null);
    }

    // Pins BuildsPageViewModel's own contribution on top of BuildRepository.ChangeBuildDetailSortOrder
    // (covered separately in BuildRepositoryTests): the in-memory SelectedBuild.Details collection has
    // to end up reordered too, in lockstep with the DB write, since that's what the drag-drop UI is
    // actually bound to.
    [Fact]
    public void ChangeDetailIndex_ReordersSelectedBuildDetailsInMemoryAndPersists()
    {
        AddDetail("A");
        AddDetail("B");
        AddDetail("C");

        _vm.ChangeDetailIndex(sourceIndex: 2, targetIndex: 0);

        Assert.Equal(["C", "A", "B"], _vm.SelectedBuild!.Details.Select(d => d.Name));

        // Persisted, not just reordered in memory — a fresh load agrees.
        Assert.Equal(["C", "A", "B"], _buildRepo.GetBuildsForPlayerRace(Race.Protoss).Single().Details.Select(d => d.Name));
    }

    [Fact]
    public void ChangeDetailIndex_NoSelectedBuild_DoesNotThrow()
    {
        _vm.SelectedBuild = null;

        _vm.ChangeDetailIndex(0, 1);
    }

    // BuildsPageViewModel.GetFilters/SlotFilter are a verbatim copy of the MapsPageViewModel pair, so the
    // same filtering behaviour has to be pinned on both sides — fixing one and not the other is the
    // realistic failure mode.

    // Guards the whitespace-name fix: without AcceptNull tracking whether the search box is empty, a
    // build whose name was cleared drops out of the tree and can't be found again to fix.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankBuildName_StillMatchesWhenNoNameFilterIsSet(string name)
    {
        _vm.SelectedBuild!.Name = name;

        // Force a filter pass with an empty box, the way clearing the search field would.
        _vm.NameFilter = "zzz";
        _vm.NameFilter = "";

        Assert.True(_vm.SelectedBuild.MatchesFilter);
    }

    [Fact]
    public void BlankBuildName_IsStillExcludedByANonEmptyNameFilter()
    {
        _vm.SelectedBuild!.Name = "";

        _vm.NameFilter = "Gateway";

        Assert.False(_vm.SelectedBuild.MatchesFilter);
    }

    // FAILING — the Builds half of the "Include unset" regression. A build loaded without a stored value
    // for the attribute has no AttributeValue row, and GetFilters wraps each slot's filter in an AndFilter
    // whose AcceptNull is never set, so the wrapper rejects the build before the inner filter (the one
    // carrying IncludeUnset) is consulted. Setting AcceptNull = slot.IncludeUnset on the wrapper is the fix.
    [Fact]
    public void IncludeUnset_BoolAttribute_KeepsABuildWithNoStoredValue()
    {
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Cheese", Type = AttributeType.Bool };
        _attributeRepo.InsertAttribute(attribute, 0);
        BoolFilterSlotViewModel slot = Assert.IsType<BoolFilterSlotViewModel>(_vm.HiddenFilterSlots.Single(s => s.Title == "Cheese"));

        slot.IsApplied = true;
        slot.IncludeUnset = true;
        slot.Value = true;

        Assert.True(_vm.SelectedBuild!.MatchesFilter);
    }

    private void AddDetail(string name)
    {
        _vm.AddDetailCommand.Execute(null);
        AttributeDefinition added = _vm.SelectedBuild!.Details[^1];
        added.Name = name;
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (IOException)
        {
        }
    }
}
