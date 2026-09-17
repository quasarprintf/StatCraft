using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Maps;
using StatCraft.Services.DatabaseRepository;
using StatCraft.ViewModels.Windows;

namespace StatCraft.Tests;

public class MapsPageViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly MapRepository _mapRepo;
    private readonly AttributeRepository _attributeRepo;
    private readonly GameDataRepository _gameDataRepo;

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
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);

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
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);

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
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);

        _attributeRepo.DeleteAttribute(attribute.Id);

        Assert.DoesNotContain(vm.AllAttributes, a => a.Id == attribute.Id);
        Assert.DoesNotContain(vm.FilteredMaps.Single().AttributeValues, v => v.Definition.Id == attribute.Id);
    }

    // A Game- or Build-scoped attribute change is irrelevant to this page; reconciling shouldn't pull it
    // in just because AttributesChanged fired.
    [Fact]
    public void AttributeAddedElsewhereForADifferentScope_IsIgnored()
    {
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);

        _attributeRepo.InsertAttribute(new(AttributeScope.Game) { Name = "Apm" }, 0);

        Assert.Empty(vm.AllAttributes);
        Assert.Empty(FilterPanel.Of(vm).AddableTitles);
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
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        FilterPanel filters = FilterPanel.Of(vm);
        AttributeDefinition held = Assert.Single(vm.AllAttributes);
        Assert.True(filters.Offered("Rush Distance").IsNumericRangeFilter);

        AttributeDefinition editedElsewhere = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Map));
        editedElsewhere.Type = AttributeType.Bool;
        _attributeRepo.UpdateAttribute(editedElsewhere);

        Assert.Equal(AttributeType.Bool, held.Type);
        Assert.Same(held, Assert.Single(vm.AllAttributes));
        Assert.Same(held, Assert.Single(vm.FilteredMaps.Single().AttributeValues).Definition);
        // The filter has to become the new kind too, not keep offering a range for a yes/no attribute.
        Assert.True(filters.Offered("Rush Distance").IsBoolFilter);
    }

    [Fact]
    public void AttributeNameChangedElsewhere_UpdatesTheSameInstanceThisPageAlreadyHolds()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Old Name" };
        _attributeRepo.InsertAttribute(attribute, 0);
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        AttributeDefinition held = Assert.Single(vm.AllAttributes);

        AttributeDefinition editedElsewhere = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Map));
        editedElsewhere.Name = "New Name";
        _attributeRepo.UpdateAttribute(editedElsewhere);

        Assert.Equal("New Name", held.Name);
        Assert.Equal(["New Name"], FilterPanel.Of(vm).AddableTitles);
    }

    [Fact]
    public void ValueOptionAddedElsewhere_AppearsOnTheSameInstance()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
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
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        FilterHandle filter = FilterPanel.Of(vm).Offered("Style").Check("Rush");

        _attributeRepo.InsertValueOption(attribute.Id, "Macro");

        Assert.Equal(["Rush", "Macro"], filter.OptionLabels);
        Assert.Equal(["Rush"], filter.CheckedLabels);
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
        Map map = AddMapWithValueRows(vm);
        map.AttributeValues.Single().SelectedValue = mapValue;

        FilterPanel.Of(vm).Add("Style").Check("Rush");

        Assert.Equal(expected, vm.FilteredMaps.Contains(map));
    }

    [Fact]
    public void AttributeFilterWithoutIncludeUnset_DropsAMapWhoseValueIsUnset()
    {
        MapsPageViewModel vm = ValuesAttributeVm();
        Map map = AddMapWithValueRows(vm);

        FilterHandle filter = FilterPanel.Of(vm).Add("Style");
        filter.IncludeUnset = false;
        filter.Check("Rush");

        Assert.DoesNotContain(map, vm.FilteredMaps);
    }

    // A map that has a value row for the attribute, just with nothing set in it — case 2 below, so
    // IncludeUnset decides. This is what the a?.SelectedValue fix addressed: the mapped value now
    // reaches the filter as null rather than as "", so IncludeUnset is actually consulted.
    [Fact]
    public void IncludeUnset_ValuesAttribute_KeepsAMapWhoseValueRowIsUnset()
    {
        MapsPageViewModel vm = ValuesAttributeVm();
        Map map = AddMapWithValueRows(vm);

        FilterHandle filter = FilterPanel.Of(vm).Add("Style");
        filter.IncludeUnset = true;
        filter.Check("Rush");

        Assert.Contains(map, vm.FilteredMaps);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IncludeUnset_BoolAttribute_DecidesAMapWhoseValueRowIsUnset(bool includeUnset)
    {
        MapsPageViewModel vm = AttributeVm("Ramped", AttributeType.Bool);
        Map map = AddMapWithValueRows(vm);

        FilterHandle filter = FilterPanel.Of(vm).Add("Ramped");
        filter.IncludeUnset = includeUnset;
        filter.BoolValue = true;

        Assert.Equal(includeUnset, vm.FilteredMaps.Contains(map));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IncludeUnset_NumericAttribute_DecidesAMapWhoseValueRowIsUnset(bool includeUnset)
    {
        MapsPageViewModel vm = AttributeVm("Rush Distance", AttributeType.Numeric);
        Map map = AddMapWithValueRows(vm);

        FilterHandle filter = FilterPanel.Of(vm).Add("Rush Distance");
        filter.IncludeUnset = includeUnset;
        filter.Min = 10;

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
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        Assert.Empty(vm.FilteredMaps.Single().AttributeValues);

        FilterHandle filter = FilterPanel.Of(vm).Add("Style");
        filter.IncludeUnset = true;
        filter.Check("Rush");

        Assert.Empty(vm.FilteredMaps);
    }

    [Fact]
    public void AppliedBoolFilter_ExcludesAMapWithNoValueRow_EvenWithIncludeUnset()
    {
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = AttributeVm("Ramped", AttributeType.Bool);

        FilterHandle filter = FilterPanel.Of(vm).Add("Ramped");
        filter.IncludeUnset = true;
        filter.BoolValue = true;

        Assert.Empty(vm.FilteredMaps);
    }

    [Fact]
    public void AppliedNumericFilter_ExcludesAMapWithNoValueRow_EvenWithIncludeUnset()
    {
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = AttributeVm("Rush Distance", AttributeType.Numeric);

        FilterHandle filter = FilterPanel.Of(vm).Add("Rush Distance");
        filter.IncludeUnset = true;
        filter.Min = 10;

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
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);

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
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);

        vm.NameFilter = "Altitude";

        Assert.Empty(vm.FilteredMaps);
    }

    #region Filter bar

    // Behaviour of the filter bar and its "+" menu as a user sees it, pinned ahead of moving the Maps,
    // Builds and Data tabs onto one shared filter menu. Everything goes through FilterPanel, so these
    // don't depend on how the page holds its filters.

    [Fact]
    public void AttributeFilters_StartUnappliedAndAreOfferedInTheAddMenu()
    {
        _attributeRepo.InsertAttribute(new(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values }, 0);
        _attributeRepo.InsertAttribute(new(AttributeScope.Map) { Name = "Ramped", Type = AttributeType.Bool }, 1);
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        FilterPanel filters = FilterPanel.Of(vm);

        Assert.Empty(filters.AppliedTitles);
        Assert.Equal(["Ramped", "Style"], filters.AddableTitles.Order());
    }

    // Maps can be missing a value for any attribute, so every attribute filter offers "Include unset".
    [Theory]
    [InlineData(AttributeType.Values)]
    [InlineData(AttributeType.Bool)]
    [InlineData(AttributeType.Numeric)]
    [InlineData(AttributeType.Percent)]
    public void AttributeFilters_OfferIncludeUnset(AttributeType type)
    {
        MapsPageViewModel vm = AttributeVm("Attribute", type);

        Assert.True(FilterPanel.Of(vm).Offered("Attribute").AllowIncludeUnset);
    }

    [Fact]
    public void AddingAFilter_MovesItFromTheAddMenuToTheFilterBar()
    {
        MapsPageViewModel vm = ValuesAttributeVm();
        FilterPanel filters = FilterPanel.Of(vm);

        FilterHandle filter = filters.Add("Style");

        Assert.True(filter.IsApplied);
        Assert.Equal(["Style"], filters.AppliedTitles);
        Assert.Empty(filters.AddableTitles);
    }

    [Fact]
    public void RemovingAFilter_ReturnsItToTheAddMenuAndStopsItConstraining()
    {
        MapsPageViewModel vm = ValuesAttributeVm();
        Map map = AddMapWithValue(vm, "Style", v => v.SelectedValue = "Macro");
        FilterPanel filters = FilterPanel.Of(vm);
        FilterHandle filter = filters.Add("Style").Check("Rush");
        Assert.DoesNotContain(map, vm.FilteredMaps);

        filters.Applied("Style").Remove();

        Assert.Contains(map, vm.FilteredMaps);
        Assert.Empty(filters.AppliedTitles);
        Assert.Equal(["Style"], filters.AddableTitles);
        Assert.False(filter.IsApplied);
    }

    // A removed filter must not come back with the criteria it had, or re-adding it would silently
    // re-apply a constraint the user can't see until they open it.
    [Fact]
    public void RemovingAFilter_ClearsItsCriteria()
    {
        MapsPageViewModel vm = AttributeVm("Rush Distance", AttributeType.Numeric);
        FilterPanel filters = FilterPanel.Of(vm);
        FilterHandle filter = filters.Add("Rush Distance");
        filter.Min = 10;
        filter.Max = 20;
        filter.IncludeUnset = true;

        filter.Remove();
        FilterHandle readded = filters.Add("Rush Distance");

        Assert.Null(readded.Min);
        Assert.Null(readded.Max);
        Assert.False(readded.IncludeUnset);
    }

    [Fact]
    public void RemovingAValuesFilter_UnchecksItsOptions()
    {
        MapsPageViewModel vm = ValuesAttributeVm();
        FilterPanel filters = FilterPanel.Of(vm);
        filters.Add("Style").Check("Rush", "Macro").Remove();

        Assert.Empty(filters.Add("Style").CheckedLabels);
    }

    // Adding a filter only shows its controls; until criteria are entered it shouldn't hide maps that
    // have a value for it.
    [Theory]
    [InlineData(AttributeType.Values)]
    [InlineData(AttributeType.Bool)]
    [InlineData(AttributeType.Numeric)]
    [InlineData(AttributeType.Percent)]
    public void AddedFilterWithNoCriteria_KeepsMapsThatHaveAValue(AttributeType type)
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Attribute", Type = type };
        _attributeRepo.InsertAttribute(attribute, 0);
        _attributeRepo.InsertValueOption(attribute.Id, "Rush");
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        Map map = AddMapWithValue(vm, "Attribute", v =>
        {
            v.SelectedValue = "Rush";
            v.BoolValue = false;
            v.NumericValue = 5;
            v.PercentValue = 50;
        });

        FilterPanel.Of(vm).Add("Attribute");

        Assert.Contains(map, vm.FilteredMaps);
    }

    [Fact]
    public void ValuesFilter_WithSeveralCheckedOptions_KeepsMapsHoldingAnyOfThem()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        foreach (string option in new[] { "Rush", "Macro", "Cheese" })
            _attributeRepo.InsertValueOption(attribute.Id, option);
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        Map rush = AddMapWithValue(vm, "Style", v => v.SelectedValue = "Rush");
        Map macro = AddMapWithValue(vm, "Style", v => v.SelectedValue = "Macro");
        Map cheese = AddMapWithValue(vm, "Style", v => v.SelectedValue = "Cheese");

        FilterPanel.Of(vm).Add("Style").Check("Rush", "Macro");

        Assert.Equal([rush, macro], vm.FilteredMaps.Order(MapOrder(rush, macro, cheese)));
    }

    [Fact]
    public void UncheckingTheLastOption_StopsTheValuesFilterConstraining()
    {
        MapsPageViewModel vm = ValuesAttributeVm();
        Map map = AddMapWithValue(vm, "Style", v => v.SelectedValue = "Macro");
        FilterHandle filter = FilterPanel.Of(vm).Add("Style").Check("Rush");
        Assert.DoesNotContain(map, vm.FilteredMaps);

        filter.Uncheck("Rush");

        Assert.Contains(map, vm.FilteredMaps);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public void BoolFilter_KeepsOnlyMapsWithTheChosenValue(bool mapValue, bool filterValue, bool expected)
    {
        MapsPageViewModel vm = AttributeVm("Ramped", AttributeType.Bool);
        Map map = AddMapWithValue(vm, "Ramped", v => v.BoolValue = mapValue);

        FilterPanel.Of(vm).Add("Ramped").BoolValue = filterValue;

        Assert.Equal(expected, vm.FilteredMaps.Contains(map));
    }

    // The three-state checkbox's indeterminate state is "either", not "unset only".
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BoolFilter_BackToIndeterminate_KeepsMapsWithEitherValue(bool mapValue)
    {
        MapsPageViewModel vm = AttributeVm("Ramped", AttributeType.Bool);
        Map map = AddMapWithValue(vm, "Ramped", v => v.BoolValue = mapValue);
        FilterHandle filter = FilterPanel.Of(vm).Add("Ramped");
        filter.BoolValue = !mapValue;

        filter.BoolValue = null;

        Assert.Contains(map, vm.FilteredMaps);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(15, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void NumericFilter_RangeIsInclusiveOnBothEnds(int mapValue, bool expected)
    {
        MapsPageViewModel vm = AttributeVm("Rush Distance", AttributeType.Numeric);
        Map map = AddMapWithValue(vm, "Rush Distance", v => v.NumericValue = mapValue);

        FilterHandle filter = FilterPanel.Of(vm).Add("Rush Distance");
        filter.Min = 10;
        filter.Max = 20;

        Assert.Equal(expected, vm.FilteredMaps.Contains(map));
    }

    [Theory]
    [InlineData(10, null, 9, false)]
    [InlineData(10, null, 1000, true)]
    [InlineData(null, 20, 21, false)]
    [InlineData(null, 20, -1000, true)]
    public void NumericFilter_WithOnlyOneBound_IsOpenOnTheOtherSide(int? min, int? max, int mapValue, bool expected)
    {
        MapsPageViewModel vm = AttributeVm("Rush Distance", AttributeType.Numeric);
        Map map = AddMapWithValue(vm, "Rush Distance", v => v.NumericValue = mapValue);

        FilterHandle filter = FilterPanel.Of(vm).Add("Rush Distance");
        filter.Min = min;
        filter.Max = max;

        Assert.Equal(expected, vm.FilteredMaps.Contains(map));
    }

    // Percent attributes store their value separately from Numeric ones; the filter has to read that one.
    [Theory]
    [InlineData(40, false)]
    [InlineData(60, true)]
    public void PercentFilter_FiltersOnThePercentValue(int mapValue, bool expected)
    {
        MapsPageViewModel vm = AttributeVm("Win Rate", AttributeType.Percent);
        Map map = AddMapWithValue(vm, "Win Rate", v => v.PercentValue = mapValue);

        FilterPanel.Of(vm).Add("Win Rate").Min = 50;

        Assert.Equal(expected, vm.FilteredMaps.Contains(map));
    }

    [Fact]
    public void NameFilterAndAttributeFilter_MustBothMatch()
    {
        MapsPageViewModel vm = ValuesAttributeVm();
        Map altitudeRush = AddMapWithValue(vm, "Style", v => v.SelectedValue = "Rush", name: "Altitude LE");
        Map altitudeMacro = AddMapWithValue(vm, "Style", v => v.SelectedValue = "Macro", name: "Altitude II");
        Map deathauraRush = AddMapWithValue(vm, "Style", v => v.SelectedValue = "Rush", name: "Deathaura LE");

        vm.NameFilter = "Altitude";
        FilterPanel.Of(vm).Add("Style").Check("Rush");

        Assert.Equal([altitudeRush], vm.FilteredMaps);
        Assert.DoesNotContain(altitudeMacro, vm.FilteredMaps);
        Assert.DoesNotContain(deathauraRush, vm.FilteredMaps);
    }

    [Fact]
    public void TwoAttributeFilters_MustBothMatch()
    {
        AttributeDefinition style = new(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(style, 0);
        _attributeRepo.InsertValueOption(style.Id, "Rush");
        _attributeRepo.InsertValueOption(style.Id, "Macro");
        _attributeRepo.InsertAttribute(new(AttributeScope.Map) { Name = "Ramped", Type = AttributeType.Bool }, 1);
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        Map both = AddMapWithValues(vm, ("Style", v => v.SelectedValue = "Rush"), ("Ramped", v => v.BoolValue = true));
        Map styleOnly = AddMapWithValues(vm, ("Style", v => v.SelectedValue = "Rush"), ("Ramped", v => v.BoolValue = false));
        Map rampedOnly = AddMapWithValues(vm, ("Style", v => v.SelectedValue = "Macro"), ("Ramped", v => v.BoolValue = true));

        FilterPanel filters = FilterPanel.Of(vm);
        filters.Add("Style").Check("Rush");
        filters.Add("Ramped").BoolValue = true;

        Assert.Equal([both], vm.FilteredMaps);
        Assert.DoesNotContain(styleOnly, vm.FilteredMaps);
        Assert.DoesNotContain(rampedOnly, vm.FilteredMaps);
    }

    // Editing a value on the selected map re-runs the filters, so a map edited out of the active filter
    // leaves the list straight away instead of lingering until the filter is next touched.
    [Fact]
    public void EditingTheSelectedMapsValue_ReappliesTheFilters()
    {
        MapsPageViewModel vm = ValuesAttributeVm();
        Map map = AddMapWithValue(vm, "Style", v => v.SelectedValue = "Rush");
        FilterPanel.Of(vm).Add("Style").Check("Rush");
        Assert.Contains(map, vm.FilteredMaps);

        map.AttributeValues.Single().SelectedValue = "Macro";

        Assert.DoesNotContain(map, vm.FilteredMaps);
    }

    [Fact]
    public void RenamingTheSelectedMap_ReappliesTheNameFilter()
    {
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        Map map = AddMapWithValueRows(vm);
        map.Name = "Altitude LE";
        vm.NameFilter = "Altitude";
        Assert.Contains(map, vm.FilteredMaps);

        map.Name = "Deathaura LE";

        Assert.DoesNotContain(map, vm.FilteredMaps);
    }

    // A filter slot created after the page was built has to be wired up the same as the original ones,
    // or checking an option on it would do nothing.
    [Fact]
    public void AttributeAddedElsewhere_IsOfferedAndFilters()
    {
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        FilterPanel filters = FilterPanel.Of(vm);

        _attributeRepo.InsertAttribute(new(AttributeScope.Map) { Name = "Ramped", Type = AttributeType.Bool, IsMandatory = true }, 0);
        Assert.Equal(["Ramped"], filters.AddableTitles);
        Assert.Single(vm.FilteredMaps);

        // The backfilled mandatory row is unset, so an applied filter without Include unset drops it.
        filters.Add("Ramped").BoolValue = true;

        Assert.Empty(vm.FilteredMaps);
    }

    [Fact]
    public void AttributeDeletedElsewhere_WhileApplied_LeavesTheFilterBarAndStopsConstraining()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        _attributeRepo.InsertValueOption(attribute.Id, "Rush");
        _attributeRepo.InsertValueOption(attribute.Id, "Macro");
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        Map map = AddMapWithValue(vm, "Style", v => v.SelectedValue = "Macro");
        FilterPanel filters = FilterPanel.Of(vm);
        filters.Add("Style").Check("Rush");
        Assert.DoesNotContain(map, vm.FilteredMaps);

        _attributeRepo.DeleteAttribute(attribute.Id);

        Assert.Empty(filters.AppliedTitles);
        Assert.Empty(filters.AddableTitles);
        Assert.Contains(map, vm.FilteredMaps);
    }

    [Fact]
    public void AttributeRenamedElsewhere_WhileApplied_RenamesItInTheFilterBarKeepingItsCriteria()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        _attributeRepo.InsertValueOption(attribute.Id, "Rush");
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        FilterPanel filters = FilterPanel.Of(vm);
        filters.Add("Style").Check("Rush");

        AttributeDefinition editedElsewhere = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Map));
        editedElsewhere.Name = "Play Style";
        _attributeRepo.UpdateAttribute(editedElsewhere);

        Assert.Equal(["Play Style"], filters.AppliedTitles);
        Assert.Equal(["Rush"], filters.Applied("Play Style").CheckedLabels);
    }

    // The slot is rebuilt as the new kind, but whether it was showing survives — and the rebuilt slot
    // still re-filters when its criteria change.
    [Fact]
    public void AttributeTypeChangedElsewhere_WhileApplied_StaysAppliedAsTheNewKindAndStillFilters()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Contested", Type = AttributeType.Numeric };
        _attributeRepo.InsertAttribute(attribute, 0);
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        Map map = AddMapWithValueRows(vm);
        FilterPanel filters = FilterPanel.Of(vm);
        filters.Add("Contested");

        AttributeDefinition editedElsewhere = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Map));
        editedElsewhere.Type = AttributeType.Bool;
        _attributeRepo.UpdateAttribute(editedElsewhere);

        Assert.Equal(["Contested"], filters.AppliedTitles);
        FilterHandle filter = filters.Applied("Contested");
        Assert.True(filter.IsBoolFilter);

        map.AttributeValues.Single().BoolValue = true;
        Assert.Contains(map, vm.FilteredMaps);
        filter.BoolValue = false;
        Assert.DoesNotContain(map, vm.FilteredMaps);
    }

    [Fact]
    public void AttributeTypeChangedElsewhere_WhileNotApplied_StaysInTheAddMenu()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Contested", Type = AttributeType.Numeric };
        _attributeRepo.InsertAttribute(attribute, 0);
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        FilterPanel filters = FilterPanel.Of(vm);

        AttributeDefinition editedElsewhere = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Map));
        editedElsewhere.Type = AttributeType.Bool;
        _attributeRepo.UpdateAttribute(editedElsewhere);

        Assert.Empty(filters.AppliedTitles);
        Assert.Equal(["Contested"], filters.AddableTitles);
    }

    #endregion

    private MapsPageViewModel ValuesAttributeVm()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        _attributeRepo.InsertValueOption(attribute.Id, "Rush");
        _attributeRepo.InsertValueOption(attribute.Id, "Macro");
        return new MapsPageViewModel(_mapRepo, _attributeRepo, _gameDataRepo);
    }

    private MapsPageViewModel AttributeVm(string name, AttributeType type)
    {
        _attributeRepo.InsertAttribute(new(AttributeScope.Map) { Name = name, Type = type }, 0);
        return new MapsPageViewModel(_mapRepo, _attributeRepo, _gameDataRepo);
    }

    // Adding through the page is what gives a map a value row per attribute; maps loaded straight from
    // the DB only carry rows for values that were actually saved.
    private static Map AddMapWithValueRows(MapsPageViewModel vm)
    {
        vm.AddMapCommand.Execute(null);
        return vm.SelectedMap!;
    }

    private static Map AddMapWithValue(MapsPageViewModel vm, string attribute, Action<AttributeValue> setValue, string? name = null) =>
        AddMapWithValues(vm, [(attribute, setValue)], name);

    private static Map AddMapWithValues(MapsPageViewModel vm, params (string Attribute, Action<AttributeValue> SetValue)[] values) =>
        AddMapWithValues(vm, values, null);

    // Values are set while the map is still selected, the way the value editor would set them. Renamed
    // straight away because map names are unique and every added map starts as "New Map".
    private static Map AddMapWithValues(MapsPageViewModel vm, (string Attribute, Action<AttributeValue> SetValue)[] values, string? name)
    {
        Map map = AddMapWithValueRows(vm);
        map.Name = name ?? $"Map {Guid.NewGuid():N}";
        foreach ((string attribute, Action<AttributeValue> setValue) in values)
            setValue(map.AttributeValues.Single(v => v.Definition.Name == attribute));
        return map;
    }

    private static Comparer<Map> MapOrder(params Map[] order) =>
        Comparer<Map>.Create((a, b) => Array.IndexOf(order, a).CompareTo(Array.IndexOf(order, b)));

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
