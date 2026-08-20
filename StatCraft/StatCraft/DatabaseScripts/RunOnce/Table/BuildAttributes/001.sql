-- 2 = AttributeScope.BuildDetail (Models/GameData/Attributes/AttributeScope.cs) — every existing
-- BuildAttributes row was, and still is, a build-detail attribute.
ALTER TABLE BuildAttributes ADD COLUMN Scope INTEGER NOT NULL DEFAULT 2;
