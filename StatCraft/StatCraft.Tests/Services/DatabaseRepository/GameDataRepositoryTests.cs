using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Services.DatabaseRepository;
using StatCraft.ViewModels;

namespace StatCraft.Tests;

public class GameDataRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly GameDataRepository _repository;
    private readonly BuildRepository _buildRepository;
    private readonly AccountRepository _accountRepository;
    private readonly int _sc2ProfileId;

    public GameDataRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");

        _accountRepository = new AccountRepository(_dbPath);
        _accountRepository.Initialize();
        _buildRepository = new BuildRepository(_dbPath);
        _buildRepository.Initialize();
        _repository = new GameDataRepository(_dbPath);
        _repository.Initialize();

        _sc2ProfileId = InsertProfile("sub-1", 111, "Player").Id;
    }

    [Fact]
    public void Initialize_CalledTwice_DoesNotThrow()
    {
        _repository.Initialize();
    }

    [Fact]
    public void InsertGame_ThenGetGamesForProfile_ReturnsGame()
    {
        GameData game = CreateGame(replayPath: "r1.SC2Replay");
        _repository.InsertGame(game, _sc2ProfileId);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal(game.GameId, loaded.GameId);
        Assert.Equal("Map", loaded.ReplayData.MapName);
        Assert.Equal(600, loaded.ReplayData.GameLengthSeconds);
        Assert.Equal(1m, loaded.ReplayData.Win);
        Assert.Equal("Me", loaded.ReplayData.Player.Name);
        Assert.Equal(3000, loaded.ReplayData.Player.Mmr);
        Assert.Equal('T', loaded.ReplayData.Player.Race);
    }

    [Fact]
    public void InsertGame_CalledTwiceSameReplayPath_DoesNotDuplicate()
    {
        GameData first = CreateGame(replayPath: "same.SC2Replay");
        _repository.InsertGame(first, _sc2ProfileId);
        int firstId = first.GameId!.Value;

        GameData second = CreateGame(replayPath: "same.SC2Replay");
        _repository.InsertGame(second, _sc2ProfileId);

        Assert.Equal(firstId, second.GameId);
        Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
    }

    [Fact]
    public void InsertGame_PersistsAlliesAndOpponentsSeparately()
    {
        GamePlayer ally = new() { Name = "Ally", Clan = "", Mmr = 2900, Race = 'T', Random = false };
        GamePlayer opponent = new() { Name = "Foe", Clan = "", Mmr = 3100, Race = 'Z', Random = false };
        GameData game = CreateGame(allies: [ally], opponents: [opponent]);
        _repository.InsertGame(game, _sc2ProfileId);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        GamePlayer loadedAlly = Assert.Single(loaded.ReplayData.Allies);
        GamePlayer loadedOpponent = Assert.Single(loaded.ReplayData.Opponents);
        Assert.Equal("Ally", loadedAlly.Name);
        Assert.Equal("Foe", loadedOpponent.Name);
    }

    [Fact]
    public void UpdateGameBuild_ThenReload_PersistsBuildId()
    {
        BuildNode build = new() { Name = "4 Gate" };
        _buildRepository.InsertBuild(build, Matchup.VsP, null, 0);

        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);
        _repository.UpdateGameBuild(game.GameId!.Value, build.Id);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal(build.Id, loaded.BuildId);
    }

    [Fact]
    public void UpdateGameNotes_ThenReload_PersistsNotes()
    {
        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);
        _repository.UpdateGameNotes(game.GameId!.Value, "GG well played");

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal("GG well played", loaded.Notes);
    }

    [Fact]
    public void UpsertAttributeValue_ThenGetGamesForProfile_ReturnsValue()
    {
        BuildAttribute attr = InsertAttribute();
        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);

        _repository.UpsertAttributeValue(game.GameId!.Value, attr.Id, "14");

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        GameAttributeValue value = Assert.Single(loaded.AttributeValues);
        Assert.Equal(attr.Id, value.BuildAttributeId);
        Assert.Equal("14", value.Value);
    }

    [Fact]
    public void UpsertAttributeValue_CalledTwice_OverwritesValue()
    {
        BuildAttribute attr = InsertAttribute();
        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);

        _repository.UpsertAttributeValue(game.GameId!.Value, attr.Id, "14");
        _repository.UpsertAttributeValue(game.GameId!.Value, attr.Id, "16");

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        GameAttributeValue value = Assert.Single(loaded.AttributeValues);
        Assert.Equal("16", value.Value);
    }

    [Fact]
    public void DeleteAttributeValue_RemovesOnlyTargetedRow()
    {
        BuildNode build = new() { Name = "Build" };
        _buildRepository.InsertBuild(build, Matchup.VsP, null, 0);
        BuildAttribute attr1 = new() { Name = "A1", Type = AttributeType.Numeric };
        BuildAttribute attr2 = new() { Name = "A2", Type = AttributeType.Numeric };
        _buildRepository.InsertAttribute(attr1, build.Id, 0);
        _buildRepository.InsertAttribute(attr2, build.Id, 1);

        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);
        _repository.UpsertAttributeValue(game.GameId!.Value, attr1.Id, "1");
        _repository.UpsertAttributeValue(game.GameId!.Value, attr2.Id, "2");

        _repository.DeleteAttributeValue(game.GameId!.Value, attr1.Id);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        GameAttributeValue remaining = Assert.Single(loaded.AttributeValues);
        Assert.Equal(attr2.Id, remaining.BuildAttributeId);
    }

    [Fact]
    public void GetGamesForProfile_ScopesByProfile_DoesNotLeakOtherProfilesGames()
    {
        int otherProfileId = InsertProfile("sub-2", 222, "Other").Id;

        GameData myGame = CreateGame(replayPath: "mine.SC2Replay");
        _repository.InsertGame(myGame, _sc2ProfileId);
        GameData otherGame = CreateGame(replayPath: "theirs.SC2Replay");
        _repository.InsertGame(otherGame, otherProfileId);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal("mine.SC2Replay", loaded.ReplayData.ReplayPath);
    }

    private Sc2Profile InsertProfile(string accountSub, int battleNetProfileId, string name)
    {
        BattleNetAccount account = new()
        {
            BattleTag = $"{name}#1234",
            AccountSub = accountSub,
            EncryptedAccessToken = [1],
            TokenExpiresAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        _accountRepository.InsertAccount(account);

        Sc2Profile profile = new()
        {
            BattleNetAccountId = account.Id,
            RegionId = "1",
            RealmId = "1",
            ProfileId = battleNetProfileId,
            Name = name,
        };
        _accountRepository.UpsertProfile(profile);
        return profile;
    }

    private BuildAttribute InsertAttribute()
    {
        BuildNode build = new() { Name = "Build" };
        _buildRepository.InsertBuild(build, Matchup.VsP, null, 0);
        BuildAttribute attr = new() { Name = "Supply", Type = AttributeType.Numeric };
        _buildRepository.InsertAttribute(attr, build.Id, 0);
        return attr;
    }

    private static GameData CreateGame(string replayPath = "replay.SC2Replay", decimal win = 1m,
        GamePlayer[]? allies = null, GamePlayer[]? opponents = null)
    {
        ParsedReplayData replay = new()
        {
            MapName = "Map",
            GameLengthSeconds = 600,
            ReplayPath = replayPath,
            Win = win,
            Player = new GamePlayer { Name = "Me", Clan = "", Mmr = 3000, Race = 'T', Random = false },
            Allies = allies ?? [],
            Opponents = opponents ?? [new GamePlayer { Name = "Foe", Clan = "", Mmr = 3100, Race = 'Z', Random = false }],
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
