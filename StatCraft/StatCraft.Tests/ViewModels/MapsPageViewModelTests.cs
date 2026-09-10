using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Maps;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.Factories;
using StatCraft.ViewModels.Windows;
using StatCraft.ViewModels.Windows.Filters;

namespace StatCraft.Tests;

public class MapsPageViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly MapRepository _mapRepo;
    private readonly AttributeRepository _attributeRepo;
    private readonly GameDataRepository _gameDataRepo;
    private readonly FilterSlotFactory _filterSlotFactory = new();

    public MapsPageViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");
        _mapRepo = new MapRepository(_dbPath);
        _mapRepo.Initialize();
        _attributeRepo = new AttributeRepository(_dbPath);
        _attributeRepo.Initialize();
        _gameDataRepo = new GameDataRepository(_dbPath);
        _gameDataRepo.Initialize();
    }

    // Exercised through NameFilter/FilteredMaps rather than calling MatchesName directly, per the comment on it.
    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("alt", true)]
    [InlineData("ALT", true)]
    [InlineData(" LE ", true)]
    [InlineData("Rorschach", false)]
    public void NameFilter_IsCaseInsensitiveSubstringMatch(string filter, bool expected)
    {
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);

        vm.NameFilter = filter;

        Assert.Equal(expected, vm.FilteredMaps.Any(m => m.Name == "Altitude LE"));
    }

    // Pins the fix for a real bug: MapsPageViewModel loaded its attribute list once at construction and
    // never refreshed it, so an attribute added elsewhere (the Attributes tab, sharing the same
    // AttributeRepository) never showed up here without restarting the app.
    [Fact]
    public void AttributeAddedElsewhere_AppearsHereAndOnEveryExistingMap()
    {
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);

        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Rush Distance", IsMandatory = true };
        _attributeRepo.InsertAttribute(attribute, 0);

        Assert.Contains(vm.AllAttributes, a => a.Id == attribute.Id);
        Assert.Contains(vm.FilteredMaps.Single().AttributeValues, v => v.Definition.Id == attribute.Id);
    }

    [Fact]
    public void AttributeDeletedElsewhere_DisappearsHereAndFromEveryMap()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Doomed" };
        _attributeRepo.InsertAttribute(attribute, 0);
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);

        _attributeRepo.DeleteAttribute(attribute.Id);

        Assert.DoesNotContain(vm.AllAttributes, a => a.Id == attribute.Id);
        Assert.DoesNotContain(vm.FilteredMaps.Single().AttributeValues, v => v.Definition.Id == attribute.Id);
    }

    // A Game- or Build-scoped attribute change is irrelevant to this page; reconciling shouldn't pull it
    // in just because AttributesChanged fired.
    [Fact]
    public void AttributeAddedElsewhereForADifferentScope_IsIgnored()
    {
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);

        _attributeRepo.InsertAttribute(new(AttributeScope.Game) { Name = "Apm" }, 0);

        Assert.Empty(vm.AllAttributes);
    }

    // Pins the fix for a real bug: this page loads its own AttributeDefinition instances, separate from
    // whatever instance the Attributes tab is editing, so a rename/retype made there never touched this
    // page's copy — the value editor kept showing the old type, and the label kept the old name. The fix
    // patches the same instance this page (and every map's AttributeValues, and the bound editors) already
    // holds, in place, rather than replacing it.
    [Fact]
    public void AttributeTypeChangedElsewhere_UpdatesTheSameInstanceThisPageAlreadyHolds()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Rush Distance", Type = AttributeType.Numeric, IsMandatory = true };
        _attributeRepo.InsertAttribute(attribute, 0);
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);
        AttributeDefinition held = Assert.Single(vm.AllAttributes);
        Assert.IsType<NumericRangeFilterSlotViewModel>(Assert.Single(vm.HiddenFilterSlots));

        AttributeDefinition editedElsewhere = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Map));
        editedElsewhere.Type = AttributeType.Bool;
        _attributeRepo.UpdateAttribute(editedElsewhere);

        Assert.Equal(AttributeType.Bool, held.Type);
        Assert.Same(held, Assert.Single(vm.AllAttributes));
        Assert.Same(held, Assert.Single(vm.FilteredMaps.Single().AttributeValues).Definition);
        // Numeric/Bool/Values are different FilterSlotViewModel subclasses, so the slot itself must be
        // swapped, not just have a property change underneath it.
        Assert.IsType<BoolFilterSlotViewModel>(Assert.Single(vm.HiddenFilterSlots));
    }

    [Fact]
    public void AttributeNameChangedElsewhere_UpdatesTheSameInstanceThisPageAlreadyHolds()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Old Name" };
        _attributeRepo.InsertAttribute(attribute, 0);
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);
        AttributeDefinition held = Assert.Single(vm.AllAttributes);

        AttributeDefinition editedElsewhere = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Map));
        editedElsewhere.Name = "New Name";
        _attributeRepo.UpdateAttribute(editedElsewhere);

        Assert.Equal("New Name", held.Name);
        Assert.Equal("New Name", Assert.Single(vm.HiddenFilterSlots).Title);
    }

    [Fact]
    public void ValueOptionAddedElsewhere_AppearsOnTheSameInstance()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);
        AttributeDefinition held = Assert.Single(vm.AllAttributes);

        _attributeRepo.InsertValueOption(attribute.Id, "Rush");

        Assert.Equal(["Rush"], held.ValueOptions);
    }

    // The checkbox filter slot keeps its own separate option list (built once at slot-creation time), so
    // adding an option elsewhere has to patch it too — and preserve whatever the user already checked.
    [Fact]
    public void ValueOptionAddedElsewhere_PatchesTheFilterSlotPreservingWhatWasChecked()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        _attributeRepo.InsertValueOption(attribute.Id, "Rush");
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);
        CheckboxFilterSlotViewModel<string> slot = Assert.IsType<CheckboxFilterSlotViewModel<string>>(Assert.Single(vm.HiddenFilterSlots));
        slot.Options.Single(o => o.Value == "Rush").IsChecked = true;

        _attributeRepo.InsertValueOption(attribute.Id, "Macro");

        Assert.Equal(["Rush", "Macro"], slot.Options.Select(o => o.Value));
        Assert.True(slot.Options.Single(o => o.Value == "Rush").IsChecked);
        Assert.False(slot.Options.Single(o => o.Value == "Macro").IsChecked);
    }

    // The happy path through the DataFiltering rewrite: a map whose value is one of the checked options
    // stays, one whose value is not is dropped.
    [Theory]
    [InlineData("Rush", true)]
    [InlineData("Macro", false)]
    public void CheckedOption_KeepsOnlyMapsHoldingThatValue(string mapValue, bool expected)
    {
        MapsPageViewModel vm = ValuesAttributeVm();
        CheckboxFilterSlotViewModel<string> slot = Assert.IsType<CheckboxFilterSlotViewModel<string>>(Assert.Single(vm.HiddenFilterSlots));
        Map map = AddMapWithValueRows(vm);
        map.AttributeValues.Single().SelectedValue = mapValue;

        slot.IsApplied = true;
        slot.Options.Single(o => o.Value == "Rush").IsChecked = true;

        Assert.Equal(expected, vm.FilteredMaps.Contains(map));
    }

    [Fact]
    public void AttributeFilterWithoutIncludeUnset_DropsAMapWhoseValueIsUnset()
    {
        MapsPageViewModel vm = ValuesAttributeVm();
        CheckboxFilterSlotViewModel<string> slot = Assert.IsType<CheckboxFilterSlotViewModel<string>>(Assert.Single(vm.HiddenFilterSlots));
        Map map = AddMapWithValueRows(vm);

        slot.IsApplied = true;
        slot.IncludeUnset = false;
        slot.Options.Single(o => o.Value == "Rush").IsChecked = true;

        Assert.DoesNotContain(map, vm.FilteredMaps);
    }

    // A map that has a value row for the attribute, just with nothing selected in it. This is the case
    // the a?.SelectedValue fix addressed — the mapped value now reaches the filter as null rather than
    // as "", so IncludeUnset is consulted.
    [Fact]
    public void IncludeUnset_ValuesAttribute_KeepsAMapWhoseValueRowIsUnset()
    {
        MapsPageViewModel vm = ValuesAttributeVm();
        CheckboxFilterSlotViewModel<string> slot = Assert.IsType<CheckboxFilterSlotViewModel<string>>(Assert.Single(vm.HiddenFilterSlots));
        Map map = AddMapWithValueRows(vm);

        slot.IsApplied = true;
        slot.IncludeUnset = true;
        slot.Options.Single(o => o.Value == "Rush").IsChecked = true;

        Assert.Contains(map, vm.FilteredMaps);
    }

    // FAILING — pins the remaining half of the "Include unset" regression. A map loaded from the DB only
    // materialises AttributeValue rows that were actually saved (see MapRepository.GetAllMaps), so a map
    // that never had this attribute set has no row at all. GetFilters wraps each slot's filter in an
    // AndFilter whose AcceptNull is never set, so that map is rejected by the wrapper before the inner
    // filter — the one carrying IncludeUnset — is ever consulted. Setting AcceptNull = slot.IncludeUnset
    // on the wrapper is the fix. The deleted AttributeFilterTests pinned this as
    // MatchesSelection_UnsetValueWithCheckedOptions_FollowsIncludeUnset.
    [Fact]
    public void IncludeUnset_ValuesAttribute_KeepsAMapLoadedWithNoStoredValue()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        _attributeRepo.InsertValueOption(attribute.Id, "Rush");
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);
        CheckboxFilterSlotViewModel<string> slot = Assert.IsType<CheckboxFilterSlotViewModel<string>>(Assert.Single(vm.HiddenFilterSlots));

        slot.IsApplied = true;
        slot.IncludeUnset = true;
        slot.Options.Single(o => o.Value == "Rush").IsChecked = true;

        Assert.Contains(vm.FilteredMaps, m => m.Name == "Altitude LE");
    }

    // FAILING — same wrapper bug, bool attribute. Was MatchesBool_UnsetValueWithFilterSet_FollowsIncludeUnset.
    [Fact]
    public void IncludeUnset_BoolAttribute_KeepsAMapLoadedWithNoStoredValue()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Ramped", Type = AttributeType.Bool };
        _attributeRepo.InsertAttribute(attribute, 0);
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);
        BoolFilterSlotViewModel slot = Assert.IsType<BoolFilterSlotViewModel>(Assert.Single(vm.HiddenFilterSlots));

        slot.IsApplied = true;
        slot.IncludeUnset = true;
        slot.Value = true;

        Assert.Contains(vm.FilteredMaps, m => m.Name == "Altitude LE");
    }

    // FAILING — same wrapper bug, numeric attribute. Was MatchesRange_UnsetValueWithActiveBounds_FollowsIncludeUnset.
    [Fact]
    public void IncludeUnset_NumericAttribute_KeepsAMapLoadedWithNoStoredValue()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Rush Distance", Type = AttributeType.Numeric };
        _attributeRepo.InsertAttribute(attribute, 0);
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);
        NumericRangeFilterSlotViewModel slot = Assert.IsType<NumericRangeFilterSlotViewModel>(Assert.Single(vm.HiddenFilterSlots));

        slot.IsApplied = true;
        slot.IncludeUnset = true;
        slot.Min = 10;

        Assert.Contains(vm.FilteredMaps, m => m.Name == "Altitude LE");
    }

    // Guards the whitespace-name fix: StringFilter treats a blank candidate as "no value", so without
    // AcceptNull tracking whether the search box is empty, a map whose name was cleared would vanish from
    // the list and could never be found again to fix.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankMapName_IsStillListedWhenNoNameFilterIsSet(string name)
    {
        _mapRepo.InsertMap(new() { Name = name });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);

        Assert.Single(vm.FilteredMaps);

        // And it stays listed after the box is typed into and cleared again, not just on first load.
        vm.NameFilter = "zzz";
        vm.NameFilter = "";

        Assert.Single(vm.FilteredMaps);
    }

    [Fact]
    public void BlankMapName_IsStillExcludedByANonEmptyNameFilter()
    {
        _mapRepo.InsertMap(new() { Name = "" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);

        vm.NameFilter = "Altitude";

        Assert.Empty(vm.FilteredMaps);
    }

    private MapsPageViewModel ValuesAttributeVm()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        _attributeRepo.InsertValueOption(attribute.Id, "Rush");
        _attributeRepo.InsertValueOption(attribute.Id, "Macro");
        return new MapsPageViewModel(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);
    }

    // Adding through the page is what gives a map a value row per attribute; maps loaded straight from
    // the DB only carry rows for values that were actually saved.
    private static Map AddMapWithValueRows(MapsPageViewModel vm)
    {
        vm.AddMapCommand.Execute(null);
        return vm.SelectedMap!;
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
            // Best-effort cleanup.
        }
    }
}
