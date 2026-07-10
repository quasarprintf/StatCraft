using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using StatCraft.Models;

namespace StatCraft.Services
{
    public class AccountRepository
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public AccountRepository(string dbPath)
        {
            _dbPath = dbPath;
            _connectionString = $"Data Source={dbPath}";
        }

        public void Initialize()
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS BattleNetAccounts (
                    Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                    BattleTag             TEXT    NOT NULL,
                    AccountSub            TEXT    NOT NULL DEFAULT '',
                    EncryptedAccessToken  BLOB    NOT NULL,
                    EncryptedRefreshToken BLOB,
                    TokenExpiresAtUtc     TEXT    NOT NULL DEFAULT '',
                    CreatedAtUtc          TEXT    NOT NULL DEFAULT ''
                );
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key   TEXT PRIMARY KEY,
                    Value TEXT NOT NULL DEFAULT ''
                );";
            cmd.ExecuteNonQuery();
        }

        public List<BattleNetAccount> GetAllAccounts()
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, BattleTag, AccountSub, EncryptedAccessToken, EncryptedRefreshToken, TokenExpiresAtUtc, CreatedAtUtc FROM BattleNetAccounts ORDER BY CreatedAtUtc";
            using var reader = cmd.ExecuteReader();

            var accounts = new List<BattleNetAccount>();
            while (reader.Read())
                accounts.Add(ReadAccount(reader));
            return accounts;
        }

        public BattleNetAccount? FindByAccountSub(string accountSub)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, BattleTag, AccountSub, EncryptedAccessToken, EncryptedRefreshToken, TokenExpiresAtUtc, CreatedAtUtc FROM BattleNetAccounts WHERE AccountSub = @accountSub";
            cmd.Parameters.AddWithValue("@accountSub", accountSub);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadAccount(reader) : null;
        }

        public void InsertAccount(BattleNetAccount account)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO BattleNetAccounts (BattleTag, AccountSub, EncryptedAccessToken, EncryptedRefreshToken, TokenExpiresAtUtc, CreatedAtUtc)
                VALUES (@battleTag, @accountSub, @accessToken, @refreshToken, @expiresAt, @createdAt);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@battleTag", account.BattleTag);
            cmd.Parameters.AddWithValue("@accountSub", account.AccountSub);
            cmd.Parameters.AddWithValue("@accessToken", account.EncryptedAccessToken);
            cmd.Parameters.AddWithValue("@refreshToken", (object?)account.EncryptedRefreshToken ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@expiresAt", account.TokenExpiresAtUtc.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@createdAt", account.CreatedAtUtc.ToString("o", CultureInfo.InvariantCulture));
            account.Id = (int)(long)cmd.ExecuteScalar()!;
        }

        public void UpdateAccountTokens(int id, byte[] encryptedAccessToken, byte[]? encryptedRefreshToken, DateTimeOffset expiresAtUtc, string battleTag)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE BattleNetAccounts
                SET BattleTag = @battleTag, EncryptedAccessToken = @accessToken, EncryptedRefreshToken = @refreshToken, TokenExpiresAtUtc = @expiresAt
                WHERE Id = @id";
            cmd.Parameters.AddWithValue("@battleTag", battleTag);
            cmd.Parameters.AddWithValue("@accessToken", encryptedAccessToken);
            cmd.Parameters.AddWithValue("@refreshToken", (object?)encryptedRefreshToken ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@expiresAt", expiresAtUtc.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteAccount(int id)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM BattleNetAccounts WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public string? GetSetting(string key)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = @key";
            cmd.Parameters.AddWithValue("@key", key);
            return cmd.ExecuteScalar() as string;
        }

        public void SetSetting(string key, string value)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO AppSettings (Key, Value) VALUES (@key, @value) ON CONFLICT(Key) DO UPDATE SET Value = @value";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.ExecuteNonQuery();
        }

        private static BattleNetAccount ReadAccount(SqliteDataReader reader) => new()
        {
            Id = (int)reader.GetInt64(0),
            BattleTag = reader.GetString(1),
            AccountSub = reader.GetString(2),
            EncryptedAccessToken = (byte[])reader[3],
            EncryptedRefreshToken = reader.IsDBNull(4) ? null : (byte[])reader[4],
            TokenExpiresAtUtc = DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
        };

        private SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON";
            pragma.ExecuteNonQuery();
            return conn;
        }
    }
}
