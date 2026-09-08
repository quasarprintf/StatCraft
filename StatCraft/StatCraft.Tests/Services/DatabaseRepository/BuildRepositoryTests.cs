using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.Tests;

public class BuildRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly BuildRepository _repository;
    private readonly AttributeRepository _attributeRepo;

    public BuildRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");
        _repository = new BuildRepository(_dbPath);
        _repository.Initialize();
        _attributeRepo = new AttributeRepository(_dbPath);
        _attributeRepo.Initialize();
    }

    [Fact]
    public void Initialize_CalledTwice_DoesNotThrow()
    {
        _repository.Initialize();
    }

    [Fact]
    public void InsertBuild_ThenGetBuildsForPlayerRace_ReturnsRootBuild()
    {
        BuildNode node = new BuildNode { Name = "4 Gate", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);

        BuildNode build = Assert.Single(_repository.GetBuildsForPlayerRace(Race.Protoss));
        Assert.Equal("4 Gate", build.Name);
    }

    [Fact]
    public void InsertBuild_ChildBuild_NestsUnderParent()
    {
        BuildNode parent = new BuildNode { Name = "Parent", PlayerRace = Race.Terran, Matchups = Matchups.VsT };
        _repository.InsertBuild(parent, null, 0);

        BuildNode child = new BuildNode { Name = "Child", PlayerRace = Race.Terran, Matchups = Matchups.VsT };
        _repository.InsertBuild(child, parent.Id, 0);

        BuildNode loadedParent = Assert.Single(_repository.GetBuildsForPlayerRace(Race.Terran));
        BuildNode loadedChild = Assert.Single(loadedParent.Children);
        Assert.Equal("Child", loadedChild.Name);
    }

    [Fact]
    public void DeleteBuild_RemovesItFromPlayerRace()
    {
        BuildNode node = new BuildNode { Name = "To Delete", PlayerRace = Race.Zerg, Matchups = Matchups.VsZ };
        _repository.InsertBuild(node, null, 0);

        _repository.DeleteBuild(node.Id);

        Assert.Empty(_repository.GetBuildsForPlayerRace(Race.Zerg));
    }

    [Fact]
    public void GetBuildsForMatchup_FiltersByOpponentRaceFlag()
    {
        BuildNode node = new BuildNode { Name = "Only vs Zerg", PlayerRace = Race.Terran, Matchups = Matchups.VsZ };
        _repository.InsertBuild(node, null, 0);

        BuildNode matched = Assert.Single(_repository.GetBuildsForMatchup(Race.Terran, Matchups.VsZ));
        Assert.Equal("Only vs Zerg", matched.Name);

        Assert.Empty(_repository.GetBuildsForMatchup(Race.Terran, Matchups.VsT));
    }

    [Fact]
    public void GetBuildsForMatchup_CombinedFlags_MatchesAnyOfThem()
    {
        BuildNode node = new BuildNode { Name = "Only vs Zerg", PlayerRace = Race.Terran, Matchups = Matchups.VsZ };
        _repository.InsertBuild(node, null, 0);

        BuildNode matched = Assert.Single(_repository.GetBuildsForMatchup(Race.Terran, Matchups.VsZ | Matchups.VsP));
        Assert.Equal("Only vs Zerg", matched.Name);

        Assert.Empty(_repository.GetBuildsForMatchup(Race.Terran, Matchups.VsT | Matchups.VsP));
    }

    [Fact]
    public void GetBuildsForPlayerRace_ReturnsBuildRegardlessOfMatchupFlags()
    {
        BuildNode node = new BuildNode { Name = "Only vs Zerg", PlayerRace = Race.Terran, Matchups = Matchups.VsZ };
        _repository.InsertBuild(node, null, 0);

        BuildNode loaded = Assert.Single(_repository.GetBuildsForPlayerRace(Race.Terran));
        Assert.Equal(Matchups.VsZ, loaded.Matchups);
    }

    [Theory]
    [InlineData(AttributeType.Numeric)]
    [InlineData(AttributeType.Bool)]
    [InlineData(AttributeType.Percent)]
    [InlineData(AttributeType.Values)]
    public void InsertBuildDetailAttribute_DefaultValueRoundTripsForEachType(AttributeType type)
    {
        BuildNode node = new BuildNode { Name = "Build", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);

        AttributeValue attr = new(new AttributeDefinition(AttributeScope.BuildDetail) { Name = "Supply", Type = type });
        switch (type)
        {
            case AttributeType.Numeric: attr.NumericValue = 12; break;
            case AttributeType.Bool: attr.BoolValue = true; break;
            case AttributeType.Percent: attr.PercentValue = 55; break;
            case AttributeType.Values: attr.SelectedValue = "Zealot"; break;
        }

        _repository.InsertBuildDetailAttribute(attr, node.Id, 0);

        BuildNode loadedNode = Assert.Single(_repository.GetBuildsForPlayerRace(Race.Protoss));
        AttributeValue loadedAttr = Assert.Single(loadedNode.Details).DefaultValue;

        Assert.Equal(AttributeScope.BuildDetail, loadedAttr.Definition.Scope);

        switch (type)
        {
            case AttributeType.Numeric: Assert.Equal(12, loadedAttr.NumericValue); break;
            case AttributeType.Bool: Assert.True(loadedAttr.BoolValue); break;
            case AttributeType.Percent: Assert.Equal(55, loadedAttr.PercentValue); break;
            case AttributeType.Values: Assert.Equal("Zealot", loadedAttr.SelectedValue); break;
        }
    }

    [Fact]
    public void InsertBuildDetailValueOption_ThenGetBuildsForPlayerRace_IncludesOption()
    {
        BuildNode node = new BuildNode { Name = "Build", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);

        AttributeValue attr = new(new AttributeDefinition(AttributeScope.BuildDetail) { Name = "Opening", Type = AttributeType.Values });
        _repository.InsertBuildDetailAttribute(attr, node.Id, 0);
        _repository.InsertBuildDetailValueOption(attr.Definition.Id, "Zealot");

        BuildNode loadedNode = Assert.Single(_repository.GetBuildsForPlayerRace(Race.Protoss));
        AttributeValue loadedAttr = Assert.Single(loadedNode.Details).DefaultValue;
        Assert.Equal(["Zealot"], loadedAttr.Definition.ValueOptions);
    }

    [Fact]
    public void InsertBuild_RaisesBuildsChanged()
    {
        int raisedCount = 0;
        _repository.BuildsChanged += () => raisedCount++;

        _repository.InsertBuild(new BuildNode { Name = "Build", PlayerRace = Race.Protoss, Matchups = Matchups.VsP }, null, 0);

        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public void DeleteBuild_RaisesBuildsChanged()
    {
        BuildNode node = new BuildNode { Name = "To Delete", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);

        int raisedCount = 0;
        _repository.BuildsChanged += () => raisedCount++;

        _repository.DeleteBuild(node.Id);

        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public void UpdateBuildDetailAttribute_RaisesBuildsChanged()
    {
        BuildNode node = new BuildNode { Name = "Build", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);
        AttributeValue attr = new(new AttributeDefinition(AttributeScope.BuildDetail) { Name = "Supply", Type = AttributeType.Numeric });
        _repository.InsertBuildDetailAttribute(attr, node.Id, 0);

        int raisedCount = 0;
        _repository.BuildsChanged += () => raisedCount++;

        attr.NumericValue = 20;
        _repository.UpdateBuildDetailAttribute(attr);

        Assert.Equal(1, raisedCount);
    }

    #region ChangeBuildDetailSortOrder

    [Fact]
    public void ChangeBuildDetailSortOrder_MovingEarlier_ShiftsIntermediateDetailsRightByOne()
    {
        BuildNode node = InsertBuildWithDetails(out int[] ids, "A", "B", "C", "D");

        // Drag D (index 3) to the front (index 0).
        _repository.ChangeBuildDetailSortOrder(node.Id, sourceIndex: 3, targetIndex: 0);

        Assert.Equal(["D", "A", "B", "C"], LoadDetailNamesInOrder(node.Id));
    }

    [Fact]
    public void ChangeBuildDetailSortOrder_MovingLater_ShiftsIntermediateDetailsLeftByOne()
    {
        BuildNode node = InsertBuildWithDetails(out int[] ids, "A", "B", "C", "D");

        // Drag A (index 0) to the end (index 3).
        _repository.ChangeBuildDetailSortOrder(node.Id, sourceIndex: 0, targetIndex: 3);

        Assert.Equal(["B", "C", "D", "A"], LoadDetailNamesInOrder(node.Id));
    }

    [Fact]
    public void ChangeBuildDetailSortOrder_AdjacentSwapForward_SwapsOnlyThoseTwo()
    {
        BuildNode node = InsertBuildWithDetails(out int[] ids, "A", "B", "C", "D");

        // Drag B (index 1) to index 2 — should swap with C, leaving A and D untouched.
        _repository.ChangeBuildDetailSortOrder(node.Id, sourceIndex: 1, targetIndex: 2);

        Assert.Equal(["A", "C", "B", "D"], LoadDetailNamesInOrder(node.Id));
    }

    [Fact]
    public void ChangeBuildDetailSortOrder_AdjacentSwapBackward_SwapsOnlyThoseTwo()
    {
        BuildNode node = InsertBuildWithDetails(out int[] ids, "A", "B", "C", "D");

        // Drag C (index 2) to index 1 — should swap with B, leaving A and D untouched.
        _repository.ChangeBuildDetailSortOrder(node.Id, sourceIndex: 2, targetIndex: 1);

        Assert.Equal(["A", "C", "B", "D"], LoadDetailNamesInOrder(node.Id));
    }

    [Fact]
    public void ChangeBuildDetailSortOrder_OnlyAffectsTheTargetedBuildNode()
    {
        BuildNode nodeA = InsertBuildWithDetails(out int[] idsA, "A1", "A2", "A3");
        BuildNode nodeB = InsertBuildWithDetails(out int[] idsB, "B1", "B2", "B3");

        _repository.ChangeBuildDetailSortOrder(nodeA.Id, sourceIndex: 2, targetIndex: 0);

        Assert.Equal(["A3", "A1", "A2"], LoadDetailNamesInOrder(nodeA.Id));
        Assert.Equal(["B1", "B2", "B3"], LoadDetailNamesInOrder(nodeB.Id));
    }

    [Fact]
    public void ChangeBuildDetailSortOrder_RaisesBuildsChanged()
    {
        BuildNode node = InsertBuildWithDetails(out int[] ids, "A", "B");

        int raisedCount = 0;
        _repository.BuildsChanged += () => raisedCount++;

        _repository.ChangeBuildDetailSortOrder(node.Id, sourceIndex: 0, targetIndex: 1);

        Assert.Equal(1, raisedCount);
    }

    // KNOWN BUG, deliberately left failing: DeleteBuildDetailAttribute never recompacts the survivors'
    // SortOrder, so once any detail has been deleted from a build, SortOrder is no longer a dense
    // 0-based sequence — but ChangeBuildDetailSortOrder's sourceIndex/targetIndex come from the UI
    // list's plain 0-based position (ObservableCollection.IndexOf), which then no longer lines up with
    // the real SortOrder values. The drag silently targets the wrong row (or none at all) while the
    // UI's own in-memory Details.Move() still reorders optimistically, so the screen shows the drag
    // succeeding while the persisted order is actually left wrong and desynced from what's displayed.
    // Mirrors InsertValueOption_AfterAnEarlierOptionWasDeleted_StillSortsAfterSurvivors above, which
    // pinned the same class of bug already fixed for AttributeValueOptions but not yet here.
    [Fact]
    public void ChangeBuildDetailSortOrder_AfterAnEarlierDetailWasDeleted_MovesTheCorrectRow()
    {
        BuildNode node = InsertBuildWithDetails(out int[] ids, "A", "B", "C", "D");

        // Delete B (SortOrder 1), leaving a gap: A=0, C=2, D=3 — never recompacted.
        _repository.DeleteBuildDetailAttribute(node.Id, ids[1]);

        // UI list is now [A, C, D] at 0-based indices 0, 1, 2. Drag C (UI index 1) to before A (index 0).
        _repository.ChangeBuildDetailSortOrder(node.Id, sourceIndex: 1, targetIndex: 0);

        Assert.Equal(["C", "A", "D"], LoadDetailNamesInOrder(node.Id));
    }

    private BuildNode InsertBuildWithDetails(out int[] detailIds, params string[] names)
    {
        BuildNode node = new() { Name = "Build", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);

        detailIds = new int[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            AttributeValue attr = new(new AttributeDefinition(AttributeScope.BuildDetail) { Name = names[i], Type = AttributeType.Numeric });
            _repository.InsertBuildDetailAttribute(attr, node.Id, i);
            detailIds[i] = attr.Definition.Id;
        }
        return node;
    }

    private List<string> LoadDetailNamesInOrder(int buildNodeId) =>
        _repository.GetAllBuilds().Single(n => n.Id == buildNodeId).Details.Select(d => d.Name).ToList();

    #endregion

    // Static attributes (Scope.Build) are only loaded when definitions are passed in — omitting them
    // (as most tests above do) must leave StaticAttributes empty rather than throwing or guessing.
    [Fact]
    public void GetBuildsForPlayerRace_NoAttributesPassed_LeavesStaticAttributesEmpty()
    {
        BuildNode node = new() { Name = "Build", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Elo", IsMandatory = true };
        _attributeRepo.InsertAttribute(attribute, 0);

        BuildNode loaded = Assert.Single(_repository.GetBuildsForPlayerRace(Race.Protoss));

        Assert.Empty(loaded.AttributeValues);
    }

    [Fact]
    public void SaveStaticAttribute_ThenGetBuildsForPlayerRace_IncludesStoredValue()
    {
        BuildNode node = new() { Name = "Build", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Elo", Type = AttributeType.Numeric };
        _attributeRepo.InsertAttribute(attribute, 0);

        AttributeValue toSave = new(attribute) { NumericValue = 1500m };
        _repository.SaveStaticAttribute(node.Id, attribute.Id, toSave.Serialize());

        BuildNode loaded = Assert.Single(_repository.GetBuildsForPlayerRace(Race.Protoss, [attribute]));
        AttributeValue loadedValue = Assert.Single(loaded.AttributeValues);
        Assert.Equal(1500m, loadedValue.NumericValue);
    }

    [Fact]
    public void SaveStaticAttribute_Null_DeletesTheRow()
    {
        BuildNode node = new() { Name = "Build", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Elo", Type = AttributeType.Numeric };
        _attributeRepo.InsertAttribute(attribute, 0);
        _repository.SaveStaticAttribute(node.Id, attribute.Id, "1500");

        _repository.SaveStaticAttribute(node.Id, attribute.Id, null);

        BuildNode loaded = Assert.Single(_repository.GetBuildsForPlayerRace(Race.Protoss, [attribute]));
        Assert.Empty(loaded.AttributeValues);
    }

    [Fact]
    public void SaveStaticAttribute_CalledTwice_UpdatesRatherThanDuplicating()
    {
        BuildNode node = new() { Name = "Build", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Elo", Type = AttributeType.Numeric };
        _attributeRepo.InsertAttribute(attribute, 0);

        _repository.SaveStaticAttribute(node.Id, attribute.Id, "1500");
        _repository.SaveStaticAttribute(node.Id, attribute.Id, "1600");

        BuildNode loaded = Assert.Single(_repository.GetBuildsForPlayerRace(Race.Protoss, [attribute]));
        AttributeValue loadedValue = Assert.Single(loaded.AttributeValues);
        Assert.Equal(1600m, loadedValue.NumericValue);
    }

    [Fact]
    public void SaveStaticAttributes_SetValuesAcrossMultipleBuilds_PersistsEachOne()
    {
        BuildNode buildA = new() { Name = "A", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        BuildNode buildB = new() { Name = "B", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(buildA, null, 0);
        _repository.InsertBuild(buildB, null, 1);
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Elo", Type = AttributeType.Numeric };
        _attributeRepo.InsertAttribute(attribute, 0);
        buildA.AttributeValues.Add(new AttributeValue(attribute) { NumericValue = 5m });
        buildB.AttributeValues.Add(new AttributeValue(attribute) { NumericValue = 10m });

        _repository.SaveStaticAttributes([buildA, buildB], attribute.Id);

        List<BuildNode> reloaded = _repository.GetBuildsForPlayerRace(Race.Protoss, [attribute]);
        Assert.Equal(5m, reloaded.Single(b => b.Id == buildA.Id).AttributeValues.Single().NumericValue);
        Assert.Equal(10m, reloaded.Single(b => b.Id == buildB.Id).AttributeValues.Single().NumericValue);
    }

    [Fact]
    public void SaveStaticAttributes_UnsetValue_DeletesAnExistingRow()
    {
        BuildNode node = new() { Name = "Build", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Elo", Type = AttributeType.Numeric };
        _attributeRepo.InsertAttribute(attribute, 0);
        _repository.SaveStaticAttribute(node.Id, attribute.Id, "1500");

        BuildNode unsetNode = new() { Id = node.Id };
        unsetNode.AttributeValues.Add(new AttributeValue(attribute));
        _repository.SaveStaticAttributes([unsetNode], attribute.Id);

        BuildNode loaded = Assert.Single(_repository.GetBuildsForPlayerRace(Race.Protoss, [attribute]));
        Assert.Empty(loaded.AttributeValues);
    }

    [Fact]
    public void SaveStaticAttributes_EmptyList_DoesNotRaiseBuildsChanged()
    {
        int raisedCount = 0;
        _repository.BuildsChanged += () => raisedCount++;

        _repository.SaveStaticAttributes([], 1);

        Assert.Equal(0, raisedCount);
    }

    // Pins the point of batching this at all: one event for the whole call, not one per build.
    [Fact]
    public void SaveStaticAttributes_MultipleBuilds_RaisesBuildsChangedExactlyOnce()
    {
        BuildNode buildA = new() { Name = "A", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        BuildNode buildB = new() { Name = "B", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(buildA, null, 0);
        _repository.InsertBuild(buildB, null, 1);
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Elo", Type = AttributeType.Numeric };
        _attributeRepo.InsertAttribute(attribute, 0);
        buildA.AttributeValues.Add(new AttributeValue(attribute) { NumericValue = 1m });
        buildB.AttributeValues.Add(new AttributeValue(attribute) { NumericValue = 2m });

        int raisedCount = 0;
        _repository.BuildsChanged += () => raisedCount++;

        _repository.SaveStaticAttributes([buildA, buildB], attribute.Id);

        Assert.Equal(1, raisedCount);
    }

    // Mandatory static attributes apply to root builds only — a child may still opt in explicitly via
    // SaveStaticAttribute, but isn't backfilled just because its parent's tree root was.
    [Fact]
    public void GetBuildsForPlayerRace_MandatoryAttribute_BackfillsRootsOnlyNotChildren()
    {
        BuildNode root = new() { Name = "Root", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(root, null, 0);
        BuildNode child = new() { Name = "Child", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(child, root.Id, 0);
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Elo", Type = AttributeType.Numeric, IsMandatory = true };
        attribute.DefaultValue.NumericValue = 1200m;
        _attributeRepo.InsertAttribute(attribute, 0);

        BuildNode loadedRoot = Assert.Single(_repository.GetBuildsForPlayerRace(Race.Protoss, [attribute]));
        BuildNode loadedChild = Assert.Single(loadedRoot.Children);

        Assert.Equal(1200m, Assert.Single(loadedRoot.AttributeValues).NumericValue);
        Assert.Empty(loadedChild.AttributeValues);
    }

    // A mandatory attribute already stored for a root must not be backfilled a second time on top of it.
    [Fact]
    public void GetBuildsForPlayerRace_MandatoryAttribute_DoesNotOverrideAnExistingStoredValue()
    {
        BuildNode root = new() { Name = "Root", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(root, null, 0);
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Elo", Type = AttributeType.Numeric, IsMandatory = true };
        attribute.DefaultValue.NumericValue = 1200m;
        _attributeRepo.InsertAttribute(attribute, 0);
        _repository.SaveStaticAttribute(root.Id, attribute.Id, "1500");

        BuildNode loaded = Assert.Single(_repository.GetBuildsForPlayerRace(Race.Protoss, [attribute]));

        Assert.Equal(1500m, Assert.Single(loaded.AttributeValues).NumericValue);
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
