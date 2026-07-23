using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
            _dbPath = dbPath;
            _connectionString = $"Data Source={dbPath}";
        }

        public void Initialize()
        {
            string? dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Games (
                    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                    Sc2ProfileId      INTEGER NOT NULL REFERENCES Sc2Profiles(Id) ON DELETE CASCADE,
                    MapName           TEXT    NOT NULL DEFAULT '',
                    GameLengthSeconds INTEGER NOT NULL DEFAULT 0,
                    ReplayPath        TEXT    NOT NULL UNIQUE,
                    Win               REAL    NOT NULL DEFAULT 0,
                    PlayerName        TEXT    NOT NULL DEFAULT '',
                    PlayerClan        TEXT    NOT NULL DEFAULT '',
                    PlayerMmr         INTEGER NOT NULL DEFAULT 0,
                    PlayerRace        TEXT    NOT NULL DEFAULT '',
                    PlayerRandom      INTEGER NOT NULL DEFAULT 0,
                    BuildId           INTEGER REFERENCES BuildNodes(Id) ON DELETE SET NULL,
                    Notes             TEXT    NOT NULL DEFAULT '',
                    CreatedAtUtc      TEXT    NOT NULL DEFAULT ''
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
                );";
            cmd.ExecuteNonQuery();
        }

        // Side values in GamePlayers.
        private const int SideAlly = 0;
        private const int SideOpponent = 1;

        internal void InsertGame(GameData game, int sc2ProfileId)
        {
            using SqliteConnection conn = OpenConnection();

            using (SqliteCommand existingCmd = conn.CreateCommand())
            {
                existingCmd.CommandText = "SELECT Id FROM Games WHERE ReplayPath = @replayPath";
                existingCmd.Parameters.AddWithValue("@replayPath", game.ReplayData.ReplayPath);
                object? existingId = existingCmd.ExecuteScalar();
                if (existingId != null)
                {
                    game.GameId = (int)(long)existingId;
                    return;
                }
            }

            ParsedReplayData replay = game.ReplayData;
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Games (Sc2ProfileId, MapName, GameLengthSeconds, ReplayPath, Win, PlayerName, PlayerClan, PlayerMmr, PlayerRace, PlayerRandom, BuildId, Notes, CreatedAtUtc)
                    VALUES (@sc2ProfileId, @mapName, @gameLengthSeconds, @replayPath, @win, @playerName, @playerClan, @playerMmr, @playerRace, @playerRandom, @buildId, @notes, @createdAt);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@sc2ProfileId", sc2ProfileId);
                cmd.Parameters.AddWithValue("@mapName", replay.MapName);
                cmd.Parameters.AddWithValue("@gameLengthSeconds", replay.GameLengthSeconds);
                cmd.Parameters.AddWithValue("@replayPath", replay.ReplayPath);
                cmd.Parameters.AddWithValue("@win", (double)replay.Win);
                cmd.Parameters.AddWithValue("@playerName", replay.Player.Name);
                cmd.Parameters.AddWithValue("@playerClan", replay.Player.Clan);
                cmd.Parameters.AddWithValue("@playerMmr", replay.Player.Mmr);
                cmd.Parameters.AddWithValue("@playerRace", replay.Player.Race.ToString());
                cmd.Parameters.AddWithValue("@playerRandom", replay.Player.Random ? 1 : 0);
                cmd.Parameters.AddWithValue("@buildId", (object?)game.BuildId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@notes", game.Notes);
                cmd.Parameters.AddWithValue("@createdAt", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                game.GameId = (int)(long)cmd.ExecuteScalar()!;
            }

            InsertGamePlayers(conn, game.GameId.Value, SideAlly, replay.Allies);
            InsertGamePlayers(conn, game.GameId.Value, SideOpponent, replay.Opponents);
        }

        private static void InsertGamePlayers(SqliteConnection conn, int gameId, int side, GamePlayer[] players)
        {
            for (int i = 0; i < players.Length; i++)
            {
                GamePlayer player = players[i];
                using SqliteCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO GamePlayers (GameId, Side, SortOrder, Name, Clan, Mmr, Race, Random)
                    VALUES (@gameId, @side, @sortOrder, @name, @clan, @mmr, @race, @random)";
                cmd.Parameters.AddWithValue("@gameId", gameId);
                cmd.Parameters.AddWithValue("@side", side);
                cmd.Parameters.AddWithValue("@sortOrder", i);
                cmd.Parameters.AddWithValue("@name", player.Name);
                cmd.Parameters.AddWithValue("@clan", player.Clan);
                cmd.Parameters.AddWithValue("@mmr", player.Mmr);
                cmd.Parameters.AddWithValue("@race", player.Race.ToString());
                cmd.Parameters.AddWithValue("@random", player.Random ? 1 : 0);
                cmd.ExecuteNonQuery();
            }
        }

        internal List<GameData> GetGamesForProfile(int sc2ProfileId)
        {
            using SqliteConnection conn = OpenConnection();

            Dictionary<long, GameRow> rows = new();
            List<long> gameIds = new();

            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT Id, MapName, GameLengthSeconds, ReplayPath, Win, PlayerName, PlayerClan, PlayerMmr, PlayerRace, PlayerRandom, BuildId, Notes
                    FROM Games WHERE Sc2ProfileId = @sc2ProfileId ORDER BY Id ASC";
                cmd.Parameters.AddWithValue("@sc2ProfileId", sc2ProfileId);
                using SqliteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    long id = reader.GetInt64(0);
                    gameIds.Add(id);
                    rows[id] = new GameRow(
                        MapName: reader.GetString(1),
                        GameLengthSeconds: reader.GetInt32(2),
                        ReplayPath: reader.GetString(3),
                        Win: (decimal)reader.GetDouble(4),
                        PlayerName: reader.GetString(5),
                        PlayerClan: reader.GetString(6),
                        PlayerMmr: reader.GetInt64(7),
                        PlayerRace: reader.GetString(8)[0],
                        PlayerRandom: reader.GetInt32(9) != 0,
                        BuildId: reader.IsDBNull(10) ? null : reader.GetInt32(10),
                        Notes: reader.GetString(11));
                }
            }

            if (gameIds.Count == 0)
                return [];

            string idList = string.Join(",", gameIds);

            Dictionary<long, List<GamePlayer>> allies = new();
            Dictionary<long, List<GamePlayer>> opponents = new();
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT GameId, Side, Name, Clan, Mmr, Race, Random FROM GamePlayers WHERE GameId IN ({idList}) ORDER BY GameId, Side, SortOrder";
                using SqliteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    long gameId = reader.GetInt64(0);
                    int side = reader.GetInt32(1);
                    GamePlayer player = new()
                    {
                        Name = reader.GetString(2),
                        Clan = reader.GetString(3),
                        Mmr = reader.GetInt64(4),
                        Race = reader.GetString(5)[0],
                        Random = reader.GetInt32(6) != 0,
                    };
                    Dictionary<long, List<GamePlayer>> target = side == SideAlly ? allies : opponents;
                    if (!target.TryGetValue(gameId, out List<GamePlayer>? list))
                        target[gameId] = list = new();
                    list.Add(player);
                }
            }

            Dictionary<long, List<GameAttributeValue>> attributeValues = new();
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT GameId, BuildAttributeId, Value FROM GameAttributeValues WHERE GameId IN ({idList})";
                using SqliteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    long gameId = reader.GetInt64(0);
                    GameAttributeValue value = new() { BuildAttributeId = reader.GetInt32(1), Value = reader.GetString(2) };
                    if (!attributeValues.TryGetValue(gameId, out List<GameAttributeValue>? list))
                        attributeValues[gameId] = list = new();
                    list.Add(value);
                }
            }

            List<GameData> games = new();
            foreach (long id in gameIds)
            {
                GameRow row = rows[id];
                ParsedReplayData replay = new()
                {
                    MapName = row.MapName,
                    GameLengthSeconds = row.GameLengthSeconds,
                    ReplayPath = row.ReplayPath,
                    Win = row.Win,
                    Player = new GamePlayer { Name = row.PlayerName, Clan = row.PlayerClan, Mmr = row.PlayerMmr, Race = row.PlayerRace, Random = row.PlayerRandom },
                    Allies = allies.TryGetValue(id, out List<GamePlayer>? a) ? a.ToArray() : [],
                    Opponents = opponents.TryGetValue(id, out List<GamePlayer>? o) ? o.ToArray() : [],
                };
                games.Add(new GameData
                {
                    GameId = (int)id,
                    ReplayData = replay,
                    BuildId = row.BuildId,
                    Notes = row.Notes,
                    AttributeValues = attributeValues.TryGetValue(id, out List<GameAttributeValue>? v) ? v : [],
                });
            }
            return games;
        }

        public void UpdateGameBuild(int gameId, int? buildId)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Games SET BuildId = @buildId WHERE Id = @id";
            cmd.Parameters.AddWithValue("@buildId", (object?)buildId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", gameId);
            cmd.ExecuteNonQuery();
        }

        public void UpdateGameNotes(int gameId, string notes)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Games SET Notes = @notes WHERE Id = @id";
            cmd.Parameters.AddWithValue("@notes", notes);
            cmd.Parameters.AddWithValue("@id", gameId);
            cmd.ExecuteNonQuery();
        }

        public void UpsertAttributeValue(int gameId, int buildAttributeId, string value)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO GameAttributeValues (GameId, BuildAttributeId, Value)
                VALUES (@gameId, @buildAttributeId, @value)
                ON CONFLICT(GameId, BuildAttributeId) DO UPDATE SET Value = @value";
            cmd.Parameters.AddWithValue("@gameId", gameId);
            cmd.Parameters.AddWithValue("@buildAttributeId", buildAttributeId);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.ExecuteNonQuery();
        }

        public void DeleteAttributeValue(int gameId, int buildAttributeId)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM GameAttributeValues WHERE GameId = @gameId AND BuildAttributeId = @buildAttributeId";
            cmd.Parameters.AddWithValue("@gameId", gameId);
            cmd.Parameters.AddWithValue("@buildAttributeId", buildAttributeId);
            cmd.ExecuteNonQuery();
        }

        private record GameRow(
            string MapName, int GameLengthSeconds, string ReplayPath, decimal Win,
            string PlayerName, string PlayerClan, long PlayerMmr, char PlayerRace, bool PlayerRandom,
            int? BuildId, string Notes);

        private SqliteConnection OpenConnection()
        {
            SqliteConnection conn = new SqliteConnection(_connectionString);
            conn.Open();
            using SqliteCommand pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON";
            pragma.ExecuteNonQuery();
            return conn;
        }
    }
}
