using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Maps;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.Tests;

public class GameDataRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly GameDataRepository _repository;
    private readonly BuildRepository _buildRepository;
    private readonly AccountRepository _accountRepository;
    private readonly MapRepository _mapRepository;
    private readonly int _sc2ProfileId;

    public GameDataRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");

        _accountRepository = new AccountRepository(_dbPath);
        _accountRepository.Initialize();
        _buildRepository = new BuildRepository(_dbPath);
        _buildRepository.Initialize();
        // Before GameDataRepository, whose MapName -> MapId migration writes into the Maps table — the
        // same ordering App.axaml.cs enforces through DI.
        _mapRepository = new MapRepository(_dbPath);
        _mapRepository.Initialize();
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
        Assert.NotNull(game.ReplayData.Player.GamePlayerId);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal(game.GameId, loaded.GameId);
        Assert.Equal(game.ReplayData.Player.GamePlayerId, loaded.ReplayData.Player.GamePlayerId);
        Assert.NotNull(loaded.ReplayData.Player.GamePlayerId);
        Assert.Equal("Map", loaded.Map?.Name);
        Assert.Equal(600, loaded.ReplayData.GameLengthSeconds);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 18, 30, 0, TimeSpan.Zero), loaded.ReplayData.ReplayTimestamp);
        Assert.Equal(1m, loaded.ReplayData.Win);
        Assert.Equal("Me", loaded.ReplayData.Player.Name);
        Assert.Equal(3000, loaded.ReplayData.Player.Mmr.Mmr);
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
        Assert.Equal(first.ReplayData.Player.GamePlayerId, second.ReplayData.Player.GamePlayerId);
        Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
    }

    [Fact]
    public void InsertGame_PersistsAlliesAndOpponentsSeparately()
    {
        GamePlayer ally = new() { Name = "Ally", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 2900 }, Race = 'T', Random = false };
        GamePlayer opponent = new() { Name = "Foe", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3100 }, Race = 'Z', Random = false };
        GameData game = CreateGame(allies: [ally], opponents: [opponent]);
        _repository.InsertGame(game, _sc2ProfileId);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        GamePlayer loadedAlly = Assert.Single(loaded.ReplayData.Allies);
        GamePlayer loadedOpponent = Assert.Single(loaded.ReplayData.Opponents);
        Assert.Equal("Ally", loadedAlly.Name);
        Assert.Equal("Foe", loadedOpponent.Name);
    }

    [Fact]
    public void InsertGame_AllyAndOpponentGetTheirOwnDistinctGamePlayerId()
    {
        GamePlayer ally = new() { Name = "Ally", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 2900 }, Race = 'T', Random = false };
        GamePlayer opponent = new() { Name = "Foe", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3100 }, Race = 'Z', Random = false };
        GameData game = CreateGame(allies: [ally], opponents: [opponent]);
        _repository.InsertGame(game, _sc2ProfileId);

        Assert.NotNull(ally.GamePlayerId);
        Assert.NotNull(opponent.GamePlayerId);
        Assert.NotEqual(ally.GamePlayerId, opponent.GamePlayerId);
        Assert.NotEqual(game.ReplayData.Player.GamePlayerId, ally.GamePlayerId);
    }

    [Fact]
    public void UpdateGameBuilds_ForAllyGamePlayerId_TracksIndependentlyFromSelf()
    {
        BuildNode selfBuild = new() { Name = "Self Build" };
        _buildRepository.InsertBuild(selfBuild, null, 0);
        BuildNode allyBuild = new() { Name = "Ally Build" };
        _buildRepository.InsertBuild(allyBuild, null, 1);

        GamePlayer ally = new() { Name = "Ally", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 2900 }, Race = 'T', Random = false };
        GameData game = CreateGame(allies: [ally]);
        _repository.InsertGame(game, _sc2ProfileId);

        _repository.UpdateGameBuilds(game.ReplayData.Player.GamePlayerId!.Value, [selfBuild.Id]);
        _repository.UpdateGameBuilds(ally.GamePlayerId!.Value, [allyBuild.Id]);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal([selfBuild.Id], loaded.ReplayData.Player.BuildIds);
        GamePlayer loadedAlly = Assert.Single(loaded.ReplayData.Allies);
        Assert.Equal([allyBuild.Id], loadedAlly.BuildIds);
    }

    [Fact]
    public void UpdateGameBuilds_ThenReload_PersistsBuildId()
    {
        BuildNode build = new() { Name = "4 Gate" };
        _buildRepository.InsertBuild(build, null, 0);

        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);
        _repository.UpdateGameBuilds(game.ReplayData.Player.GamePlayerId!.Value, [build.Id]);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal([build.Id], loaded.ReplayData.Player.BuildIds);
    }

    [Fact]
    public void UpdateGameBuilds_MultipleBuilds_ThenReload_PersistsInOrder()
    {
        BuildNode buildA = new() { Name = "A" };
        _buildRepository.InsertBuild(buildA, null, 0);
        BuildNode buildB = new() { Name = "B" };
        _buildRepository.InsertBuild(buildB, null, 1);

        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);
        _repository.UpdateGameBuilds(game.ReplayData.Player.GamePlayerId!.Value, [buildB.Id, buildA.Id]);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal([buildB.Id, buildA.Id], loaded.ReplayData.Player.BuildIds);
    }

    [Fact]
    public void UpdateGameBuilds_CalledAgainWithFewerBuilds_ReplacesPreviousSet()
    {
        BuildNode buildA = new() { Name = "A" };
        _buildRepository.InsertBuild(buildA, null, 0);
        BuildNode buildB = new() { Name = "B" };
        _buildRepository.InsertBuild(buildB, null, 1);

        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);
        _repository.UpdateGameBuilds(game.ReplayData.Player.GamePlayerId!.Value, [buildA.Id, buildB.Id]);
        _repository.UpdateGameBuilds(game.ReplayData.Player.GamePlayerId!.Value, [buildB.Id]);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal([buildB.Id], loaded.ReplayData.Player.BuildIds);
    }

    [Fact]
    public void DeleteGame_RemovesGameAndItsPlayersBuildsAndAttributeValues()
    {
        BuildNode build = new() { Name = "4 Gate" };
        _buildRepository.InsertBuild(build, null, 0);
        AttributeValue attr = InsertAttribute();

        GamePlayer ally = new() { Name = "Ally", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 2900 }, Race = 'T', Random = false };
        GameData game = CreateGame(allies: [ally]);
        _repository.InsertGame(game, _sc2ProfileId);
        _repository.UpdateGameBuilds(game.ReplayData.Player.GamePlayerId!.Value, [build.Id]);
        _repository.UpsertAttributeValue(game.ReplayData.Player.GamePlayerId!.Value, attr.Definition.Id, "14");

        _repository.DeleteGame(game.GameId!.Value);

        Assert.Empty(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.False(_repository.IsAnyBuildReferenced([build.Id]));
    }

    [Fact]
    public void DeleteGame_UnrelatedGameForSameProfile_IsUnaffected()
    {
        GameData keep = CreateGame(replayPath: "keep.SC2Replay");
        _repository.InsertGame(keep, _sc2ProfileId);
        GameData delete = CreateGame(replayPath: "delete.SC2Replay");
        _repository.InsertGame(delete, _sc2ProfileId);

        _repository.DeleteGame(delete.GameId!.Value);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal(keep.GameId, loaded.GameId);
    }

    [Fact]
    public void InsertGame_PersistsGameType()
    {
        GameData game = CreateGame();
        game.GameType = GameType.Unranked;
        _repository.InsertGame(game, _sc2ProfileId);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal(GameType.Unranked, loaded.GameType);
    }

    [Fact]
    public void UpdateGameType_OverridesTheInferredValue()
    {
        // Ranked vs Unranked is inferred and can be wrong, so a manual correction has to stick.
        GameData game = CreateGame();
        game.GameType = GameType.Ranked;
        _repository.InsertGame(game, _sc2ProfileId);

        _repository.UpdateGameType(game.GameId!.Value, GameType.Unranked);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal(GameType.Unranked, loaded.GameType);
    }

    [Fact]
    public void UpdateGameType_LeavesOtherGamesAlone()
    {
        GameData first = CreateGame(replayPath: "a.SC2Replay");
        first.GameType = GameType.Ranked;
        _repository.InsertGame(first, _sc2ProfileId);
        GameData second = CreateGame(replayPath: "b.SC2Replay");
        second.GameType = GameType.Ranked;
        _repository.InsertGame(second, _sc2ProfileId);

        _repository.UpdateGameType(first.GameId!.Value, GameType.Unranked);

        List<GameData> loaded = _repository.GetGamesForProfile(_sc2ProfileId);
        Assert.Equal(GameType.Unranked, loaded.Single(g => g.GameId == first.GameId).GameType);
        Assert.Equal(GameType.Ranked, loaded.Single(g => g.GameId == second.GameId).GameType);
    }

    [Fact]
    public void InsertGame_MmrAfterStartsNull()
    {
        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Null(loaded.ReplayData.Player.MmrAfter);
        Assert.Null(loaded.ReplayData.Player.MmrChange);
    }

    [Fact]
    public void UpdateGamePlayerMmrAfter_ThenReload_PersistsValueAndDerivesChange()
    {
        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);

        // CreateGame gives the self player an Mmr of 3000 going into the game.
        _repository.UpdateGamePlayerMmrAfter(game.ReplayData.Player.GamePlayerId!.Value, 3024);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal(3024, loaded.ReplayData.Player.MmrAfter);
        Assert.Equal(24, loaded.ReplayData.Player.MmrChange);
    }

    [Fact]
    public void UpdateGamePlayerMmrAfter_LowerThanBefore_YieldsNegativeChange()
    {
        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);

        _repository.UpdateGamePlayerMmrAfter(game.ReplayData.Player.GamePlayerId!.Value, 2976);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal(-24, loaded.ReplayData.Player.MmrChange);
    }

    [Fact]
    public void UpdateGamePlayerMmrAfter_DoesNotTouchOtherPlayers()
    {
        GamePlayer opponent = new() { Name = "Foe", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3100 }, Race = 'Z', Random = false };
        GameData game = CreateGame(opponents: [opponent]);
        _repository.InsertGame(game, _sc2ProfileId);

        _repository.UpdateGamePlayerMmrAfter(game.ReplayData.Player.GamePlayerId!.Value, 3024);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal(3024, loaded.ReplayData.Player.MmrAfter);
        Assert.Null(Assert.Single(loaded.ReplayData.Opponents).MmrAfter);
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
        AttributeValue attr = InsertAttribute();
        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);

        _repository.UpsertAttributeValue(game.ReplayData.Player.GamePlayerId!.Value, attr.Definition.Id, "14");

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        GameAttributeValue value = Assert.Single(loaded.ReplayData.Player.AttributeValues);
        Assert.Equal(attr.Definition.Id, value.BuildAttributeId);
        Assert.Equal("14", value.Value);
    }

    [Fact]
    public void UpsertAttributeValue_CalledTwice_OverwritesValue()
    {
        AttributeValue attr = InsertAttribute();
        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);

        _repository.UpsertAttributeValue(game.ReplayData.Player.GamePlayerId!.Value, attr.Definition.Id, "14");
        _repository.UpsertAttributeValue(game.ReplayData.Player.GamePlayerId!.Value, attr.Definition.Id, "16");

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        GameAttributeValue value = Assert.Single(loaded.ReplayData.Player.AttributeValues);
        Assert.Equal("16", value.Value);
    }

    [Fact]
    public void DeleteAttributeValue_RemovesOnlyTargetedRow()
    {
        BuildNode build = new() { Name = "Build" };
        _buildRepository.InsertBuild(build, null, 0);
        AttributeValue attr1 = new(new AttributeDefinition(AttributeScope.BuildDetail) { Name = "A1", Type = AttributeType.Numeric });
        AttributeValue attr2 = new(new AttributeDefinition(AttributeScope.BuildDetail) { Name = "A2", Type = AttributeType.Numeric });
        _buildRepository.InsertAttribute(attr1, build.Id, 0);
        _buildRepository.InsertAttribute(attr2, build.Id, 1);

        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);
        _repository.UpsertAttributeValue(game.ReplayData.Player.GamePlayerId!.Value, attr1.Definition.Id, "1");
        _repository.UpsertAttributeValue(game.ReplayData.Player.GamePlayerId!.Value, attr2.Definition.Id, "2");

        _repository.DeleteAttributeValue(game.ReplayData.Player.GamePlayerId!.Value, attr1.Definition.Id);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        GameAttributeValue remaining = Assert.Single(loaded.ReplayData.Player.AttributeValues);
        Assert.Equal(attr2.Definition.Id, remaining.BuildAttributeId);
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

    [Fact]
    public void GetGamesForProfiles_MergesGamesFromMultipleProfiles()
    {
        int otherProfileId = InsertProfile("sub-2", 222, "Other").Id;

        GameData myGame = CreateGame(replayPath: "mine.SC2Replay");
        _repository.InsertGame(myGame, _sc2ProfileId);
        GameData otherGame = CreateGame(replayPath: "theirs.SC2Replay");
        _repository.InsertGame(otherGame, otherProfileId);

        List<GameData> loaded = _repository.GetGamesForProfiles([_sc2ProfileId, otherProfileId]);
        Assert.Equal(2, loaded.Count);
        Assert.Contains(loaded, g => g.ReplayData.ReplayPath == "mine.SC2Replay");
        Assert.Contains(loaded, g => g.ReplayData.ReplayPath == "theirs.SC2Replay");
    }

    [Fact]
    public void GetGamesForProfiles_EmptyIdList_ReturnsEmpty()
    {
        _repository.InsertGame(CreateGame(), _sc2ProfileId);

        Assert.Empty(_repository.GetGamesForProfiles([]));
    }

    [Fact]
    public void GetGamesForProfiles_OrdersAcrossProfilesByReplayTimestamp()
    {
        int otherProfileId = InsertProfile("sub-2", 222, "Other").Id;

        GameData later = CreateGame(replayPath: "later.SC2Replay", replayTimestamp: new DateTimeOffset(2026, 1, 20, 0, 0, 0, TimeSpan.Zero));
        _repository.InsertGame(later, _sc2ProfileId);
        GameData earlier = CreateGame(replayPath: "earlier.SC2Replay", replayTimestamp: new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero));
        _repository.InsertGame(earlier, otherProfileId);

        List<GameData> loaded = _repository.GetGamesForProfiles([_sc2ProfileId, otherProfileId]);
        Assert.Equal(["earlier.SC2Replay", "later.SC2Replay"], loaded.Select(g => g.ReplayData.ReplayPath));
    }

    [Fact]
    public void GetGamesForProfile_IsEquivalentToGetGamesForProfilesWithSingleId()
    {
        _repository.InsertGame(CreateGame(replayPath: "solo.SC2Replay"), _sc2ProfileId);

        List<GameData> viaSingle = _repository.GetGamesForProfile(_sc2ProfileId);
        List<GameData> viaMulti = _repository.GetGamesForProfiles([_sc2ProfileId]);
        Assert.Equal(viaSingle.Select(g => g.GameId), viaMulti.Select(g => g.GameId));
    }

    [Fact]
    public void IsAnyBuildReferenced_BuildUsedByAGame_ReturnsTrue()
    {
        BuildNode build = new() { Name = "4 Gate" };
        _buildRepository.InsertBuild(build, null, 0);

        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);
        _repository.UpdateGameBuilds(game.ReplayData.Player.GamePlayerId!.Value, [build.Id]);

        Assert.True(_repository.IsAnyBuildReferenced([build.Id]));
    }

    [Fact]
    public void IsAnyBuildReferenced_BuildNotUsedByAnyGame_ReturnsFalse()
    {
        BuildNode build = new() { Name = "4 Gate" };
        _buildRepository.InsertBuild(build, null, 0);

        Assert.False(_repository.IsAnyBuildReferenced([build.Id]));
    }

    [Fact]
    public void IsAnyBuildReferenced_NoIdsGiven_ReturnsFalse()
    {
        Assert.False(_repository.IsAnyBuildReferenced([]));
    }

    [Fact]
    public void IsAnyBuildReferenced_MatchesAnyIdInSet_ReturnsTrue()
    {
        BuildNode parent = new() { Name = "Parent" };
        _buildRepository.InsertBuild(parent, null, 0);
        BuildNode child = new() { Name = "Child" };
        _buildRepository.InsertBuild(child, parent.Id, 0);

        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);
        _repository.UpdateGameBuilds(game.ReplayData.Player.GamePlayerId!.Value, [child.Id]);

        // Simulates deleting "parent", which would cascade-delete "child" too — the caller passes the
        // whole subtree, and only "child" (not "parent") is actually referenced by a game.
        Assert.True(_repository.IsAnyBuildReferenced([parent.Id, child.Id]));
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

    private AttributeValue InsertAttribute()
    {
        BuildNode build = new() { Name = "Build" };
        _buildRepository.InsertBuild(build, null, 0);
        AttributeValue attr = new(new AttributeDefinition(AttributeScope.BuildDetail) { Name = "Supply", Type = AttributeType.Numeric });
        _buildRepository.InsertAttribute(attr, build.Id, 0);
        return attr;
    }

    private GameData CreateGame(string replayPath = "replay.SC2Replay", decimal win = 1m,
        GamePlayer[]? allies = null, GamePlayer[]? opponents = null, DateTimeOffset? replayTimestamp = null,
        string mapName = "Map")
    {
        ParsedReplayData replay = new()
        {
            GameLengthSeconds = 600,
            ReplayPath = replayPath,
            ReplayTimestamp = replayTimestamp ?? new DateTimeOffset(2026, 1, 15, 18, 30, 0, TimeSpan.Zero),
            Win = win,
            Player = new GamePlayer { Name = "Me", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3000 }, Race = 'T', Random = false },
            Allies = allies ?? [],
            Opponents = opponents ?? [new GamePlayer { Name = "Foe", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3100 }, Race = 'Z', Random = false }],
        };
        return new GameData { Map = _mapRepository.GetOrCreateMap(mapName), ReplayData = replay };
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
