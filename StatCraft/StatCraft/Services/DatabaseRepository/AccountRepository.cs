using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using StatCraft.Models.Battlenet;

namespace StatCraft.Services.DatabaseRepository
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
            string? dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
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
                CREATE TABLE IF NOT EXISTS Sc2Profiles (
                    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                    BattleNetAccountId INTEGER NOT NULL REFERENCES BattleNetAccounts(Id) ON DELETE CASCADE,
                    RegionId           TEXT    NOT NULL,
                    RealmId            TEXT    NOT NULL,
                    ProfileId          TEXT    NOT NULL,
                    Name               TEXT    NOT NULL,
                    UNIQUE(BattleNetAccountId, RegionId, RealmId, ProfileId)
                );
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key   TEXT PRIMARY KEY,
                    Value TEXT NOT NULL DEFAULT ''
                );";
            cmd.ExecuteNonQuery();
        }

        private const string AccountColumns = "Id, BattleTag, AccountSub, EncryptedAccessToken, EncryptedRefreshToken, TokenExpiresAtUtc, CreatedAtUtc";

        public BattleNetAccount? FindByAccountSub(string accountSub)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {AccountColumns} FROM BattleNetAccounts WHERE AccountSub = @accountSub";
            cmd.Parameters.AddWithValue("@accountSub", accountSub);
            using SqliteDataReader reader = cmd.ExecuteReader();
            return reader.Read() ? ReadAccount(reader) : null;
        }

        public void InsertAccount(BattleNetAccount account)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
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
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
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
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM BattleNetAccounts WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public List<Sc2Profile> GetAllProfiles()
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT p.Id, p.BattleNetAccountId, p.RegionId, p.RealmId, p.ProfileId, p.Name, {PrefixColumns("a", AccountColumns)}
                FROM Sc2Profiles p
                JOIN BattleNetAccounts a ON a.Id = p.BattleNetAccountId
                ORDER BY p.Id";
            using SqliteDataReader reader = cmd.ExecuteReader();

            List<Sc2Profile> profiles = new List<Sc2Profile>();
            while (reader.Read())
            {
                BattleNetAccount account = ReadAccount(reader, 6);
                Sc2Profile profile = new Sc2Profile
                {
                    Id = (int)reader.GetInt64(0),
                    BattleNetAccountId = (int)reader.GetInt64(1),
                    RegionId = reader.GetString(2),
                    RealmId = reader.GetString(3),
                    ProfileId = reader.GetString(4),
                    Name = reader.GetString(5),
                    Account = account,
                };
                profiles.Add(profile);
            }
            return profiles;
        }

        public void UpsertProfile(Sc2Profile profile)
        {
            using SqliteConnection conn = OpenConnection();

            using (SqliteCommand upsertCmd = conn.CreateCommand())
            {
                upsertCmd.CommandText = @"
                    INSERT INTO Sc2Profiles (BattleNetAccountId, RegionId, RealmId, ProfileId, Name)
                    VALUES (@accountId, @regionId, @realmId, @profileId, @name)
                    ON CONFLICT(BattleNetAccountId, RegionId, RealmId, ProfileId) DO UPDATE SET Name = @name";
                upsertCmd.Parameters.AddWithValue("@accountId", profile.BattleNetAccountId);
                upsertCmd.Parameters.AddWithValue("@regionId", profile.RegionId);
                upsertCmd.Parameters.AddWithValue("@realmId", profile.RealmId);
                upsertCmd.Parameters.AddWithValue("@profileId", profile.ProfileId);
                upsertCmd.Parameters.AddWithValue("@name", profile.Name);
                upsertCmd.ExecuteNonQuery();
            }

            using SqliteCommand selectCmd = conn.CreateCommand();
            selectCmd.CommandText = @"
                SELECT Id FROM Sc2Profiles
                WHERE BattleNetAccountId = @accountId AND RegionId = @regionId AND RealmId = @realmId AND ProfileId = @profileId";
            selectCmd.Parameters.AddWithValue("@accountId", profile.BattleNetAccountId);
            selectCmd.Parameters.AddWithValue("@regionId", profile.RegionId);
            selectCmd.Parameters.AddWithValue("@realmId", profile.RealmId);
            selectCmd.Parameters.AddWithValue("@profileId", profile.ProfileId);
            profile.Id = (int)(long)selectCmd.ExecuteScalar()!;
        }

        public string? GetSetting(string key)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppSettings WHERE Key = @key";
            cmd.Parameters.AddWithValue("@key", key);
            return cmd.ExecuteScalar() as string;
        }

        public void SetSetting(string key, string value)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO AppSettings (Key, Value) VALUES (@key, @value) ON CONFLICT(Key) DO UPDATE SET Value = @value";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.ExecuteNonQuery();
        }

        private static string PrefixColumns(string alias, string columns)
        {
            string[] names = columns.Split(", ");
            for (int i = 0; i < names.Length; i++)
                names[i] = $"{alias}.{names[i]}";
            return string.Join(", ", names);
        }

        private static BattleNetAccount ReadAccount(SqliteDataReader reader, int offset = 0) => new()
        {
            Id = (int)reader.GetInt64(offset),
            BattleTag = reader.GetString(offset + 1),
            AccountSub = reader.GetString(offset + 2),
            EncryptedAccessToken = (byte[])reader[offset + 3],
            EncryptedRefreshToken = reader.IsDBNull(offset + 4) ? null : (byte[])reader[offset + 4],
            TokenExpiresAtUtc = DateTimeOffset.Parse(reader.GetString(offset + 5), CultureInfo.InvariantCulture),
            CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(offset + 6), CultureInfo.InvariantCulture),
        };

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
