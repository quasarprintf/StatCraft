-- Migrates every existing MapAttributes/MapAttributeValueOptions row into the new shared
-- AttributeDefinitions/AttributeValueOptions tables, then drops the old ones. Lives here rather than
-- under MapAttributes/ because the rebuilt MapAttributeValues below needs an actual (not just
-- schema-declared) Maps table to already exist: SQLite only validates a FOREIGN KEY's target at DML
-- time, not at CREATE TABLE time, but that means an INSERT into a column with a "REFERENCES Maps(Id)"
-- constraint genuinely needs Maps to exist, or it fails with "no such table: main.Maps" even though the
-- CREATE TABLE that declared the constraint succeeds fine. "MapAttributes" sorts alphabetically before
-- "Maps" under DbUp's script-name comparer, so a script living under MapAttributes/ would always run
-- before Maps/000.sql ever creates that table on a fresh database. Placing it here, one number after
-- Maps/000.sql, is the first name guaranteed to sort after every table this migration touches
-- (AttributeDefinitions, AttributeValueOptions, MapAttributeValueOptions, MapAttributeValues,
-- MapAttributes, and Maps itself).

-- Every existing MapAttributes row becomes an AttributeDefinitions row, Id preserved explicitly so
-- MapAttributeValueOptions/MapAttributeValues' MapAttributeId values keep pointing at the right row
-- with no remapping needed anywhere else in this script. AttributeDefinitions starts genuinely empty
-- (nothing produces AttributeScope.Game/Build rows anywhere today), so there's no risk of an Id
-- collision with anything already inserted. DefaultValue has no Map-attribute equivalent today, so it
-- backfills to ''.
INSERT INTO AttributeDefinitions (Id, Name, Type, Scope, DefaultValue, SortOrder)
SELECT Id, Name, Type, Scope, '', SortOrder FROM MapAttributes;

-- Same idea: AttributeId = the old MapAttributeId, numerically identical to the AttributeDefinitions.Id
-- just inserted above, so no remapping is needed here either.
INSERT INTO AttributeValueOptions (Id, AttributeId, Value, SortOrder)
SELECT Id, MapAttributeId, Value, SortOrder FROM MapAttributeValueOptions;

-- MapAttributeValues.MapAttributeId must now reference AttributeDefinitions instead of the
-- about-to-be-dropped MapAttributes, and is renamed to AttributeId to match. SQLite can't ALTER a FK
-- target (or a column's name alongside its FK) in place, so this rebuilds the table and copies rows
-- across — MapAttributeId's values already equal the matching AttributeDefinitions.Id thanks to the
-- explicit Id preservation above, so the copy itself needs no remapping, just the column rename.
CREATE TABLE MapAttributeValues_New (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    MapId       INTEGER NOT NULL REFERENCES Maps(Id) ON DELETE CASCADE,
    AttributeId INTEGER NOT NULL REFERENCES AttributeDefinitions(Id) ON DELETE CASCADE,
    Value       TEXT    NOT NULL DEFAULT '',
    UNIQUE(MapId, AttributeId)
);
INSERT INTO MapAttributeValues_New (Id, MapId, AttributeId, Value)
SELECT Id, MapId, MapAttributeId, Value FROM MapAttributeValues;
DROP TABLE MapAttributeValues;
ALTER TABLE MapAttributeValues_New RENAME TO MapAttributeValues;

DROP TABLE MapAttributeValueOptions;
DROP TABLE MapAttributes;
