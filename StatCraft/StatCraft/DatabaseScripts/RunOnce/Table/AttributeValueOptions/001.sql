-- Renamed to disambiguate from the new shared AttributeValueOptions table created in 002.sql — this
-- one remains specifically the value options for BuildDetailsAttributes (see BuildAttributes/002.sql).
ALTER TABLE AttributeValueOptions RENAME TO BuildDetailsAttributeValueOptions;
