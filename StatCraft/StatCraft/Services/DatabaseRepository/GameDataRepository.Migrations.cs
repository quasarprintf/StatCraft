using System;
using Dapper;
using Microsoft.Data.Sqlite;

namespace StatCraft.Services.DatabaseRepository
{
    public partial class GameDataRepository
    {
        public void Initialize()
        {
            EnsureDatabaseFolderExists();

            using SqliteConnection conn = OpenConnection();
            CreateTables(conn);

            RunMigrations(conn, nameof(GameDataRepository), Migrations);
        }

        // Each migration below guards itself with the sentinel-first-statement idiom: its opening
        // statement only succeeds against a database that still has the old shape, so it's a no-op
        // everywhere else — including on a database RunMigrations doesn't yet have a recorded version
        // for, which is exactly the case the first time this ledger runs against an already-evolved
        // real database. Several migrations depend on an earlier one having already run (noted on
        // each), so this order is load-bearing.
        private static readonly Action<SqliteConnection>[] Migrations =
        [
            MigrateReplayTimestampColumn,
            MigrateMmrAfterColumn,
            MigrateGameTypeColumn,
            BackfillSelfGamePlayers,
            MigrateBuildTrackingToGamePlayerId,
            MigrateGamesBuildIdToGameBuilds,
            MigrateGamesMapNameToMapId,
        ];

        private static void CreateTables(SqliteConnection conn)
        {
            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS Games (
                    Id                INTEGER PRIMARY KEY AUTOINCREMENT,
                    Sc2ProfileId      INTEGER NOT NULL REFERENCES Sc2Profiles(Id) ON DELETE CASCADE,
                    MapId             INTEGER REFERENCES Maps(Id),
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
                    CreatedAtUtc      TEXT    NOT NULL DEFAULT '',
                    GameType          INTEGER
                );
                CREATE TABLE IF NOT EXISTS GamePlayers (
                    Id        INTEGER PRIMARY KEY AUTOINCREMENT,
                    GameId    INTEGER NOT NULL REFERENCES Games(Id) ON DELETE CASCADE,
                    Side      INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    Name      TEXT    NOT NULL DEFAULT '',
                    Clan      TEXT    NOT NULL DEFAULT '',
                    Mmr       INTEGER NOT NULL DEFAULT 0,
                    MmrAfter  INTEGER,
                    Race      TEXT    NOT NULL DEFAULT '',
                    Random    INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS GameBuilds (
                    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    GamePlayerId INTEGER NOT NULL REFERENCES GamePlayers(Id) ON DELETE CASCADE,
                    BuildId      INTEGER NOT NULL REFERENCES BuildNodes(Id) ON DELETE CASCADE,
                    SortOrder    INTEGER NOT NULL DEFAULT 0,
                    UNIQUE(GamePlayerId, BuildId)
                );
                CREATE TABLE IF NOT EXISTS GameAttributeValues (
                    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    GamePlayerId     INTEGER NOT NULL REFERENCES GamePlayers(Id) ON DELETE CASCADE,
                    BuildAttributeId INTEGER NOT NULL REFERENCES BuildAttributes(Id) ON DELETE CASCADE,
                    Value            TEXT    NOT NULL DEFAULT '',
                    UNIQUE(GamePlayerId, BuildAttributeId)
                );");
        }

        // Upgrades a pre-existing DB that predates ReplayTimestamp. Backfills it from CreatedAtUtc
        // (the closest thing that existed before — when we recorded the game, not when the replay
        // file itself was last written, but a reasonable stand-in for old rows) rather than leaving it
        // blank. On a fresh DB CreateTables already added the column, so ADD COLUMN fails immediately
        // and the whole batch aborts before ever reaching the backfill.
        private static void MigrateReplayTimestampColumn(SqliteConnection conn)
        {
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
        }

        // Upgrades a pre-existing DB that predates post-game MMR tracking. Deliberately left NULL
        // rather than backfilled: MMR after a game can only be observed shortly after it's played, so
        // historical rows genuinely have no value to recover, and NULL is already how "unknown" is
        // represented everywhere downstream.
        private static void MigrateMmrAfterColumn(SqliteConnection conn)
        {
            try
            {
                conn.Execute("ALTER TABLE GamePlayers ADD COLUMN MmrAfter INTEGER;");
            }
            catch (SqliteException)
            {
                // Already migrated, or a fresh DB already created with the new schema.
            }
        }

        // Upgrades a pre-existing DB that predates game-type detection. Left NULL rather than
        // guessed: the type is derived from replay flags that were never stored, so existing rows
        // have nothing to reclassify from.
        private static void MigrateGameTypeColumn(SqliteConnection conn)
        {
            try
            {
                conn.Execute("ALTER TABLE Games ADD COLUMN GameType INTEGER;");
            }
            catch (SqliteException)
            {
                // Already migrated, or a fresh DB already created with the new schema.
            }
        }

        // Every game must always have exactly one "Self" GamePlayers row (Side = SideSelf) representing
        // the tracked user themselves, synthesized from the Player* columns already stored on Games —
        // GameBuilds/GameAttributeValues reference it rather than Games.Id directly (see
        // MigrateBuildTrackingToGamePlayerId, which requires this to have already run). Idempotent via
        // its own NOT EXISTS guard rather than the sentinel idiom (a fresh DB just has no Games rows
        // yet either way) — but now that it's version-gated it only actually runs once regardless.
        private static void BackfillSelfGamePlayers(SqliteConnection conn)
        {
            conn.Execute($@"
                INSERT INTO GamePlayers (GameId, Side, SortOrder, Name, Clan, Mmr, Race, Random)
                    SELECT g.Id, {SideSelf}, 0, g.PlayerName, g.PlayerClan, g.PlayerMmr, g.PlayerRace, g.PlayerRandom
                    FROM Games g
                    WHERE NOT EXISTS (SELECT 1 FROM GamePlayers gp WHERE gp.GameId = g.Id AND gp.Side = {SideSelf});");
        }

        // Upgrades a pre-existing DB where GameBuilds/GameAttributeValues were tied directly to
        // Games.Id. Build/attribute tracking is inherently about the tracked player's own performance
        // in a game, not the game as a whole, so they're retargeted to that player's own GamePlayers
        // row instead (backfilled by BackfillSelfGamePlayers, called just before this). SQLite can't
        // drop a column that's part of a UNIQUE constraint (GameId was part of UNIQUE(GameId, BuildId)
        // etc.), so the two tables are rebuilt from scratch rather than altered in place; the whole
        // rebuild runs in one transaction so a failure partway through can't leave the schema
        // half-migrated. On a fresh DB (or one already migrated) GameBuilds already has a GamePlayerId
        // column, so the first ADD COLUMN fails immediately and the whole batch — including the
        // transaction — is rolled back and aborted before ever reaching the destructive
        // table-recreation statements below. Must run before MigrateGamesBuildIdToGameBuilds, which
        // assumes GameBuilds is already GamePlayerId-keyed.
        private static void MigrateBuildTrackingToGamePlayerId(SqliteConnection conn)
        {
            try
            {
                using SqliteTransaction tx = conn.BeginTransaction();

                conn.Execute("ALTER TABLE GameBuilds ADD COLUMN GamePlayerId INTEGER REFERENCES GamePlayers(Id) ON DELETE CASCADE", transaction: tx);

                conn.Execute($@"
                    UPDATE GameBuilds SET GamePlayerId = (
                        SELECT gp.Id FROM GamePlayers gp WHERE gp.GameId = GameBuilds.GameId AND gp.Side = {SideSelf});

                    ALTER TABLE GameAttributeValues ADD COLUMN GamePlayerId INTEGER REFERENCES GamePlayers(Id) ON DELETE CASCADE;
                    UPDATE GameAttributeValues SET GamePlayerId = (
                        SELECT gp.Id FROM GamePlayers gp WHERE gp.GameId = GameAttributeValues.GameId AND gp.Side = {SideSelf});

                    CREATE TABLE GameBuilds_New (
                        Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                        GamePlayerId INTEGER NOT NULL REFERENCES GamePlayers(Id) ON DELETE CASCADE,
                        BuildId      INTEGER NOT NULL REFERENCES BuildNodes(Id) ON DELETE CASCADE,
                        SortOrder    INTEGER NOT NULL DEFAULT 0,
                        UNIQUE(GamePlayerId, BuildId)
                    );
                    INSERT INTO GameBuilds_New (Id, GamePlayerId, BuildId, SortOrder)
                        SELECT Id, GamePlayerId, BuildId, SortOrder FROM GameBuilds;
                    DROP TABLE GameBuilds;
                    ALTER TABLE GameBuilds_New RENAME TO GameBuilds;

                    CREATE TABLE GameAttributeValues_New (
                        Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                        GamePlayerId     INTEGER NOT NULL REFERENCES GamePlayers(Id) ON DELETE CASCADE,
                        BuildAttributeId INTEGER NOT NULL REFERENCES BuildAttributes(Id) ON DELETE CASCADE,
                        Value            TEXT    NOT NULL DEFAULT '',
                        UNIQUE(GamePlayerId, BuildAttributeId)
                    );
                    INSERT INTO GameAttributeValues_New (Id, GamePlayerId, BuildAttributeId, Value)
                        SELECT Id, GamePlayerId, BuildAttributeId, Value FROM GameAttributeValues;
                    DROP TABLE GameAttributeValues;
                    ALTER TABLE GameAttributeValues_New RENAME TO GameAttributeValues;",
                    transaction: tx);

                tx.Commit();
            }
            catch (SqliteException)
            {
                // Already migrated, or a fresh DB already created with the new schema.
            }
        }

        // Upgrades a pre-existing DB that predates GameBuilds entirely, moving its single Games.BuildId
        // column into GameBuilds (already GamePlayerId-keyed by this point, whether freshly created
        // above or migrated by MigrateBuildTrackingToGamePlayerId) before dropping the column —
        // resolving each game's GamePlayerId via the Self row BackfillSelfGamePlayers already added. On
        // a fresh DB (or one already migrated) Games never has a BuildId column, so the INSERT fails
        // immediately and the batch aborts before ever reaching DROP COLUMN.
        private static void MigrateGamesBuildIdToGameBuilds(SqliteConnection conn)
        {
            try
            {
                conn.Execute($@"
                    INSERT INTO GameBuilds (GamePlayerId, BuildId, SortOrder)
                        SELECT (SELECT gp.Id FROM GamePlayers gp WHERE gp.GameId = Games.Id AND gp.Side = {SideSelf}), BuildId, 0
                        FROM Games WHERE BuildId IS NOT NULL;
                    ALTER TABLE Games DROP COLUMN BuildId;");
            }
            catch (SqliteException)
            {
                // Already migrated, or a fresh DB already created with the new schema.
            }
        }

        // Upgrades a pre-existing DB that stored the map as a bare name on Games, creating one Maps
        // row per distinct historical name and repointing each game at it. Blank legacy names (the
        // old column's DEFAULT '') are deliberately left as a NULL MapId rather than becoming a map
        // called "". On a fresh DB (or one already migrated) Games already has a MapId column, so the
        // ADD COLUMN fails immediately and the batch aborts before reaching DROP COLUMN. (Adding a
        // column with a REFERENCES clause is only legal under PRAGMA foreign_keys because its default
        // is NULL, which is exactly what an unmigrated row should have anyway.)
        //
        // Requires the Maps table to already exist — see MapRepository, which App.axaml.cs and the
        // tests both initialize before this repository for exactly that reason.
        private static void MigrateGamesMapNameToMapId(SqliteConnection conn)
        {
            try
            {
                conn.Execute(@"
                    ALTER TABLE Games ADD COLUMN MapId INTEGER REFERENCES Maps(Id);
                    INSERT OR IGNORE INTO Maps (Name) SELECT DISTINCT MapName FROM Games WHERE MapName <> '';
                    UPDATE Games SET MapId = (SELECT Id FROM Maps WHERE Maps.Name = Games.MapName) WHERE MapName <> '';
                    ALTER TABLE Games DROP COLUMN MapName;");
            }
            catch (SqliteException)
            {
                // Already migrated, or a fresh DB already created with the new schema.
            }
        }
    }
}
