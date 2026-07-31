using Microsoft.Data.Sqlite;
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
        Assert.NotNull(game.ReplayData.Player.GamePlayerId);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        Assert.Equal(game.GameId, loaded.GameId);
        Assert.Equal(game.ReplayData.Player.GamePlayerId, loaded.ReplayData.Player.GamePlayerId);
        Assert.NotNull(loaded.ReplayData.Player.GamePlayerId);
        Assert.Equal("Map", loaded.ReplayData.MapName);
        Assert.Equal(600, loaded.ReplayData.GameLengthSeconds);
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 18, 30, 0, TimeSpan.Zero), loaded.ReplayData.ReplayTimestamp);
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
        Assert.Equal(first.ReplayData.Player.GamePlayerId, second.ReplayData.Player.GamePlayerId);
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
    public void InsertGame_AllyAndOpponentGetTheirOwnDistinctGamePlayerId()
    {
        GamePlayer ally = new() { Name = "Ally", Clan = "", Mmr = 2900, Race = 'T', Random = false };
        GamePlayer opponent = new() { Name = "Foe", Clan = "", Mmr = 3100, Race = 'Z', Random = false };
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

        GamePlayer ally = new() { Name = "Ally", Clan = "", Mmr = 2900, Race = 'T', Random = false };
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
        BuildAttribute attr = InsertAttribute();

        GamePlayer ally = new() { Name = "Ally", Clan = "", Mmr = 2900, Race = 'T', Random = false };
        GameData game = CreateGame(allies: [ally]);
        _repository.InsertGame(game, _sc2ProfileId);
        _repository.UpdateGameBuilds(game.ReplayData.Player.GamePlayerId!.Value, [build.Id]);
        _repository.UpsertAttributeValue(game.ReplayData.Player.GamePlayerId!.Value, attr.Id, "14");

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

        _repository.UpsertAttributeValue(game.ReplayData.Player.GamePlayerId!.Value, attr.Id, "14");

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        GameAttributeValue value = Assert.Single(loaded.ReplayData.Player.AttributeValues);
        Assert.Equal(attr.Id, value.BuildAttributeId);
        Assert.Equal("14", value.Value);
    }

    [Fact]
    public void UpsertAttributeValue_CalledTwice_OverwritesValue()
    {
        BuildAttribute attr = InsertAttribute();
        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);

        _repository.UpsertAttributeValue(game.ReplayData.Player.GamePlayerId!.Value, attr.Id, "14");
        _repository.UpsertAttributeValue(game.ReplayData.Player.GamePlayerId!.Value, attr.Id, "16");

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        GameAttributeValue value = Assert.Single(loaded.ReplayData.Player.AttributeValues);
        Assert.Equal("16", value.Value);
    }

    [Fact]
    public void DeleteAttributeValue_RemovesOnlyTargetedRow()
    {
        BuildNode build = new() { Name = "Build" };
        _buildRepository.InsertBuild(build, null, 0);
        BuildAttribute attr1 = new() { Name = "A1", Type = AttributeType.Numeric };
        BuildAttribute attr2 = new() { Name = "A2", Type = AttributeType.Numeric };
        _buildRepository.InsertAttribute(attr1, build.Id, 0);
        _buildRepository.InsertAttribute(attr2, build.Id, 1);

        GameData game = CreateGame();
        _repository.InsertGame(game, _sc2ProfileId);
        _repository.UpsertAttributeValue(game.ReplayData.Player.GamePlayerId!.Value, attr1.Id, "1");
        _repository.UpsertAttributeValue(game.ReplayData.Player.GamePlayerId!.Value, attr2.Id, "2");

        _repository.DeleteAttributeValue(game.ReplayData.Player.GamePlayerId!.Value, attr1.Id);

        GameData loaded = Assert.Single(_repository.GetGamesForProfile(_sc2ProfileId));
        GameAttributeValue remaining = Assert.Single(loaded.ReplayData.Player.AttributeValues);
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

    [Fact]
    public void Initialize_ExistingOldSchemaWithoutReplayTimestamp_BackfillsFromCreatedAtUtc()
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
                    CREATE TABLE Games (
                        Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                        Sc2ProfileId      INTEGER NOT NULL,
                        MapName           TEXT    NOT NULL DEFAULT '',
                        GameLengthSeconds INTEGER NOT NULL DEFAULT 0,
                        ReplayPath        TEXT    NOT NULL UNIQUE,
                        Win               REAL    NOT NULL DEFAULT 0,
                        PlayerName        TEXT    NOT NULL DEFAULT '',
                        PlayerClan        TEXT    NOT NULL DEFAULT '',
                        PlayerMmr         INTEGER NOT NULL DEFAULT 0,
                        PlayerRace        TEXT    NOT NULL DEFAULT '',
                        PlayerRandom      INTEGER NOT NULL DEFAULT 0,
                        BuildId           INTEGER,
                        Notes             TEXT    NOT NULL DEFAULT '',
                        CreatedAtUtc      TEXT    NOT NULL DEFAULT ''
                    );";
                createCmd.ExecuteNonQuery();

                using SqliteCommand insertCmd = conn.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO Games (Sc2ProfileId, ReplayPath, PlayerRace, CreatedAtUtc)
                    VALUES (1, 'legacy.SC2Replay', 'T', '2025-06-01T12:00:00.0000000+00:00')";
                insertCmd.ExecuteNonQuery();
            }

            GameDataRepository repository = new GameDataRepository(dbPath);
            repository.Initialize();

            GameData loaded = Assert.Single(repository.GetGamesForProfile(1));
            Assert.Equal(DateTimeOffset.Parse("2025-06-01T12:00:00.0000000+00:00"), loaded.ReplayData.ReplayTimestamp);
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

    [Fact]
    public void Initialize_ExistingOldSchemaWithBuildIdColumn_MigratesIntoGameBuilds()
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
                    CREATE TABLE Games (
                        Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                        Sc2ProfileId      INTEGER NOT NULL,
                        MapName           TEXT    NOT NULL DEFAULT '',
                        GameLengthSeconds INTEGER NOT NULL DEFAULT 0,
                        ReplayPath        TEXT    NOT NULL UNIQUE,
                        ReplayTimestamp   TEXT    NOT NULL DEFAULT '',
                        Win               REAL    NOT NULL DEFAULT 0,
                        PlayerName        TEXT    NOT NULL DEFAULT '',
                        PlayerClan        TEXT    NOT NULL DEFAULT '',
                        PlayerMmr         INTEGER NOT NULL DEFAULT 0,
                        PlayerRace        TEXT    NOT NULL DEFAULT '',
                        PlayerRandom      INTEGER NOT NULL DEFAULT 0,
                        BuildId           INTEGER,
                        Notes             TEXT    NOT NULL DEFAULT '',
                        CreatedAtUtc      TEXT    NOT NULL DEFAULT ''
                    );
                    CREATE TABLE BuildNodes (
                        Id   INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL DEFAULT ''
                    );";
                createCmd.ExecuteNonQuery();

                using SqliteCommand insertBuildCmd = conn.CreateCommand();
                insertBuildCmd.CommandText = "INSERT INTO BuildNodes (Id, Name) VALUES (42, 'Legacy Build')";
                insertBuildCmd.ExecuteNonQuery();

                using SqliteCommand insertCmd = conn.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO Games (Sc2ProfileId, ReplayPath, ReplayTimestamp, PlayerRace, BuildId, CreatedAtUtc)
                    VALUES (1, 'legacy.SC2Replay', '2025-06-01T12:00:00.0000000+00:00', 'T', 42, '2025-06-01T12:00:00.0000000+00:00')";
                insertCmd.ExecuteNonQuery();
            }

            GameDataRepository repository = new GameDataRepository(dbPath);
            repository.Initialize();

            GameData loaded = Assert.Single(repository.GetGamesForProfile(1));
            Assert.Equal([42], loaded.ReplayData.Player.BuildIds);
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

    [Fact]
    public void Initialize_ExistingOldSchemaWithGameIdKeyedGameBuilds_MigratesToGamePlayerId()
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
                    CREATE TABLE Games (
                        Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                        Sc2ProfileId      INTEGER NOT NULL,
                        MapName           TEXT    NOT NULL DEFAULT '',
                        GameLengthSeconds INTEGER NOT NULL DEFAULT 0,
                        ReplayPath        TEXT    NOT NULL UNIQUE,
                        ReplayTimestamp   TEXT    NOT NULL DEFAULT '',
                        Win               REAL    NOT NULL DEFAULT 0,
                        PlayerName        TEXT    NOT NULL DEFAULT '',
                        PlayerClan        TEXT    NOT NULL DEFAULT '',
                        PlayerMmr         INTEGER NOT NULL DEFAULT 0,
                        PlayerRace        TEXT    NOT NULL DEFAULT '',
                        PlayerRandom      INTEGER NOT NULL DEFAULT 0,
                        Notes             TEXT    NOT NULL DEFAULT '',
                        CreatedAtUtc      TEXT    NOT NULL DEFAULT ''
                    );
                    CREATE TABLE GamePlayers (
                        Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                        GameId    INTEGER NOT NULL,
                        Side      INTEGER NOT NULL,
                        SortOrder INTEGER NOT NULL DEFAULT 0,
                        Name      TEXT    NOT NULL DEFAULT '',
                        Clan      TEXT    NOT NULL DEFAULT '',
                        Mmr       INTEGER NOT NULL DEFAULT 0,
                        Race      TEXT    NOT NULL DEFAULT '',
                        Random    INTEGER NOT NULL DEFAULT 0
                    );
                    CREATE TABLE GameBuilds (
                        Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                        GameId    INTEGER NOT NULL,
                        BuildId   INTEGER NOT NULL,
                        SortOrder INTEGER NOT NULL DEFAULT 0,
                        UNIQUE(GameId, BuildId)
                    );
                    CREATE TABLE GameAttributeValues (
                        Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                        GameId           INTEGER NOT NULL,
                        BuildAttributeId INTEGER NOT NULL,
                        Value            TEXT    NOT NULL DEFAULT '',
                        UNIQUE(GameId, BuildAttributeId)
                    );
                    CREATE TABLE BuildNodes (
                        Id   INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL DEFAULT ''
                    );
                    CREATE TABLE BuildAttributes (
                        Id   INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL DEFAULT ''
                    );";
                createCmd.ExecuteNonQuery();

                using SqliteCommand insertGameCmd = conn.CreateCommand();
                insertGameCmd.CommandText = @"
                    INSERT INTO Games (Sc2ProfileId, ReplayPath, ReplayTimestamp, PlayerName, PlayerRace, CreatedAtUtc)
                    VALUES (1, 'legacy.SC2Replay', '2025-06-01T12:00:00.0000000+00:00', 'Me', 'T', '2025-06-01T12:00:00.0000000+00:00')";
                insertGameCmd.ExecuteNonQuery();

                using SqliteCommand insertAllyCmd = conn.CreateCommand();
                insertAllyCmd.CommandText = "INSERT INTO GamePlayers (GameId, Side, SortOrder, Name, Race) VALUES (1, 1, 0, 'Foe', 'Z')";
                insertAllyCmd.ExecuteNonQuery();

                using SqliteCommand insertBuildNodeCmd = conn.CreateCommand();
                insertBuildNodeCmd.CommandText = "INSERT INTO BuildNodes (Id, Name) VALUES (42, 'Legacy Build')";
                insertBuildNodeCmd.ExecuteNonQuery();

                using SqliteCommand insertAttrCmd = conn.CreateCommand();
                insertAttrCmd.CommandText = "INSERT INTO BuildAttributes (Id, Name) VALUES (7, 'Legacy Attr')";
                insertAttrCmd.ExecuteNonQuery();

                using SqliteCommand insertGameBuildCmd = conn.CreateCommand();
                insertGameBuildCmd.CommandText = "INSERT INTO GameBuilds (GameId, BuildId, SortOrder) VALUES (1, 42, 0)";
                insertGameBuildCmd.ExecuteNonQuery();

                using SqliteCommand insertAttrValueCmd = conn.CreateCommand();
                insertAttrValueCmd.CommandText = "INSERT INTO GameAttributeValues (GameId, BuildAttributeId, Value) VALUES (1, 7, '99')";
                insertAttrValueCmd.ExecuteNonQuery();
            }

            GameDataRepository repository = new GameDataRepository(dbPath);
            repository.Initialize();

            GameData loaded = Assert.Single(repository.GetGamesForProfile(1));
            Assert.NotNull(loaded.ReplayData.Player.GamePlayerId);
            Assert.Equal([42], loaded.ReplayData.Player.BuildIds);
            GameAttributeValue value = Assert.Single(loaded.ReplayData.Player.AttributeValues);
            Assert.Equal(7, value.BuildAttributeId);
            Assert.Equal("99", value.Value);
            // The pre-existing Opponent row (Side = 1) must not be mistaken for the migrated Self row.
            GamePlayer opponent = Assert.Single(loaded.ReplayData.Opponents);
            Assert.Equal("Foe", opponent.Name);
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
        _buildRepository.InsertBuild(build, null, 0);
        BuildAttribute attr = new() { Name = "Supply", Type = AttributeType.Numeric };
        _buildRepository.InsertAttribute(attr, build.Id, 0);
        return attr;
    }

    private static GameData CreateGame(string replayPath = "replay.SC2Replay", decimal win = 1m,
        GamePlayer[]? allies = null, GamePlayer[]? opponents = null, DateTimeOffset? replayTimestamp = null)
    {
        ParsedReplayData replay = new()
        {
            MapName = "Map",
            GameLengthSeconds = 600,
            ReplayPath = replayPath,
            ReplayTimestamp = replayTimestamp ?? new DateTimeOffset(2026, 1, 15, 18, 30, 0, TimeSpan.Zero),
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
