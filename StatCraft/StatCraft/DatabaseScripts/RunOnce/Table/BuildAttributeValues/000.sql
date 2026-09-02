CREATE TABLE BuildAttributeValues (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    BuildId       INTEGER NOT NULL REFERENCES BuildNodes(Id) ON DELETE CASCADE,
    AttributeId INTEGER NOT NULL REFERENCES AttributeDefinitions(Id) ON DELETE CASCADE,
    Value       TEXT    NOT NULL DEFAULT '',
    UNIQUE(BuildId, AttributeId)
);