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

        // Append future schema changes here as named methods, in order; RunMigrations tracks how many
        // of them a given database has already applied.
        private static readonly Action<SqliteConnection>[] Migrations =
        [
            // Placeholder for a migration that briefly existed here during development and was removed
            // again before ever shipping — but not before it ran against at least one real database,
            // which recorded "1 migration applied" in SchemaVersion. Without this slot, that database's
            // ledger would already read >= the length of this array and skip every real migration below
            // forever; a fresh database just runs this no-op once and moves on.
            _ => { },

            // Captures each player's actual in-game color (from the replay's own Details record) so the
            // Data tab's per-player build tabs can be colored by who they really are rather than just
            // ally/opponent side. Existing rows get NULL and are backfilled lazily — see
            // ReplayDataExtractor.TryResolvePlayerColorAsync — rather than needing every past replay
            // re-imported.
            conn => conn.Execute("ALTER TABLE GamePlayers ADD COLUMN Color INTEGER"),
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
                    GameType          INTEGER NOT NULL
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
    }
}
