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
                    CreatedAtUtc          TEXT    NOT NULL DEFAULT '',
                    Sc2RegionId           TEXT    NOT NULL DEFAULT '',
                    Sc2RealmId            TEXT    NOT NULL DEFAULT '',
                    Sc2ProfileId          TEXT    NOT NULL DEFAULT '',
                    Sc2ProfileName        TEXT    NOT NULL DEFAULT ''
                );
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key   TEXT PRIMARY KEY,
                    Value TEXT NOT NULL DEFAULT ''
                );";
            cmd.ExecuteNonQuery();

            foreach (var column in new[] { "Sc2RegionId", "Sc2RealmId", "Sc2ProfileId", "Sc2ProfileName" })
            {
                try
                {
                    using var migrateCmd = conn.CreateCommand();
                    migrateCmd.CommandText = $"ALTER TABLE BattleNetAccounts ADD COLUMN {column} TEXT NOT NULL DEFAULT ''";
                    migrateCmd.ExecuteNonQuery();
                }
                catch (SqliteException)
                {
                    // Column already exists.
                }
            }
        }

        private const string AccountColumns = "Id, BattleTag, AccountSub, EncryptedAccessToken, EncryptedRefreshToken, TokenExpiresAtUtc, CreatedAtUtc, Sc2RegionId, Sc2RealmId, Sc2ProfileId, Sc2ProfileName";

        public List<BattleNetAccount> GetAllAccounts()
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {AccountColumns} FROM BattleNetAccounts ORDER BY CreatedAtUtc";
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
            cmd.CommandText = $"SELECT {AccountColumns} FROM BattleNetAccounts WHERE AccountSub = @accountSub";
            cmd.Parameters.AddWithValue("@accountSub", accountSub);
            using var reader = cmd.ExecuteReader();
            return reader.Read() ? ReadAccount(reader) : null;
        }

        public void InsertAccount(BattleNetAccount account)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO BattleNetAccounts (BattleTag, AccountSub, EncryptedAccessToken, EncryptedRefreshToken, TokenExpiresAtUtc, CreatedAtUtc, Sc2RegionId, Sc2RealmId, Sc2ProfileId, Sc2ProfileName)
                VALUES (@battleTag, @accountSub, @accessToken, @refreshToken, @expiresAt, @createdAt, @sc2RegionId, @sc2RealmId, @sc2ProfileId, @sc2ProfileName);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@battleTag", account.BattleTag);
            cmd.Parameters.AddWithValue("@accountSub", account.AccountSub);
            cmd.Parameters.AddWithValue("@accessToken", account.EncryptedAccessToken);
            cmd.Parameters.AddWithValue("@refreshToken", (object?)account.EncryptedRefreshToken ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@expiresAt", account.TokenExpiresAtUtc.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@createdAt", account.CreatedAtUtc.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@sc2RegionId", account.Sc2RegionId);
            cmd.Parameters.AddWithValue("@sc2RealmId", account.Sc2RealmId);
            cmd.Parameters.AddWithValue("@sc2ProfileId", account.Sc2ProfileId);
            cmd.Parameters.AddWithValue("@sc2ProfileName", account.Sc2ProfileName);
            account.Id = (int)(long)cmd.ExecuteScalar()!;
        }

        public void UpdateAccountTokens(int id, byte[] encryptedAccessToken, byte[]? encryptedRefreshToken, DateTimeOffset expiresAtUtc, string battleTag, string sc2RegionId, string sc2RealmId, string sc2ProfileId, string sc2ProfileName)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE BattleNetAccounts
                SET BattleTag = @battleTag, EncryptedAccessToken = @accessToken, EncryptedRefreshToken = @refreshToken, TokenExpiresAtUtc = @expiresAt,
                    Sc2RegionId = @sc2RegionId, Sc2RealmId = @sc2RealmId, Sc2ProfileId = @sc2ProfileId, Sc2ProfileName = @sc2ProfileName
                WHERE Id = @id";
            cmd.Parameters.AddWithValue("@battleTag", battleTag);
            cmd.Parameters.AddWithValue("@accessToken", encryptedAccessToken);
            cmd.Parameters.AddWithValue("@refreshToken", (object?)encryptedRefreshToken ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@expiresAt", expiresAtUtc.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@sc2RegionId", sc2RegionId);
            cmd.Parameters.AddWithValue("@sc2RealmId", sc2RealmId);
            cmd.Parameters.AddWithValue("@sc2ProfileId", sc2ProfileId);
            cmd.Parameters.AddWithValue("@sc2ProfileName", sc2ProfileName);
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
            Sc2RegionId = reader.GetString(7),
            Sc2RealmId = reader.GetString(8),
            Sc2ProfileId = reader.GetString(9),
            Sc2ProfileName = reader.GetString(10),
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
