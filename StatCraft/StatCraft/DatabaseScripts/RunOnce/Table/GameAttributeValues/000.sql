CREATE TABLE IF NOT EXISTS GameAttributeValues (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    GamePlayerId     INTEGER NOT NULL REFERENCES GamePlayers(Id) ON DELETE CASCADE,
    BuildAttributeId INTEGER NOT NULL REFERENCES BuildAttributes(Id) ON DELETE CASCADE,
    Value            TEXT    NOT NULL DEFAULT '',
    UNIQUE(GamePlayerId, BuildAttributeId)
);
