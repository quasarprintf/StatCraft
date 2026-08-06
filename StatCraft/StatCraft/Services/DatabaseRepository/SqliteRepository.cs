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
    }
}
