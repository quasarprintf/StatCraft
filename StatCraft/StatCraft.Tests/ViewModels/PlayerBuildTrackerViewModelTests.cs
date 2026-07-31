using System.Collections.ObjectModel;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;
using StatCraft.ViewModels;

namespace StatCraft.Tests;

public class PlayerBuildTrackerViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly GameDataRepository _gameDataRepository;
    private readonly BuildRepository _buildRepository;
    private readonly AccountRepository _accountRepository;
    private readonly int _sc2ProfileId;

    public PlayerBuildTrackerViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");

        _accountRepository = new AccountRepository(_dbPath);
        _accountRepository.Initialize();
        _buildRepository = new BuildRepository(_dbPath);
        _buildRepository.Initialize();
        _gameDataRepository = new GameDataRepository(_dbPath);
        _gameDataRepository.Initialize();

        BattleNetAccount account = new()
        {
            BattleTag = "Player#1234",
            AccountSub = "sub-1",
            EncryptedAccessToken = [1],
            TokenExpiresAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        _accountRepository.InsertAccount(account);
        Sc2Profile profile = new() { BattleNetAccountId = account.Id, RegionId = "1", RealmId = "1", ProfileId = 111, Name = "Player" };
        _accountRepository.UpsertProfile(profile);
        _sc2ProfileId = profile.Id;
    }

    [Fact]
    public void RefreshAttributeEditors_AfterTemplateDefaultChanges_DoesNotChangeAlreadyLockedInValue()
    {
        BuildNode build = new() { Name = "4 Gate", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(build, null, 0);
        BuildAttribute attr = new() { Name = "Supply", Type = AttributeType.Numeric, NumericValue = 10 };
        _buildRepository.InsertAttribute(attr, build.Id, 0);

        GameData game = CreateGame();
        _gameDataRepository.InsertGame(game, _sc2ProfileId);

        ObservableCollection<BuildNode> tree = new(_buildRepository.GetBuildsForPlayerRace(Race.Z));
        PlayerBuildTrackerViewModel tracker = new(game.ReplayData.Player, _gameDataRepository, tree);

        // Select the build — this locks in the attribute's current default (10) as this player's own value.
        BuildNode loadedBuild = tree.Single();
        tracker.BuildSlots[0].SelectedBuildNode = loadedBuild;
        Assert.Equal(10, Assert.Single(tracker.AttributeEditors).NumericValue);

        // Simulate editing the attribute's default on the Builds tab, then DataPageViewModel's
        // RefreshBuildTreeCache pattern of mutating the same tree collection in place.
        attr.NumericValue = 20;
        _buildRepository.UpdateAttribute(attr);
        tree.Clear();
        foreach (BuildNode node in _buildRepository.GetBuildsForPlayerRace(Race.Z))
            tree.Add(node);

        tracker.RefreshAttributeEditors();

        Assert.Equal(10, Assert.Single(tracker.AttributeEditors).NumericValue);
    }

    [Fact]
    public void SelectingABuild_ForTheFirstTime_UsesTemplatesCurrentDefault()
    {
        BuildNode build = new() { Name = "4 Gate", PlayerRace = Race.Z };
        _buildRepository.InsertBuild(build, null, 0);
        BuildAttribute attr = new() { Name = "Supply", Type = AttributeType.Numeric, NumericValue = 10 };
        _buildRepository.InsertAttribute(attr, build.Id, 0);

        // Edit the default before anyone ever selects the build.
        attr.NumericValue = 20;
        _buildRepository.UpdateAttribute(attr);

        GameData game = CreateGame();
        _gameDataRepository.InsertGame(game, _sc2ProfileId);

        ObservableCollection<BuildNode> tree = new(_buildRepository.GetBuildsForPlayerRace(Race.Z));
        PlayerBuildTrackerViewModel tracker = new(game.ReplayData.Player, _gameDataRepository, tree);

        tracker.BuildSlots[0].SelectedBuildNode = tree.Single();

        Assert.Equal(20, Assert.Single(tracker.AttributeEditors).NumericValue);
    }

    private static GameData CreateGame()
    {
        ParsedReplayData replay = new()
        {
            MapName = "Map",
            GameLengthSeconds = 600,
            ReplayPath = Guid.NewGuid() + ".SC2Replay",
            ReplayTimestamp = DateTimeOffset.UtcNow,
            Win = 1m,
            Player = new GamePlayer { Name = "Me", Clan = "", Mmr = 3000, Race = 'Z', Random = false },
            Allies = [],
            Opponents = [new GamePlayer { Name = "Foe", Clan = "", Mmr = 3100, Race = 'T', Random = false }],
        };
        return new GameData { ReplayData = replay };
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
