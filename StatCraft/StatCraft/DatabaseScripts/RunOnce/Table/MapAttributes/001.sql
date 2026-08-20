-- 4 = AttributeScope.Map (Models/GameData/Attributes/AttributeScope.cs) — every existing
-- MapAttributes row was, and still is, a map attribute.
ALTER TABLE MapAttributes ADD COLUMN Scope INTEGER NOT NULL DEFAULT 4;
