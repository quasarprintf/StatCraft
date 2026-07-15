using StatCraft.Models.GameData.Builds;
using StatCraft.Services.DatabaseRepository;
using StatCraft.ViewModels;

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
    public void InsertBuild_ThenGetBuildsForMatchup_ReturnsRootBuild()
    {
        BuildNode node = new BuildNode { Name = "4 Gate" };
        _repository.InsertBuild(node, Matchup.VsP, null, 0);

        BuildNode build = Assert.Single(_repository.GetBuildsForMatchup(Matchup.VsP));
        Assert.Equal("4 Gate", build.Name);
    }

    [Fact]
    public void InsertBuild_ChildBuild_NestsUnderParent()
    {
        BuildNode parent = new BuildNode { Name = "Parent" };
        _repository.InsertBuild(parent, Matchup.VsT, null, 0);

        BuildNode child = new BuildNode { Name = "Child" };
        _repository.InsertBuild(child, Matchup.VsT, parent.Id, 0);

        BuildNode loadedParent = Assert.Single(_repository.GetBuildsForMatchup(Matchup.VsT));
        BuildNode loadedChild = Assert.Single(loadedParent.Children);
        Assert.Equal("Child", loadedChild.Name);
    }

    [Fact]
    public void DeleteBuild_RemovesItFromMatchup()
    {
        BuildNode node = new BuildNode { Name = "To Delete" };
        _repository.InsertBuild(node, Matchup.VsZ, null, 0);

        _repository.DeleteBuild(node.Id);

        Assert.Empty(_repository.GetBuildsForMatchup(Matchup.VsZ));
    }

    [Theory]
    [InlineData(AttributeType.Numeric)]
    [InlineData(AttributeType.Bool)]
    [InlineData(AttributeType.Percent)]
    [InlineData(AttributeType.Values)]
    public void InsertAttribute_DefaultValueRoundTripsForEachType(AttributeType type)
    {
        BuildNode node = new BuildNode { Name = "Build" };
        _repository.InsertBuild(node, Matchup.VsP, null, 0);

        BuildAttribute attr = new BuildAttribute { Name = "Supply", Type = type };
        switch (type)
        {
            case AttributeType.Numeric: attr.NumericValue = 12; break;
            case AttributeType.Bool: attr.BoolValue = true; break;
            case AttributeType.Percent: attr.PercentValue = 55; break;
            case AttributeType.Values: attr.SelectedValue = "Zealot"; break;
        }

        _repository.InsertAttribute(attr, node.Id, 0);

        BuildNode loadedNode = Assert.Single(_repository.GetBuildsForMatchup(Matchup.VsP));
        BuildAttribute loadedAttr = Assert.Single(loadedNode.Attributes);

        switch (type)
        {
            case AttributeType.Numeric: Assert.Equal(12, loadedAttr.NumericValue); break;
            case AttributeType.Bool: Assert.True(loadedAttr.BoolValue); break;
            case AttributeType.Percent: Assert.Equal(55, loadedAttr.PercentValue); break;
            case AttributeType.Values: Assert.Equal("Zealot", loadedAttr.SelectedValue); break;
        }
    }

    [Fact]
    public void InsertValueOption_ThenGetBuildsForMatchup_IncludesOption()
    {
        BuildNode node = new BuildNode { Name = "Build" };
        _repository.InsertBuild(node, Matchup.VsP, null, 0);

        BuildAttribute attr = new BuildAttribute { Name = "Opening", Type = AttributeType.Values };
        _repository.InsertAttribute(attr, node.Id, 0);
        _repository.InsertValueOption(attr.Id, "Zealot", 0);

        BuildNode loadedNode = Assert.Single(_repository.GetBuildsForMatchup(Matchup.VsP));
        BuildAttribute loadedAttr = Assert.Single(loadedNode.Attributes);
        Assert.Equal(["Zealot"], loadedAttr.ValueOptions);
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
