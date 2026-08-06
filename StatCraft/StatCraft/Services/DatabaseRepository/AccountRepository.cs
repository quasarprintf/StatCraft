using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using StatCraft.Models.Battlenet;

namespace StatCraft.Services.DatabaseRepository
{
    public class AccountRepository : SqliteRepository
    {
        public AccountRepository(string dbPath) : base(dbPath)
        {
        }

        public void Initialize()
        {
            EnsureDatabaseFolderExists();

            using SqliteConnection conn = OpenConnection();
            conn.Execute(@"
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
                    ProfileId          INTEGER NOT NULL,
                    Name               TEXT    NOT NULL,
                    UNIQUE(BattleNetAccountId, RegionId, RealmId, ProfileId)
                );
                CREATE TABLE IF NOT EXISTS AppSettings (
                    Key   TEXT PRIMARY KEY,
                    Value TEXT NOT NULL DEFAULT ''
                );");
        }

        private const string AccountColumns = "Id, BattleTag, AccountSub, EncryptedAccessToken, EncryptedRefreshToken, TokenExpiresAtUtc, CreatedAtUtc";

        public BattleNetAccount? FindByAccountSub(string accountSub)
        {
            using SqliteConnection conn = OpenConnection();
            return conn.QueryFirstOrDefault<BattleNetAccount>(
                $"SELECT {AccountColumns} FROM BattleNetAccounts WHERE AccountSub = @accountSub",
                new { accountSub });
        }

        public void InsertAccount(BattleNetAccount account)
        {
            using SqliteConnection conn = OpenConnection();
            account.Id = (int)conn.ExecuteScalar<long>(@"
                INSERT INTO BattleNetAccounts (BattleTag, AccountSub, EncryptedAccessToken, EncryptedRefreshToken, TokenExpiresAtUtc, CreatedAtUtc)
                VALUES (@battleTag, @accountSub, @accessToken, @refreshToken, @expiresAt, @createdAt);
                SELECT last_insert_rowid();",
                new
                {
                    battleTag = account.BattleTag,
                    accountSub = account.AccountSub,
                    accessToken = account.EncryptedAccessToken,
                    refreshToken = account.EncryptedRefreshToken,
                    expiresAt = account.TokenExpiresAtUtc,
                    createdAt = account.CreatedAtUtc,
                });
        }

        public void UpdateAccountTokens(int id, byte[] encryptedAccessToken, byte[]? encryptedRefreshToken, DateTimeOffset expiresAtUtc, string battleTag)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute(@"
                UPDATE BattleNetAccounts
                SET BattleTag = @battleTag, EncryptedAccessToken = @accessToken, EncryptedRefreshToken = @refreshToken, TokenExpiresAtUtc = @expiresAt
                WHERE Id = @id",
                new { battleTag, accessToken = encryptedAccessToken, refreshToken = encryptedRefreshToken, expiresAt = expiresAtUtc, id });
        }

        public void DeleteAccount(int id)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM BattleNetAccounts WHERE Id = @id", new { id });
        }

        public List<Sc2Profile> GetAllProfiles()
        {
            using SqliteConnection conn = OpenConnection();
            IEnumerable<Sc2Profile> profiles = conn.Query<Sc2Profile, BattleNetAccount, Sc2Profile>(
                $@"SELECT p.Id, p.BattleNetAccountId, p.RegionId, p.RealmId, p.ProfileId, p.Name, {PrefixColumns("a", AccountColumns)}
                   FROM Sc2Profiles p
                   JOIN BattleNetAccounts a ON a.Id = p.BattleNetAccountId
                   ORDER BY p.Id",
                (profile, account) => { profile.Account = account; return profile; },
                splitOn: "Id");
            return profiles.ToList();
        }

        public void UpsertProfile(Sc2Profile profile)
        {
            using SqliteConnection conn = OpenConnection();

            conn.Execute(@"
                INSERT INTO Sc2Profiles (BattleNetAccountId, RegionId, RealmId, ProfileId, Name)
                VALUES (@accountId, @regionId, @realmId, @profileId, @name)
                ON CONFLICT(BattleNetAccountId, RegionId, RealmId, ProfileId) DO UPDATE SET Name = @name",
                new { accountId = profile.BattleNetAccountId, regionId = profile.RegionId, realmId = profile.RealmId, profileId = profile.ProfileId, name = profile.Name });

            profile.Id = (int)conn.ExecuteScalar<long>(@"
                SELECT Id FROM Sc2Profiles
                WHERE BattleNetAccountId = @accountId AND RegionId = @regionId AND RealmId = @realmId AND ProfileId = @profileId",
                new { accountId = profile.BattleNetAccountId, regionId = profile.RegionId, realmId = profile.RealmId, profileId = profile.ProfileId });
        }

        public string? GetSetting(string key)
        {
            using SqliteConnection conn = OpenConnection();
            return conn.ExecuteScalar<string?>("SELECT Value FROM AppSettings WHERE Key = @key", new { key });
        }

        public void SetSetting(string key, string value)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("INSERT INTO AppSettings (Key, Value) VALUES (@key, @value) ON CONFLICT(Key) DO UPDATE SET Value = @value", new { key, value });
        }

        private static string PrefixColumns(string alias, string columns)
        {
            string[] names = columns.Split(", ");
            for (int i = 0; i < names.Length; i++)
                names[i] = $"{alias}.{names[i]}";
            return string.Join(", ", names);
        }
    }
}
