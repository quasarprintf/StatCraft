using Microsoft.Data.Sqlite;
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
    public void InsertBuild_ThenGetBuildsForPlayerRace_ReturnsRootBuild()
    {
        BuildNode node = new BuildNode { Name = "4 Gate", PlayerRace = Race.P, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);

        BuildNode build = Assert.Single(_repository.GetBuildsForPlayerRace(Race.P));
        Assert.Equal("4 Gate", build.Name);
    }

    [Fact]
    public void InsertBuild_ChildBuild_NestsUnderParent()
    {
        BuildNode parent = new BuildNode { Name = "Parent", PlayerRace = Race.T, Matchups = Matchups.VsT };
        _repository.InsertBuild(parent, null, 0);

        BuildNode child = new BuildNode { Name = "Child", PlayerRace = Race.T, Matchups = Matchups.VsT };
        _repository.InsertBuild(child, parent.Id, 0);

        BuildNode loadedParent = Assert.Single(_repository.GetBuildsForPlayerRace(Race.T));
        BuildNode loadedChild = Assert.Single(loadedParent.Children);
        Assert.Equal("Child", loadedChild.Name);
    }

    [Fact]
    public void DeleteBuild_RemovesItFromPlayerRace()
    {
        BuildNode node = new BuildNode { Name = "To Delete", PlayerRace = Race.Z, Matchups = Matchups.VsZ };
        _repository.InsertBuild(node, null, 0);

        _repository.DeleteBuild(node.Id);

        Assert.Empty(_repository.GetBuildsForPlayerRace(Race.Z));
    }

    [Fact]
    public void GetBuildsForMatchup_FiltersByOpponentRaceFlag()
    {
        BuildNode node = new BuildNode { Name = "Only vs Z", PlayerRace = Race.T, Matchups = Matchups.VsZ };
        _repository.InsertBuild(node, null, 0);

        BuildNode matched = Assert.Single(_repository.GetBuildsForMatchup(Race.T, Race.Z));
        Assert.Equal("Only vs Z", matched.Name);

        Assert.Empty(_repository.GetBuildsForMatchup(Race.T, Race.T));
    }

    [Fact]
    public void GetBuildsForPlayerRace_ReturnsBuildRegardlessOfMatchupFlags()
    {
        BuildNode node = new BuildNode { Name = "Only vs Z", PlayerRace = Race.T, Matchups = Matchups.VsZ };
        _repository.InsertBuild(node, null, 0);

        BuildNode loaded = Assert.Single(_repository.GetBuildsForPlayerRace(Race.T));
        Assert.Equal(Matchups.VsZ, loaded.Matchups);
    }

    [Theory]
    [InlineData(AttributeType.Numeric)]
    [InlineData(AttributeType.Bool)]
    [InlineData(AttributeType.Percent)]
    [InlineData(AttributeType.Values)]
    public void InsertAttribute_DefaultValueRoundTripsForEachType(AttributeType type)
    {
        BuildNode node = new BuildNode { Name = "Build", PlayerRace = Race.P, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);

        BuildAttribute attr = new BuildAttribute { Name = "Supply", Type = type };
        switch (type)
        {
            case AttributeType.Numeric: attr.NumericValue = 12; break;
            case AttributeType.Bool: attr.BoolValue = true; break;
            case AttributeType.Percent: attr.PercentValue = 55; break;
            case AttributeType.Values: attr.SelectedValue = "Zealot"; break;
        }

        _repository.InsertAttribute(attr, node.Id, 0);

        BuildNode loadedNode = Assert.Single(_repository.GetBuildsForPlayerRace(Race.P));
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
    public void InsertValueOption_ThenGetBuildsForPlayerRace_IncludesOption()
    {
        BuildNode node = new BuildNode { Name = "Build", PlayerRace = Race.P, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);

        BuildAttribute attr = new BuildAttribute { Name = "Opening", Type = AttributeType.Values };
        _repository.InsertAttribute(attr, node.Id, 0);
        _repository.InsertValueOption(attr.Id, "Zealot", 0);

        BuildNode loadedNode = Assert.Single(_repository.GetBuildsForPlayerRace(Race.P));
        BuildAttribute loadedAttr = Assert.Single(loadedNode.Attributes);
        Assert.Equal(["Zealot"], loadedAttr.ValueOptions);
    }

    [Fact]
    public void InsertBuild_RaisesBuildsChanged()
    {
        int raisedCount = 0;
        _repository.BuildsChanged += () => raisedCount++;

        _repository.InsertBuild(new BuildNode { Name = "Build", PlayerRace = Race.P, Matchups = Matchups.VsP }, null, 0);

        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public void DeleteBuild_RaisesBuildsChanged()
    {
        BuildNode node = new BuildNode { Name = "To Delete", PlayerRace = Race.P, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);

        int raisedCount = 0;
        _repository.BuildsChanged += () => raisedCount++;

        _repository.DeleteBuild(node.Id);

        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public void UpdateAttribute_RaisesBuildsChanged()
    {
        BuildNode node = new BuildNode { Name = "Build", PlayerRace = Race.P, Matchups = Matchups.VsP };
        _repository.InsertBuild(node, null, 0);
        BuildAttribute attr = new BuildAttribute { Name = "Supply", Type = AttributeType.Numeric };
        _repository.InsertAttribute(attr, node.Id, 0);

        int raisedCount = 0;
        _repository.BuildsChanged += () => raisedCount++;

        attr.NumericValue = 20;
        _repository.UpdateAttribute(attr);

        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public void Initialize_ExistingOldSchemaWithMatchupColumn_BackfillsPlayerRaceAndMatchups()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
            using (SqliteConnection conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using SqliteCommand createCmd = conn.CreateCommand();
                createCmd.CommandText = @"
                    CREATE TABLE BuildNodes (
                        Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                        Matchup     INTEGER NOT NULL,
                        ParentId    INTEGER REFERENCES BuildNodes(Id) ON DELETE CASCADE,
                        Name        TEXT    NOT NULL DEFAULT '',
                        Description TEXT    NOT NULL DEFAULT '',
                        SortOrder   INTEGER NOT NULL DEFAULT 0
                    );";
                createCmd.ExecuteNonQuery();

                using SqliteCommand insertCmd = conn.CreateCommand();
                // Old Matchup enum order was ZvZ=0,ZvT=1,ZvP=2,TvZ=3,... — 3 is TvZ (PlayerRace=T, vs Z).
                insertCmd.CommandText = "INSERT INTO BuildNodes (Matchup, Name) VALUES (3, 'Legacy Build')";
                insertCmd.ExecuteNonQuery();
            }

            BuildRepository repository = new BuildRepository(dbPath);
            repository.Initialize();

            BuildNode build = Assert.Single(repository.GetBuildsForPlayerRace(Race.T));
            Assert.Equal("Legacy Build", build.Name);
            Assert.Equal(Matchups.VsZ, build.Matchups);
        }
        finally
        {
            try
            {
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
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
