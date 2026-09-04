-- This table's actual contents have always been BuildDetail-scoped values recorded per GamePlayer
-- (GamePlayerId, BuildAttributeId -> BuildDetailsAttributes), not Game-scoped ones, so BuildDetailValues
-- is the accurate name. Renaming it frees "GameAttributeValues" for a new table below storing genuinely
-- Game-scoped attribute values, matching MapAttributeValues/BuildAttributeValues' shape for the other two
-- scopes. Nothing else has a "REFERENCES GameAttributeValues(...)" clause to worry about being swept up
-- (or not) by SQLite's auto-FK-update-on-rename, so no ordering hazard here like BuildAttributes' rename
-- had — this can safely live in its own table's folder.
ALTER TABLE GameAttributeValues RENAME TO BuildDetailValues;

CREATE TABLE GameAttributeValues (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    GameId      INTEGER NOT NULL REFERENCES Games(Id) ON DELETE CASCADE,
    AttributeId INTEGER NOT NULL REFERENCES AttributeDefinitions(Id) ON DELETE CASCADE,
    Value       TEXT    NOT NULL DEFAULT '',
    UNIQUE(GameId, AttributeId)
);
