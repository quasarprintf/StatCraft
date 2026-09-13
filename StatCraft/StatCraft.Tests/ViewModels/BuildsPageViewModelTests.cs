using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
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
    private readonly GameDataRepository _gameDataRepo;
    private readonly BuildsPageViewModel _vm;

    public BuildsPageViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");

        _buildRepo = new BuildRepository(_dbPath);
        _buildRepo.Initialize();
        MapRepository mapRepository = new(_dbPath);
        mapRepository.Initialize();
        _gameDataRepo = new GameDataRepository(_dbPath);
        _gameDataRepo.Initialize();
        _attributeRepo = new AttributeRepository(_dbPath);
        _attributeRepo.Initialize();

        _vm = new BuildsPageViewModel(_buildRepo, _attributeRepo, _gameDataRepo, new FilterSlotFactory())
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

    // Case 1 (see the three-case note in MapsPageViewModelTests): a build with no value row for the
    // attribute doesn't participate in that dimension, so applying the filter excludes it outright and
    // IncludeUnset must not rescue it. A non-mandatory attribute is only backfilled onto builds when it
    // is mandatory (BuildsPageViewModel's new-attribute sync), which is how a build ends up here.
    [Fact]
    public void AppliedBoolFilter_ExcludesABuildWithNoValueRow_EvenWithIncludeUnset()
    {
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Cheese", Type = AttributeType.Bool };
        _attributeRepo.InsertAttribute(attribute, 0);
        BoolFilterSlotViewModel<AttributeValue> slot = InnerSlot<BoolFilterSlotViewModel<AttributeValue>>(_vm.HiddenFilterSlots.Single(s => s.Title == "Cheese"));
        Assert.DoesNotContain(_vm.SelectedBuild!.AttributeValues, v => v.Definition.Id == attribute.Id);

        slot.IsApplied = true;
        slot.IncludeUnset = true;
        slot.Value = true;

        Assert.False(_vm.SelectedBuild.MatchesFilter);
    }

    // Case 2: a mandatory attribute is backfilled onto every build with an empty value, so those builds
    // do have a row and IncludeUnset decides for them.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IncludeUnset_BoolAttribute_DecidesABuildWhoseValueRowIsUnset(bool includeUnset)
    {
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Cheese", Type = AttributeType.Bool, IsMandatory = true };
        _attributeRepo.InsertAttribute(attribute, 0);
        BoolFilterSlotViewModel<AttributeValue> slot = InnerSlot<BoolFilterSlotViewModel<AttributeValue>>(_vm.HiddenFilterSlots.Single(s => s.Title == "Cheese"));
        Assert.Contains(_vm.SelectedBuild!.AttributeValues, v => v.Definition.Id == attribute.Id);

        slot.IsApplied = true;
        slot.IncludeUnset = includeUnset;
        slot.Value = true;

        Assert.Equal(includeUnset, _vm.SelectedBuild.MatchesFilter);
    }

    // The Builds twin of MapsPageViewModelTests.ValueOptionAddedElsewhere_PatchesTheFilterSlotPreservingWhatWasChecked.
    // The checkbox slot keeps its own option list, built when the slot is created, so an option added on
    // the Attributes tab has to be patched in without dropping what the user already checked.
    [Fact]
    public void ValueOptionAddedElsewhere_PatchesTheFilterSlotPreservingWhatWasChecked()
    {
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        _attributeRepo.InsertValueOption(attribute.Id, "Rush");
        // A page built after the attribute exists, so the slot starts with the option already on it —
        // the same starting point as the Maps test.
        BuildsPageViewModel vm = new(_buildRepo, _attributeRepo, _gameDataRepo, new FilterSlotFactory())
        {
            PlayerRace = Race.Protoss,
        };
        CheckboxFilterSlotViewModel<AttributeValue, string?> slot =
            InnerSlot<CheckboxFilterSlotViewModel<AttributeValue, string?>>(vm.HiddenFilterSlots.Single(s => s.Title == "Style"));
        slot.Options.Single(o => o.Value == "Rush").IsChecked = true;

        _attributeRepo.InsertValueOption(attribute.Id, "Macro");

        Assert.Equal(["Rush", "Macro"], slot.Options.Select(o => o.Value));
        Assert.True(slot.Options.Single(o => o.Value == "Rush").IsChecked);
        Assert.False(slot.Options.Single(o => o.Value == "Macro").IsChecked);
    }

    // An attribute filter slot is an AttributeFilterSlotViewModel wrapper: the kind-specific slot lives
    // inside it and filters an AttributeValue, while the wrapper is what projects a build onto its value
    // row. Tests that need Value/Min/Options reach through to the inner one.
    private static TSlot InnerSlot<TSlot>(IFilterSlotViewModel slot) where TSlot : class =>
        Assert.IsType<TSlot>(((IWrappedFilterSlotViewModel)slot).WrappedFilter);

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
