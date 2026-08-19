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
