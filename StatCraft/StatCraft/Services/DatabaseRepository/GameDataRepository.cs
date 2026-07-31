using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using StatCraft.Models.GameData;

namespace StatCraft.Services.DatabaseRepository
{
    public class GameDataRepository
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public GameDataRepository(string dbPath)
        {
            DapperTypeHandlers.EnsureRegistered();
            _dbPath = dbPath;
            _connectionString = $"Data Source={dbPath}";
        }

        public void Initialize()
        {
            string? dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using SqliteConnection conn = OpenConnection();
            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS Games (
                    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                    Sc2ProfileId      INTEGER NOT NULL REFERENCES Sc2Profiles(Id) ON DELETE CASCADE,
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
                CREATE TABLE IF NOT EXISTS GameBuilds (
                    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                    GameId    INTEGER NOT NULL REFERENCES Games(Id) ON DELETE CASCADE,
                    BuildId   INTEGER NOT NULL REFERENCES BuildNodes(Id) ON DELETE CASCADE,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    UNIQUE(GameId, BuildId)
                );
                CREATE TABLE IF NOT EXISTS GamePlayers (
                    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                    GameId    INTEGER NOT NULL REFERENCES Games(Id) ON DELETE CASCADE,
                    Side      INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    Name      TEXT    NOT NULL DEFAULT '',
                    Clan      TEXT    NOT NULL DEFAULT '',
                    Mmr       INTEGER NOT NULL DEFAULT 0,
                    Race      TEXT    NOT NULL DEFAULT '',
                    Random    INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS GameAttributeValues (
                    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    GameId           INTEGER NOT NULL REFERENCES Games(Id) ON DELETE CASCADE,
                    BuildAttributeId INTEGER NOT NULL REFERENCES BuildAttributes(Id) ON DELETE CASCADE,
                    Value            TEXT    NOT NULL DEFAULT '',
                    UNIQUE(GameId, BuildAttributeId)
                );");

            // Upgrades a pre-existing DB that predates ReplayTimestamp. Backfills it from CreatedAtUtc
            // (the closest thing that existed before — when we recorded the game, not when the replay
            // file itself was last written, but a reasonable stand-in for old rows) rather than leaving it
            // blank. On a fresh DB the CREATE TABLE above already has the column, so ADD COLUMN fails
            // immediately and the whole batch aborts before ever reaching the backfill.
            try
            {
                conn.Execute(@"
                    ALTER TABLE Games ADD COLUMN ReplayTimestamp TEXT NOT NULL DEFAULT '';
                    UPDATE Games SET ReplayTimestamp = CreatedAtUtc WHERE ReplayTimestamp = '';");
            }
            catch (SqliteException)
            {
                // Already migrated, or a fresh DB already created with the new schema.
            }

            // Upgrades a pre-existing DB that predates GameBuilds, moving its single Games.BuildId column
            // into the new join table before dropping the column. On a fresh DB (or one already migrated)
            // Games never has a BuildId column, so the INSERT fails immediately and the batch aborts
            // before ever reaching DROP COLUMN.
            try
            {
                conn.Execute(@"
                    INSERT INTO GameBuilds (GameId, BuildId, SortOrder)
                        SELECT Id, BuildId, 0 FROM Games WHERE BuildId IS NOT NULL;
                    ALTER TABLE Games DROP COLUMN BuildId;");
            }
            catch (SqliteException)
            {
                // Already migrated, or a fresh DB already created with the new schema.
            }
        }

        // Side values in GamePlayers.
        private const int SideAlly = 0;
        private const int SideOpponent = 1;

        internal void InsertGame(GameData game, int sc2ProfileId)
        {
            using SqliteConnection conn = OpenConnection();

            long? existingId = conn.ExecuteScalar<long?>(
                "SELECT Id FROM Games WHERE ReplayPath = @replayPath",
                new { replayPath = game.ReplayData.ReplayPath });
            if (existingId != null)
            {
                game.GameId = (int)existingId.Value;
                return;
            }

            ParsedReplayData replay = game.ReplayData;
            game.GameId = (int)conn.ExecuteScalar<long>(@"
                INSERT INTO Games (Sc2ProfileId, MapName, GameLengthSeconds, ReplayPath, ReplayTimestamp, Win, PlayerName, PlayerClan, PlayerMmr, PlayerRace, PlayerRandom, Notes, CreatedAtUtc)
                VALUES (@sc2ProfileId, @mapName, @gameLengthSeconds, @replayPath, @replayTimestamp, @win, @playerName, @playerClan, @playerMmr, @playerRace, @playerRandom, @notes, @createdAt);
                SELECT last_insert_rowid();",
                new
                {
                    sc2ProfileId,
                    mapName = replay.MapName,
                    gameLengthSeconds = replay.GameLengthSeconds,
                    replayPath = replay.ReplayPath,
                    replayTimestamp = replay.ReplayTimestamp,
                    win = (double)replay.Win,
                    playerName = replay.Player.Name,
                    playerClan = replay.Player.Clan,
                    playerMmr = replay.Player.Mmr,
                    playerRace = replay.Player.Race,
                    playerRandom = replay.Player.Random ? 1 : 0,
                    notes = game.Notes,
                    createdAt = DateTimeOffset.UtcNow,
                });

            InsertGamePlayers(conn, game.GameId.Value, SideAlly, replay.Allies);
            InsertGamePlayers(conn, game.GameId.Value, SideOpponent, replay.Opponents);
        }

        private static void InsertGamePlayers(SqliteConnection conn, int gameId, int side, GamePlayer[] players)
        {
            if (players.Length == 0)
                return;

            conn.Execute(@"
                INSERT INTO GamePlayers (GameId, Side, SortOrder, Name, Clan, Mmr, Race, Random)
                VALUES (@gameId, @side, @sortOrder, @name, @clan, @mmr, @race, @random)",
                players.Select((player, i) => new
                {
                    gameId,
                    side,
                    sortOrder = i,
                    name = player.Name,
                    clan = player.Clan,
                    mmr = player.Mmr,
                    race = player.Race,
                    random = player.Random ? 1 : 0,
                }));
        }

        // Plain classes with settable properties, not positional records — Dapper's constructor-based
        // materialization requires constructor parameter types to exactly match the raw column types,
        // which bypasses both its numeric widening and our DateTimeOffsetTypeHandler. The property-setter
        // path it uses for a parameterless-constructible type applies both correctly.
        private class GameRow
        {
            public long Id { get; set; }
            public string MapName { get; set; } = "";
            public int GameLengthSeconds { get; set; }
            public string ReplayPath { get; set; } = "";
            public DateTimeOffset ReplayTimestamp { get; set; }
            public decimal Win { get; set; }
            public string PlayerName { get; set; } = "";
            public string PlayerClan { get; set; } = "";
            public long PlayerMmr { get; set; }
            public char PlayerRace { get; set; }
            public bool PlayerRandom { get; set; }
            public string Notes { get; set; } = "";
        }

        private class GamePlayerRow
        {
            public long GameId { get; set; }
            public int Side { get; set; }
            public string Name { get; set; } = "";
            public string Clan { get; set; } = "";
            public long Mmr { get; set; }
            public char Race { get; set; }
            public bool Random { get; set; }
        }

        private class GameBuildRow
        {
            public long GameId { get; set; }
            public int BuildId { get; set; }
        }

        private class GameAttributeValueRow
        {
            public long GameId { get; set; }
            public int BuildAttributeId { get; set; }
            public string Value { get; set; } = "";
        }

        internal List<GameData> GetGamesForProfile(int sc2ProfileId)
        {
            using SqliteConnection conn = OpenConnection();

            List<GameRow> gameRows = conn.Query<GameRow>(@"
                SELECT Id, MapName, GameLengthSeconds, ReplayPath, ReplayTimestamp, Win, PlayerName, PlayerClan, PlayerMmr, PlayerRace, PlayerRandom, Notes
                FROM Games WHERE Sc2ProfileId = @sc2ProfileId ORDER BY Id ASC",
                new { sc2ProfileId }).ToList();

            if (gameRows.Count == 0)
                return [];

            string idList = string.Join(",", gameRows.Select(r => r.Id));

            Dictionary<long, List<GamePlayer>> allies = new();
            Dictionary<long, List<GamePlayer>> opponents = new();
            IEnumerable<GamePlayerRow> playerRows = conn.Query<GamePlayerRow>(
                $"SELECT GameId, Side, Name, Clan, Mmr, Race, Random FROM GamePlayers WHERE GameId IN ({idList}) ORDER BY GameId, Side, SortOrder");
            foreach (GamePlayerRow row in playerRows)
            {
                GamePlayer player = new() { Name = row.Name, Clan = row.Clan, Mmr = row.Mmr, Race = row.Race, Random = row.Random };
                Dictionary<long, List<GamePlayer>> target = row.Side == SideAlly ? allies : opponents;
                if (!target.TryGetValue(row.GameId, out List<GamePlayer>? list))
                    target[row.GameId] = list = new();
                list.Add(player);
            }

            Dictionary<long, List<int>> buildIds = new();
            IEnumerable<GameBuildRow> buildRows = conn.Query<GameBuildRow>(
                $"SELECT GameId, BuildId FROM GameBuilds WHERE GameId IN ({idList}) ORDER BY GameId, SortOrder");
            foreach (GameBuildRow row in buildRows)
            {
                if (!buildIds.TryGetValue(row.GameId, out List<int>? list))
                    buildIds[row.GameId] = list = new();
                list.Add(row.BuildId);
            }

            Dictionary<long, List<GameAttributeValue>> attributeValues = new();
            IEnumerable<GameAttributeValueRow> attributeRows = conn.Query<GameAttributeValueRow>(
                $"SELECT GameId, BuildAttributeId, Value FROM GameAttributeValues WHERE GameId IN ({idList})");
            foreach (GameAttributeValueRow row in attributeRows)
            {
                GameAttributeValue value = new() { BuildAttributeId = row.BuildAttributeId, Value = row.Value };
                if (!attributeValues.TryGetValue(row.GameId, out List<GameAttributeValue>? list))
                    attributeValues[row.GameId] = list = new();
                list.Add(value);
            }

            List<GameData> games = new();
            foreach (GameRow row in gameRows)
            {
                ParsedReplayData replay = new()
                {
                    MapName = row.MapName,
                    GameLengthSeconds = row.GameLengthSeconds,
                    ReplayPath = row.ReplayPath,
                    ReplayTimestamp = row.ReplayTimestamp,
                    Win = row.Win,
                    Player = new GamePlayer { Name = row.PlayerName, Clan = row.PlayerClan, Mmr = row.PlayerMmr, Race = row.PlayerRace, Random = row.PlayerRandom },
                    Allies = allies.TryGetValue(row.Id, out List<GamePlayer>? a) ? a.ToArray() : [],
                    Opponents = opponents.TryGetValue(row.Id, out List<GamePlayer>? o) ? o.ToArray() : [],
                };
                games.Add(new GameData
                {
                    GameId = (int)row.Id,
                    ReplayData = replay,
                    BuildIds = buildIds.TryGetValue(row.Id, out List<int>? b) ? b : [],
                    Notes = row.Notes,
                    AttributeValues = attributeValues.TryGetValue(row.Id, out List<GameAttributeValue>? v) ? v : [],
                });
            }
            return games;
        }

        public void UpdateGameBuilds(int gameId, IReadOnlyList<int> buildIds)
        {
            using SqliteConnection conn = OpenConnection();

            conn.Execute("DELETE FROM GameBuilds WHERE GameId = @gameId", new { gameId });

            if (buildIds.Count > 0)
            {
                conn.Execute(
                    "INSERT INTO GameBuilds (GameId, BuildId, SortOrder) VALUES (@gameId, @buildId, @sortOrder)",
                    buildIds.Select((buildId, i) => new { gameId, buildId, sortOrder = i }));
            }
        }

        public void UpdateGameNotes(int gameId, string notes)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("UPDATE Games SET Notes = @notes WHERE Id = @id", new { notes, id = gameId });
        }

        public void UpsertAttributeValue(int gameId, int buildAttributeId, string value)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute(@"
                INSERT INTO GameAttributeValues (GameId, BuildAttributeId, Value)
                VALUES (@gameId, @buildAttributeId, @value)
                ON CONFLICT(GameId, BuildAttributeId) DO UPDATE SET Value = @value",
                new { gameId, buildAttributeId, value });
        }

        public void DeleteAttributeValue(int gameId, int buildAttributeId)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM GameAttributeValues WHERE GameId = @gameId AND BuildAttributeId = @buildAttributeId",
                new { gameId, buildAttributeId });
        }

        // True if any GameBuilds row still points at one of these build node ids. Deleting a BuildNode
        // cascades to its whole subtree (BuildNodes.ParentId ON DELETE CASCADE), and each deleted node
        // cascades away any GameBuilds row referencing it (ON DELETE CASCADE) along with that game's
        // recorded attribute values for it (via BuildAttributes -> GameAttributeValues) — so callers
        // should pass every id in the subtree being deleted, not just the root.
        public bool IsAnyBuildReferenced(IEnumerable<int> buildNodeIds)
        {
            List<int> ids = buildNodeIds.ToList();
            if (ids.Count == 0)
                return false;

            using SqliteConnection conn = OpenConnection();
            string idList = string.Join(",", ids);
            long count = conn.ExecuteScalar<long>($"SELECT COUNT(*) FROM GameBuilds WHERE BuildId IN ({idList})");
            return count > 0;
        }

        private SqliteConnection OpenConnection()
        {
            SqliteConnection conn = new SqliteConnection(_connectionString);
            conn.Open();
            conn.Execute("PRAGMA foreign_keys = ON");
            return conn;
        }
    }
}
