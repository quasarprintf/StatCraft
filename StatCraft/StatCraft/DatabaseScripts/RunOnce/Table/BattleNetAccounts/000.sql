CREATE TABLE IF NOT EXISTS BattleNetAccounts (
    Id                    INTEGER PRIMARY KEY AUTOINCREMENT,
    BattleTag             TEXT    NOT NULL,
    AccountSub            TEXT    NOT NULL DEFAULT '',
    EncryptedAccessToken  BLOB    NOT NULL,
    EncryptedRefreshToken BLOB,
    TokenExpiresAtUtc     TEXT    NOT NULL DEFAULT '',
    CreatedAtUtc          TEXT    NOT NULL DEFAULT ''
);
