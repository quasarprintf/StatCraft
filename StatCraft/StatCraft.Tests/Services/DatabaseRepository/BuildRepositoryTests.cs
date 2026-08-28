using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.Tests;

public class BuildRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly BuildRepository _repository;

    public BuildRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");
        _repository = new BuildRepository(_dbPath);
        _repository.Initialize();
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
    public void InsertAttribute_DefaultValueRoundTripsForEachType(AttributeType type)
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

        _repository.InsertAttribute(attr, node.Id, 0);

        BuildNode loadedNode = Assert.Single(_repository.GetBuildsForPlayerRace(Race.Protoss));
        AttributeValue loadedAttr = Assert.Single(loadedNode.Details);

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
    public void InsertValueOption_ThenGetBuildsForPlayerRace_IncludesOption()
    {
        BuildNode node = new BuildNode { Name = "Build", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);

        AttributeValue attr = new(new AttributeDefinition(AttributeScope.BuildDetail) { Name = "Opening", Type = AttributeType.Values });
        _repository.InsertAttribute(attr, node.Id, 0);
        _repository.InsertValueOption(attr.Definition.Id, "Zealot");

        BuildNode loadedNode = Assert.Single(_repository.GetBuildsForPlayerRace(Race.Protoss));
        AttributeValue loadedAttr = Assert.Single(loadedNode.Details);
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
    public void UpdateAttribute_RaisesBuildsChanged()
    {
        BuildNode node = new BuildNode { Name = "Build", PlayerRace = Race.Protoss, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);
        AttributeValue attr = new(new AttributeDefinition(AttributeScope.BuildDetail) { Name = "Supply", Type = AttributeType.Numeric });
        _repository.InsertAttribute(attr, node.Id, 0);

        int raisedCount = 0;
        _repository.BuildsChanged += () => raisedCount++;

        attr.NumericValue = 20;
        _repository.UpdateAttribute(attr);

        Assert.Equal(1, raisedCount);
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
