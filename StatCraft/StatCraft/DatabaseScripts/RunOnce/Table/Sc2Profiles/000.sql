CREATE TABLE IF NOT EXISTS Sc2Profiles (
    Id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    BattleNetAccountId INTEGER NOT NULL REFERENCES BattleNetAccounts(Id) ON DELETE CASCADE,
    RegionId           TEXT    NOT NULL,
    RealmId            TEXT    NOT NULL,
    ProfileId          INTEGER NOT NULL,
    Name               TEXT    NOT NULL,
    UNIQUE(BattleNetAccountId, RegionId, RealmId, ProfileId)
);
