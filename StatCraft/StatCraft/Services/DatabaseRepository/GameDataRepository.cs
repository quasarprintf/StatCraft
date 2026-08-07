using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Maps;

namespace StatCraft.Services.DatabaseRepository
{
    public partial class GameDataRepository : SqliteRepository
    {
        // Side values in GamePlayers.
        private const int SideAlly = 0;
        private const int SideOpponent = 1;
        private const int SideSelf = 2;

        public GameDataRepository(string dbPath) : base(dbPath)
        {
        }

        internal void InsertGame(GameData game, int sc2ProfileId)
        {
            using SqliteConnection conn = OpenConnection();
            game.Sc2ProfileId = sc2ProfileId;

            long? existingId = conn.ExecuteScalar<long?>(
                "SELECT Id FROM Games WHERE ReplayPath = @replayPath",
                new { replayPath = game.ReplayData.ReplayPath });
            if (existingId != null)
            {
                game.GameId = (int)existingId.Value;
                game.ReplayData.Player.GamePlayerId = (int)conn.ExecuteScalar<long>(
                    "SELECT Id FROM GamePlayers WHERE GameId = @gameId AND Side = @side",
                    new { gameId = game.GameId, side = SideSelf });
                return;
            }

            ParsedReplayData replay = game.ReplayData;
            game.GameId = (int)conn.ExecuteScalar<long>(@"
                INSERT INTO Games (Sc2ProfileId, MapId, GameLengthSeconds, ReplayPath, ReplayTimestamp, Win, PlayerName, PlayerClan, PlayerMmr, PlayerRace, PlayerRandom, Notes, CreatedAtUtc, GameType)
                VALUES (@sc2ProfileId, @mapId, @gameLengthSeconds, @replayPath, @replayTimestamp, @win, @playerName, @playerClan, @playerMmr, @playerRace, @playerRandom, @notes, @createdAt, @gameType);
                SELECT last_insert_rowid();",
                new
                {
                    sc2ProfileId,
                    mapId = game.Map?.Id,
                    gameLengthSeconds = replay.GameLengthSeconds,
                    replayPath = replay.ReplayPath,
                    replayTimestamp = replay.ReplayTimestamp,
                    win = (double)replay.Win,
                    playerName = replay.Player.Name,
                    playerClan = replay.Player.Clan,
                    playerMmr = replay.Player.Mmr,
                    gameType = (int?)game.GameType,
                    playerRace = replay.Player.Race,
                    playerRandom = replay.Player.Random ? 1 : 0,
                    notes = game.Notes,
                    createdAt = DateTimeOffset.UtcNow,
                });

            replay.Player.GamePlayerId = (int)conn.ExecuteScalar<long>(@"
                INSERT INTO GamePlayers (GameId, Side, SortOrder, Name, Clan, Mmr, Race, Random)
                VALUES (@gameId, @side, 0, @name, @clan, @mmr, @race, @random);
                SELECT last_insert_rowid();",
                new
                {
                    gameId = game.GameId,
                    side = SideSelf,
                    name = replay.Player.Name,
                    clan = replay.Player.Clan,
                    mmr = replay.Player.Mmr,
                    race = replay.Player.Race,
                    random = replay.Player.Random ? 1 : 0,
                });

            InsertGamePlayers(conn, game.GameId.Value, SideAlly, replay.Allies);
            InsertGamePlayers(conn, game.GameId.Value, SideOpponent, replay.Opponents);
        }

        // Inserted one row at a time (rather than Dapper's batched IEnumerable-params Execute) so each
        // player's generated GamePlayerId can be captured back onto it — every tracked player (not just
        // the session user) can have their own build selections, so every GamePlayer needs its own id.
        private static void InsertGamePlayers(SqliteConnection conn, int gameId, int side, GamePlayer[] players)
        {
            for (int i = 0; i < players.Length; i++)
            {
                GamePlayer player = players[i];
                player.GamePlayerId = (int)conn.ExecuteScalar<long>(@"
                    INSERT INTO GamePlayers (GameId, Side, SortOrder, Name, Clan, Mmr, Race, Random)
                    VALUES (@gameId, @side, @sortOrder, @name, @clan, @mmr, @race, @random);
                    SELECT last_insert_rowid();",
                    new
                    {
                        gameId,
                        side,
                        sortOrder = i,
                        name = player.Name,
                        clan = player.Clan,
                        mmr = player.Mmr,
                        race = player.Race,
                        random = player.Random ? 1 : 0,
                    });
            }
        }

        // Plain classes with settable properties, not positional records — Dapper's constructor-based
        // materialization requires constructor parameter types to exactly match the raw column types,
        // which bypasses both its numeric widening and our DateTimeOffsetTypeHandler. The property-setter
        // path it uses for a parameterless-constructible type applies both correctly.
        private class GameRow
        {
            public long Id { get; set; }
            public int Sc2ProfileId { get; set; }
            public int? MapId { get; set; }
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
            public GameType GameType { get; set; }
        }

        // Only the columns needed to identify a game's map. The full Map — including its attribute
        // values — is MapRepository's business; games just need something to display and group by.
        private class MapRow
        {
            public long Id { get; set; }
            public string Name { get; set; } = "";
        }

        private class GamePlayerRow
        {
            public long Id { get; set; }
            public long GameId { get; set; }
            public int Side { get; set; }
            public string Name { get; set; } = "";
            public string Clan { get; set; } = "";
            public long Mmr { get; set; }
            public long? MmrAfter { get; set; }
            public char Race { get; set; }
            public bool Random { get; set; }
        }

        private class GameBuildRow
        {
            public long GamePlayerId { get; set; }
            public int BuildId { get; set; }
        }

        private class GameAttributeValueRow
        {
            public long GamePlayerId { get; set; }
            public int BuildAttributeId { get; set; }
            public string Value { get; set; } = "";
        }

        internal List<GameData> GetGamesForProfile(int sc2ProfileId) => GetGamesForProfiles([sc2ProfileId]);

        // Loads and merges games across every given profile, ordered by when they were actually played
        // rather than by Id — each profile has its own independent Id sequence, so merging by Id would
        // interleave profiles inconsistently.
        internal List<GameData> GetGamesForProfiles(IReadOnlyCollection<int> sc2ProfileIds)
        {
            if (sc2ProfileIds.Count == 0)
                return [];

            using SqliteConnection conn = OpenConnection();

            List<GameRow> gameRows = conn.Query<GameRow>(@"
                SELECT Id, Sc2ProfileId, MapId, GameLengthSeconds, ReplayPath, ReplayTimestamp, Win, PlayerName, PlayerClan, PlayerMmr, PlayerRace, PlayerRandom, Notes, GameType
                FROM Games WHERE Sc2ProfileId IN @sc2ProfileIds ORDER BY ReplayTimestamp ASC",
                new { sc2ProfileIds }).ToList();

            if (gameRows.Count == 0)
                return [];

            string idList = string.Join(",", gameRows.Select(r => r.Id));

            // Fetched separately and shared by id rather than joined onto each game row, so every game on
            // the same map ends up holding the *same* Map instance — reference equality is what lets the
            // Maps tab and anything grouping by map line up without needing value equality on Map.
            Dictionary<int, Map> mapsById = new();
            List<int> mapIds = gameRows.Where(r => r.MapId != null).Select(r => r.MapId!.Value).Distinct().ToList();
            if (mapIds.Count > 0)
            {
                IEnumerable<MapRow> mapRows = conn.Query<MapRow>(
                    $"SELECT Id, Name FROM Maps WHERE Id IN ({string.Join(",", mapIds)})");
                foreach (MapRow row in mapRows)
                    mapsById[(int)row.Id] = new Map { Id = (int)row.Id, Name = row.Name };
            }

            Dictionary<long, List<GamePlayer>> allies = new();
            Dictionary<long, List<GamePlayer>> opponents = new();
            Dictionary<long, GamePlayer> selfPlayers = new(); // Games.Id -> self GamePlayer
            Dictionary<long, GamePlayer> playersById = new(); // GamePlayers.Id -> GamePlayer, every side
            IEnumerable<GamePlayerRow> playerRows = conn.Query<GamePlayerRow>(
                $"SELECT Id, GameId, Side, Name, Clan, Mmr, MmrAfter, Race, Random FROM GamePlayers WHERE GameId IN ({idList}) ORDER BY GameId, Side, SortOrder");
            foreach (GamePlayerRow row in playerRows)
            {
                GamePlayer player = new() { GamePlayerId = (int)row.Id, Name = row.Name, Clan = row.Clan, Mmr = row.Mmr, MmrAfter = row.MmrAfter, Race = row.Race, Random = row.Random };
                playersById[row.Id] = player;

                if (row.Side == SideSelf)
                {
                    selfPlayers[row.GameId] = player;
                    continue;
                }

                Dictionary<long, List<GamePlayer>> target = row.Side == SideAlly ? allies : opponents;
                if (!target.TryGetValue(row.GameId, out List<GamePlayer>? list))
                    target[row.GameId] = list = new();
                list.Add(player);
            }

            // Every tracked player (not just the session user) can have their own build selections, so
            // builds/attributes are fetched for every player loaded above, not just the self ones.
            if (playersById.Count > 0)
            {
                string playerIdList = string.Join(",", playersById.Keys);

                IEnumerable<GameBuildRow> buildRows = conn.Query<GameBuildRow>(
                    $"SELECT GamePlayerId, BuildId FROM GameBuilds WHERE GamePlayerId IN ({playerIdList}) ORDER BY GamePlayerId, SortOrder");
                foreach (GameBuildRow row in buildRows)
                    playersById[row.GamePlayerId].BuildIds.Add(row.BuildId);

                IEnumerable<GameAttributeValueRow> attributeRows = conn.Query<GameAttributeValueRow>(
                    $"SELECT GamePlayerId, BuildAttributeId, Value FROM GameAttributeValues WHERE GamePlayerId IN ({playerIdList})");
                foreach (GameAttributeValueRow row in attributeRows)
                    playersById[row.GamePlayerId].AttributeValues.Add(new GameAttributeValue { BuildAttributeId = row.BuildAttributeId, Value = row.Value });
            }

            List<GameData> games = new();
            foreach (GameRow row in gameRows)
            {
                // A missing Self row would mean the GamePlayers backfill in Initialize() somehow never
                // ran for this game — shouldn't happen, but fall back to reconstructing from the Games
                // row's own Player* columns rather than crashing.
                GamePlayer selfPlayer = selfPlayers.TryGetValue(row.Id, out GamePlayer? sp) ? sp : new GamePlayer
                {
                    Name = row.PlayerName,
                    Clan = row.PlayerClan,
                    Mmr = row.PlayerMmr,
                    Race = row.PlayerRace,
                    Random = row.PlayerRandom,
                };

                ParsedReplayData replay = new()
                {
                    GameLengthSeconds = row.GameLengthSeconds,
                    ReplayPath = row.ReplayPath,
                    ReplayTimestamp = row.ReplayTimestamp,
                    Win = row.Win,
                    Player = selfPlayer,
                    Allies = allies.TryGetValue(row.Id, out List<GamePlayer>? a) ? a.ToArray() : [],
                    Opponents = opponents.TryGetValue(row.Id, out List<GamePlayer>? o) ? o.ToArray() : [],
                };

                games.Add(new GameData
                {
                    GameId = (int)row.Id,
                    Sc2ProfileId = row.Sc2ProfileId,
                    Map = row.MapId != null && mapsById.TryGetValue(row.MapId.Value, out Map? map) ? map : null,
                    GameType = row.GameType,
                    ReplayData = replay,
                    Notes = row.Notes,
                });
            }
            return games;
        }

        public void UpdateGameBuilds(int gamePlayerId, IReadOnlyList<int> buildIds)
        {
            using SqliteConnection conn = OpenConnection();

            conn.Execute("DELETE FROM GameBuilds WHERE GamePlayerId = @gamePlayerId", new { gamePlayerId });

            if (buildIds.Count > 0)
            {
                conn.Execute(
                    "INSERT INTO GameBuilds (GamePlayerId, BuildId, SortOrder) VALUES (@gamePlayerId, @buildId, @sortOrder)",
                    buildIds.Select((buildId, i) => new { gamePlayerId, buildId, sortOrder = i }));
            }
        }

        public void UpdateGameNotes(int gameId, string notes)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("UPDATE Games SET Notes = @notes WHERE Id = @id", new { notes, id = gameId });
        }

        // Ranked vs Unranked can only be inferred, never read directly off the replay, so the user is
        // allowed to correct it by hand — this persists that override.
        public void UpdateGameType(int gameId, GameType? gameType)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("UPDATE Games SET GameType = @gameType WHERE Id = @id", new { gameType = (int?)gameType, id = gameId });
        }

        // Records the tracked player's post-game ladder MMR, observed from the Battle.net API after the
        // replay was imported (see ReplayImportService's polling).
        public void UpdateGamePlayerMmrAfter(int gamePlayerId, long mmrAfter)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("UPDATE GamePlayers SET MmrAfter = @mmrAfter WHERE Id = @id", new { mmrAfter, id = gamePlayerId });
        }

        // Cascades to GamePlayers (ON DELETE CASCADE), which in turn cascades to that game's
        // GameBuilds/GameAttributeValues rows for every player — self, allies, and opponents alike.
        public void DeleteGame(int gameId)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM Games WHERE Id = @id", new { id = gameId });
        }

        public void UpsertAttributeValue(int gamePlayerId, int buildAttributeId, string value)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute(@"
                INSERT INTO GameAttributeValues (GamePlayerId, BuildAttributeId, Value)
                VALUES (@gamePlayerId, @buildAttributeId, @value)
                ON CONFLICT(GamePlayerId, BuildAttributeId) DO UPDATE SET Value = @value",
                new { gamePlayerId, buildAttributeId, value });
        }

        public void DeleteAttributeValue(int gamePlayerId, int buildAttributeId)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM GameAttributeValues WHERE GamePlayerId = @gamePlayerId AND BuildAttributeId = @buildAttributeId",
                new { gamePlayerId, buildAttributeId });
        }

        // True if any GameBuilds row still points at one of these build node ids. Deleting a BuildNode
        // cascades to its whole subtree (BuildNodes.ParentId ON DELETE CASCADE), and each deleted node
        // cascades away any GameBuilds row referencing it (ON DELETE CASCADE) along with that player's
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

        // True if any game was played on this map. Unlike builds there's no subtree to collect — maps
        // never nest — so the Maps tab passes a single id. Games.MapId has no ON DELETE CASCADE, so
        // deleting a referenced map would leave dangling ids; the caller blocks the delete instead.
        public bool IsAnyMapReferenced(int mapId)
        {
            using SqliteConnection conn = OpenConnection();
            long count = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM Games WHERE MapId = @mapId", new { mapId });
            return count > 0;
        }
    }
}
