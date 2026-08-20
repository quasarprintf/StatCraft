-- The real AttributeValueOptions table, for the new shared AttributeDefinitions table — created fresh
-- now that the old table of this name was just renamed out of the way in 001.sql.
CREATE TABLE IF NOT EXISTS AttributeValueOptions (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    AttributeId INTEGER NOT NULL REFERENCES AttributeDefinitions(Id) ON DELETE CASCADE,
    Value       TEXT    NOT NULL,
    SortOrder   INTEGER NOT NULL DEFAULT 0
);
