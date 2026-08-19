CREATE TABLE IF NOT EXISTS MapAttributeValueOptions (
    Id             INTEGER PRIMARY KEY AUTOINCREMENT,
    MapAttributeId INTEGER NOT NULL REFERENCES MapAttributes(Id) ON DELETE CASCADE,
    Value          TEXT    NOT NULL,
    SortOrder      INTEGER NOT NULL DEFAULT 0
);
