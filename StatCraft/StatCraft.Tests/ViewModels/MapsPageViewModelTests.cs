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

    // An applied attribute filter sorts maps into three cases, and only the middle one is IncludeUnset's
    // business:
    //   1. no value row for the attribute at all — the map doesn't participate in that dimension, so
    //      applying the filter excludes it outright, IncludeUnset or not
    //   2. a value row with nothing set in it — IncludeUnset decides
    //   3. a value row with a value — the value has to match
    // Case 1 arises for any map loaded from the DB that never had this attribute saved; case 2 for one
    // created through the page, which gets a row per attribute up front.

    // The happy path through the DataFiltering rewrite (case 3): a map whose value is one of the checked
    // options stays, one whose value is not is dropped.
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

    // A map that has a value row for the attribute, just with nothing set in it — case 2 below, so
    // IncludeUnset decides. This is what the a?.SelectedValue fix addressed: the mapped value now
    // reaches the filter as null rather than as "", so IncludeUnset is actually consulted.
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IncludeUnset_BoolAttribute_DecidesAMapWhoseValueRowIsUnset(bool includeUnset)
    {
        MapsPageViewModel vm = AttributeVm("Ramped", AttributeType.Bool);
        BoolFilterSlotViewModel slot = Assert.IsType<BoolFilterSlotViewModel>(Assert.Single(vm.HiddenFilterSlots));
        Map map = AddMapWithValueRows(vm);

        slot.IsApplied = true;
        slot.IncludeUnset = includeUnset;
        slot.Value = true;

        Assert.Equal(includeUnset, vm.FilteredMaps.Contains(map));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IncludeUnset_NumericAttribute_DecidesAMapWhoseValueRowIsUnset(bool includeUnset)
    {
        MapsPageViewModel vm = AttributeVm("Rush Distance", AttributeType.Numeric);
        NumericRangeFilterSlotViewModel slot = Assert.IsType<NumericRangeFilterSlotViewModel>(Assert.Single(vm.HiddenFilterSlots));
        Map map = AddMapWithValueRows(vm);

        slot.IsApplied = true;
        slot.IncludeUnset = includeUnset;
        slot.Min = 10;

        Assert.Equal(includeUnset, vm.FilteredMaps.Contains(map));
    }

    // Case 1, and deliberately NOT the same as case 2: a map with no value row for the attribute at all
    // doesn't participate in that dimension, so applying the filter excludes it outright. IncludeUnset
    // only speaks to a map that has the attribute but hasn't had a value put in it yet, so it must not
    // rescue this map. Maps loaded from the DB only carry rows for values that were actually saved (see
    // MapRepository.GetAllMaps), which is how a map ends up here.
    [Fact]
    public void AppliedValuesFilter_ExcludesAMapWithNoValueRow_EvenWithIncludeUnset()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        _attributeRepo.InsertValueOption(attribute.Id, "Rush");
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo, _filterSlotFactory);
        CheckboxFilterSlotViewModel<string> slot = Assert.IsType<CheckboxFilterSlotViewModel<string>>(Assert.Single(vm.HiddenFilterSlots));
        Assert.Empty(vm.FilteredMaps.Single().AttributeValues);

        slot.IsApplied = true;
        slot.IncludeUnset = true;
        slot.Options.Single(o => o.Value == "Rush").IsChecked = true;

        Assert.Empty(vm.FilteredMaps);
    }

    [Fact]
    public void AppliedBoolFilter_ExcludesAMapWithNoValueRow_EvenWithIncludeUnset()
    {
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = AttributeVm("Ramped", AttributeType.Bool);
        BoolFilterSlotViewModel slot = Assert.IsType<BoolFilterSlotViewModel>(Assert.Single(vm.HiddenFilterSlots));

        slot.IsApplied = true;
        slot.IncludeUnset = true;
        slot.Value = true;

        Assert.Empty(vm.FilteredMaps);
    }

    [Fact]
    public void AppliedNumericFilter_ExcludesAMapWithNoValueRow_EvenWithIncludeUnset()
    {
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = AttributeVm("Rush Distance", AttributeType.Numeric);
        NumericRangeFilterSlotViewModel slot = Assert.IsType<NumericRangeFilterSlotViewModel>(Assert.Single(vm.HiddenFilterSlots));

        slot.IsApplied = true;
        slot.IncludeUnset = true;
        slot.Min = 10;

        Assert.Empty(vm.FilteredMaps);
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

    private MapsPageViewModel AttributeVm(string name, AttributeType type)
    {
        _attributeRepo.InsertAttribute(new(AttributeScope.Map) { Name = name, Type = type }, 0);
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
