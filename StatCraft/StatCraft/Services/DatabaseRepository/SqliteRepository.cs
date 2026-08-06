using System;
using System.Collections.Generic;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;

namespace StatCraft.Services.DatabaseRepository
{
    // Shared connection/lifecycle plumbing for every SQLite-backed repository: registering Dapper's
    // custom type handlers once, building the connection string, ensuring the database's containing
    // folder exists, and opening a connection with foreign keys turned on (SQLite defaults this off per
    // connection, so every connection has to ask for it itself). Each subclass still owns its own
    // Initialize() — its tables and migrations are specific to it — this only removes what was identical
    // setup around them.
    public abstract class SqliteRepository
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        protected SqliteRepository(string dbPath)
        {
            DapperTypeHandlers.EnsureRegistered();
            _dbPath = dbPath;
            _connectionString = $"Data Source={dbPath}";
        }

        // Called at the top of each subclass's Initialize(), before its first OpenConnection() — SQLite
        // won't create the file's directory for you.
        protected void EnsureDatabaseFolderExists()
        {
            string? dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }

        protected SqliteConnection OpenConnection()
        {
            SqliteConnection conn = new SqliteConnection(_connectionString);
            conn.Open();
            conn.Execute("PRAGMA foreign_keys = ON");
            return conn;
        }

        // Runs whichever of the given migrations this component (identified by name, since every
        // repository shares one physical database file) hasn't applied yet, in order, recording
        // progress as it goes. Each migration is still expected to guard itself against being invoked
        // when its work is already done some other way — typically the sentinel-first-statement idiom,
        // where a migration's opening statement only succeeds against the schema shape it expects to
        // find, so it's a no-op everywhere else. That self-guarding is what makes it safe to introduce
        // this version ledger onto a database whose actual history predates the ledger's own existence:
        // "no recorded version yet" doesn't mean "nothing has ever been migrated". Once a component's
        // version catches up, later Initialize() calls skip straight past its already-applied migrations
        // instead of re-attempting (and catching the failure of) every one of them on every launch.
        protected void RunMigrations(SqliteConnection conn, string component, IReadOnlyList<Action<SqliteConnection>> migrations)
        {
            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS SchemaVersion (
                    Component TEXT PRIMARY KEY,
                    Version   INTEGER NOT NULL DEFAULT 0
                );");

            int version = conn.ExecuteScalar<int?>(
                "SELECT Version FROM SchemaVersion WHERE Component = @component", new { component }) ?? 0;

            for (; version < migrations.Count; version++)
            {
                migrations[version](conn);
                conn.Execute(@"
                    INSERT INTO SchemaVersion (Component, Version) VALUES (@component, @version)
                    ON CONFLICT(Component) DO UPDATE SET Version = @version",
                    new { component, version = version + 1 });
            }
        }
    }
}
