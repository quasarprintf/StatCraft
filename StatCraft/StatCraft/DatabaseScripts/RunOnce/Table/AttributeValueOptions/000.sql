CREATE TABLE IF NOT EXISTS AttributeValueOptions (
    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
    BuildAttributeId INTEGER NOT NULL REFERENCES BuildAttributes(Id) ON DELETE CASCADE,
    Value            TEXT    NOT NULL,
    SortOrder        INTEGER NOT NULL DEFAULT 0
);
