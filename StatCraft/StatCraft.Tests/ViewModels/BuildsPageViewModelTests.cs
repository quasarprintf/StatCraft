using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;
using StatCraft.ViewModels.Windows;

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

        _vm = new BuildsPageViewModel(_buildRepo, _attributeRepo, _gameDataRepo)
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

    [Theory]
    [InlineData("gate", true)]
    [InlineData("GATE", true)]
    [InlineData(" 4 ", true)]
    [InlineData("Cannon", false)]
    public void NameFilter_IsCaseInsensitiveSubstringMatch(string filter, bool expected)
    {
        _vm.SelectedBuild!.Name = "4 Gate";

        _vm.NameFilter = filter;

        Assert.Equal(expected, _vm.SelectedBuild.MatchesFilter);
    }

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
        Assert.DoesNotContain(_vm.SelectedBuild!.AttributeValues, v => v.Definition.Id == attribute.Id);

        FilterHandle filter = FilterPanel.Of(_vm).Add("Cheese");
        filter.IncludeUnset = true;
        filter.BoolValue = true;

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
        Assert.Contains(_vm.SelectedBuild!.AttributeValues, v => v.Definition.Id == attribute.Id);

        FilterHandle filter = FilterPanel.Of(_vm).Add("Cheese");
        filter.IncludeUnset = includeUnset;
        filter.BoolValue = true;

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
        BuildsPageViewModel vm = new(_buildRepo, _attributeRepo, _gameDataRepo)
        {
            PlayerRace = Race.Protoss,
        };
        FilterHandle filter = FilterPanel.Of(vm).Offered("Style").Check("Rush");

        _attributeRepo.InsertValueOption(attribute.Id, "Macro");

        Assert.Equal(["Rush", "Macro"], filter.OptionLabels);
        Assert.Equal(["Rush"], filter.CheckedLabels);
    }

    #region Filter bar

    // Behaviour of the filter bar and its "+" menu as a user sees it, pinned ahead of moving the Maps,
    // Builds and Data tabs onto one shared filter menu. Everything goes through FilterPanel, so these
    // don't depend on how the page holds its filters.

    [Fact]
    public void AttributeFilters_StartUnappliedAndAreOfferedInTheAddMenuWithIncludeUnset()
    {
        InsertStyleAttribute();
        _attributeRepo.InsertAttribute(new(AttributeScope.Build) { Name = "Cheese", Type = AttributeType.Bool }, 1);
        FilterPanel filters = FilterPanel.Of(_vm);

        Assert.Empty(filters.AppliedTitles);
        Assert.Equal(["Cheese", "Style"], filters.AddableTitles.Order());
        Assert.True(filters.Offered("Style").AllowIncludeUnset);
        Assert.True(filters.Offered("Cheese").AllowIncludeUnset);
    }

    [Fact]
    public void AddingAFilter_MovesItFromTheAddMenuToTheFilterBar()
    {
        InsertStyleAttribute();
        FilterPanel filters = FilterPanel.Of(_vm);

        filters.Add("Style");

        Assert.Equal(["Style"], filters.AppliedTitles);
        Assert.Empty(filters.AddableTitles);
    }

    [Fact]
    public void RemovingAFilter_ReturnsItToTheAddMenuStopsItConstrainingAndClearsIt()
    {
        InsertStyleAttribute();
        BuildNode build = _vm.SelectedBuild!;
        SetValue(build, "Style", v => v.SelectedValue = "Macro");
        FilterPanel filters = FilterPanel.Of(_vm);
        filters.Add("Style").Check("Rush");
        Assert.False(build.MatchesFilter);

        filters.Applied("Style").Remove();

        Assert.True(build.MatchesFilter);
        Assert.Empty(filters.AppliedTitles);
        Assert.Equal(["Style"], filters.AddableTitles);
        Assert.Empty(filters.Add("Style").CheckedLabels);
    }

    [Theory]
    [InlineData("Rush", true)]
    [InlineData("Macro", false)]
    public void CheckedOption_KeepsOnlyBuildsHoldingThatValue(string buildValue, bool expected)
    {
        InsertStyleAttribute();
        SetValue(_vm.SelectedBuild!, "Style", v => v.SelectedValue = buildValue);

        FilterPanel.Of(_vm).Add("Style").Check("Rush");

        Assert.Equal(expected, _vm.SelectedBuild!.MatchesFilter);
    }

    [Fact]
    public void AddedFilterWithNoCriteria_KeepsBuildsThatHaveAValue()
    {
        InsertStyleAttribute();
        SetValue(_vm.SelectedBuild!, "Style", v => v.SelectedValue = "Macro");

        FilterPanel.Of(_vm).Add("Style");

        Assert.True(_vm.SelectedBuild!.MatchesFilter);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void NumericFilter_RangeIsInclusiveOnBothEnds(int buildValue, bool expected)
    {
        _attributeRepo.InsertAttribute(new(AttributeScope.Build) { Name = "Supply", Type = AttributeType.Numeric }, 0);
        SetValue(_vm.SelectedBuild!, "Supply", v => v.NumericValue = buildValue);

        FilterHandle filter = FilterPanel.Of(_vm).Add("Supply");
        filter.Min = 10;
        filter.Max = 20;

        Assert.Equal(expected, _vm.SelectedBuild!.MatchesFilter);
    }

    [Fact]
    public void NameFilterAndAttributeFilter_MustBothMatch()
    {
        InsertStyleAttribute();
        BuildNode gateRush = _vm.SelectedBuild!;
        gateRush.Name = "4 Gate";
        SetValue(gateRush, "Style", v => v.SelectedValue = "Rush");
        BuildNode gateMacro = AddRootBuild("3 Gate Robo");
        SetValue(gateMacro, "Style", v => v.SelectedValue = "Macro");
        BuildNode cannonRush = AddRootBuild("Cannon Rush");
        SetValue(cannonRush, "Style", v => v.SelectedValue = "Rush");

        _vm.NameFilter = "Gate";
        FilterPanel.Of(_vm).Add("Style").Check("Rush");

        Assert.True(gateRush.MatchesFilter);
        Assert.False(gateMacro.MatchesFilter);
        Assert.False(cannonRush.MatchesFilter);
    }

    // Unlike the flat map list, hiding a non-matching build would also hide any matching build nested
    // under it, so a match keeps its whole ancestor chain visible — but not its non-matching siblings.
    [Fact]
    public void MatchingChild_KeepsItsAncestorsVisible_ButNotItsNonMatchingSiblings()
    {
        InsertStyleAttribute();
        BuildNode root = _vm.SelectedBuild!;
        _vm.AddChildBuildCommand.Execute(root);
        BuildNode middle = _vm.SelectedBuild!;
        _vm.AddChildBuildCommand.Execute(middle);
        BuildNode rush = _vm.SelectedBuild!;
        SetValue(rush, "Style", v => v.SelectedValue = "Rush");
        _vm.AddChildBuildCommand.Execute(middle);
        BuildNode macro = _vm.SelectedBuild!;
        SetValue(macro, "Style", v => v.SelectedValue = "Macro");

        FilterPanel.Of(_vm).Add("Style").Check("Rush");

        Assert.True(root.MatchesFilter);
        Assert.True(middle.MatchesFilter);
        Assert.True(rush.MatchesFilter);
        Assert.False(macro.MatchesFilter);
    }

    [Fact]
    public void NameFilter_MatchingChild_KeepsItsParentVisible()
    {
        BuildNode root = _vm.SelectedBuild!;
        root.Name = "Gateway Expand";
        _vm.AddChildBuildCommand.Execute(root);
        _vm.SelectedBuild!.Name = "Blink";

        _vm.NameFilter = "Blink";

        Assert.True(root.MatchesFilter);
        Assert.True(_vm.SelectedBuild.MatchesFilter);
    }

    // Each player race's tree is loaded the first time it's shown, and the filters already applied have
    // to be run over it then too — not only over the race that was showing when they were set.
    [Fact]
    public void SwitchingToAnUnloadedPlayerRace_AppliesTheActiveFiltersToItsBuilds()
    {
        _buildRepo.InsertBuild(new BuildNode { Name = "Proxy Rax", PlayerRace = Race.Terran, Matchups = Matchups.VsP }, null, 0);
        _buildRepo.InsertBuild(new BuildNode { Name = "Mech", PlayerRace = Race.Terran, Matchups = Matchups.VsP }, null, 1);

        _vm.NameFilter = "rax";
        _vm.SelectPlayerRaceCommand.Execute(Race.Terran);

        Assert.True(_vm.Builds.Single(b => b.Name == "Proxy Rax").MatchesFilter);
        Assert.False(_vm.Builds.Single(b => b.Name == "Mech").MatchesFilter);
    }

    [Fact]
    public void EditingTheSelectedBuildsValue_ReappliesTheFilters()
    {
        InsertStyleAttribute();
        BuildNode build = _vm.SelectedBuild!;
        SetValue(build, "Style", v => v.SelectedValue = "Rush");
        FilterPanel.Of(_vm).Add("Style").Check("Rush");
        Assert.True(build.MatchesFilter);

        build.GetAttributeByDefinitionId(_vm.AllAttributes.Single().Id)!.SelectedValue = "Macro";

        Assert.False(build.MatchesFilter);
    }

    [Fact]
    public void RenamingTheSelectedBuild_ReappliesTheNameFilter()
    {
        BuildNode build = _vm.SelectedBuild!;
        build.Name = "4 Gate";
        _vm.NameFilter = "Gate";
        Assert.True(build.MatchesFilter);

        build.Name = "Cannon Rush";

        Assert.False(build.MatchesFilter);
    }

    [Fact]
    public void AttributeDeletedElsewhere_WhileApplied_LeavesTheFilterBarAndStopsConstraining()
    {
        AttributeDefinition attribute = InsertStyleAttribute();
        BuildNode build = _vm.SelectedBuild!;
        SetValue(build, "Style", v => v.SelectedValue = "Macro");
        FilterPanel filters = FilterPanel.Of(_vm);
        filters.Add("Style").Check("Rush");
        Assert.False(build.MatchesFilter);

        _attributeRepo.DeleteAttribute(attribute.Id);

        Assert.Empty(filters.AppliedTitles);
        Assert.Empty(filters.AddableTitles);
        Assert.True(build.MatchesFilter);
    }

    [Fact]
    public void AttributeRenamedElsewhere_WhileApplied_RenamesItInTheFilterBarKeepingItsCriteria()
    {
        InsertStyleAttribute();
        FilterPanel filters = FilterPanel.Of(_vm);
        filters.Add("Style").Check("Rush");

        AttributeDefinition editedElsewhere = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Build));
        editedElsewhere.Name = "Play Style";
        _attributeRepo.UpdateAttribute(editedElsewhere);

        Assert.Equal(["Play Style"], filters.AppliedTitles);
        Assert.Equal(["Rush"], filters.Applied("Play Style").CheckedLabels);
    }

    [Fact]
    public void AttributeTypeChangedElsewhere_WhileApplied_StaysAppliedAsTheNewKindAndStillFilters()
    {
        _attributeRepo.InsertAttribute(new(AttributeScope.Build) { Name = "Contested", Type = AttributeType.Numeric }, 0);
        BuildNode build = _vm.SelectedBuild!;
        SetValue(build, "Contested", _ => { });
        FilterPanel filters = FilterPanel.Of(_vm);
        filters.Add("Contested");

        AttributeDefinition editedElsewhere = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Build));
        editedElsewhere.Type = AttributeType.Bool;
        _attributeRepo.UpdateAttribute(editedElsewhere);

        Assert.Equal(["Contested"], filters.AppliedTitles);
        FilterHandle filter = filters.Applied("Contested");
        Assert.True(filter.IsBoolFilter);

        SetValue(build, "Contested", v => v.BoolValue = true);
        Assert.True(build.MatchesFilter);
        filter.BoolValue = false;
        Assert.False(build.MatchesFilter);
    }

    [Fact]
    public void AttributeAddedElsewhere_IsOfferedAndFilters()
    {
        FilterPanel filters = FilterPanel.Of(_vm);

        _attributeRepo.InsertAttribute(new(AttributeScope.Build) { Name = "Cheese", Type = AttributeType.Bool, IsMandatory = true }, 0);
        Assert.Equal(["Cheese"], filters.AddableTitles);
        Assert.True(_vm.SelectedBuild!.MatchesFilter);

        // The backfilled mandatory row is unset, so an applied filter without Include unset drops it.
        filters.Add("Cheese").BoolValue = true;

        Assert.False(_vm.SelectedBuild.MatchesFilter);
    }

    #endregion

    private AttributeDefinition InsertStyleAttribute()
    {
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        _attributeRepo.InsertValueOption(attribute.Id, "Rush");
        _attributeRepo.InsertValueOption(attribute.Id, "Macro");
        return attribute;
    }

    private BuildNode AddRootBuild(string name)
    {
        _vm.AddBuildCommand.Execute(null);
        _vm.SelectedBuild!.Name = name;
        return _vm.SelectedBuild;
    }

    // Selects the build and edits its value the way the attribute editor would: adding the attribute to
    // the build first if it doesn't have a row for it yet.
    private void SetValue(BuildNode build, string attributeName, Action<AttributeValue> setValue)
    {
        _vm.SelectedBuild = build;
        AttributeDefinition definition = _vm.AllAttributes.Single(a => a.Name == attributeName);
        if (build.GetAttributeByDefinitionId(definition.Id) == null)
            _vm.AttributeValuesSelect.AddAttributeCommand.Execute(definition);
        setValue(build.GetAttributeByDefinitionId(definition.Id)!);
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
