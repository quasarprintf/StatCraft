CREATE TABLE IF NOT EXISTS AttributeDefinitions (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    Name         TEXT    NOT NULL DEFAULT '',
    Type         INTEGER NOT NULL DEFAULT 0,
    Scope        INTEGER NOT NULL DEFAULT 0,
    DefaultValue TEXT    NOT NULL DEFAULT '',
    SortOrder    INTEGER NOT NULL DEFAULT 0
);
