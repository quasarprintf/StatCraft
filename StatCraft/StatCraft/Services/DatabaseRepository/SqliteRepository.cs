using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using StatCraft.Services.BackgroundService;

namespace StatCraft.Services.DatabaseRepository
{
    // Shared connection/lifecycle plumbing for every SQLite-backed repository: registering Dapper's
    // custom type handlers once, building the connection string, applying every schema script (via
    // DatabaseMigrator) on Initialize(), and opening a connection with foreign keys turned on (SQLite
    // defaults this off per connection, so every connection has to ask for it itself).
    public abstract class SqliteRepository
    {
        private readonly string _dbPath;
        private readonly string _connectionString;
        private readonly ILogger? _logger;

        protected SqliteRepository(string dbPath, ILogger? logger = null)
        {
            DapperTypeHandlers.EnsureRegistered();
            _dbPath = dbPath;
            _logger = logger;
            _connectionString = $"Data Source={dbPath}";
        }

        // Every repository shares one physical database file, so any one of them running this fully
        // migrates it — which repository happens to be resolved/Initialize()'d first doesn't matter.
        public void Initialize()
        {
            EnsureDatabaseFolderExists();
            DatabaseMigrator.Migrate(_dbPath, _logger);
        }

        // SQLite won't create the file's directory for you.
        private void EnsureDatabaseFolderExists()
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
    }
}
