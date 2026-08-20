using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Maps;
using StatCraft.Services.BackgroundService;

namespace StatCraft.Services.DatabaseRepository
{
    // Owns maps and their globally-defined attributes. Mirrors BuildRepository's conventions, with one
    // structural difference: MapAttributes has no owning-entity foreign key, which is exactly what makes
    // an attribute apply to every map at once.
    public class MapRepository : SqliteRepository
    {
        // Raised after any map/attribute/value change, so other parts of the app can refresh.
        public event Action? MapsChanged;

        public MapRepository(string dbPath, ILogger? logger = null) : base(dbPath, logger)
        {
        }

        public List<AttributeDefinition> GetAllAttributes()
        {
            using SqliteConnection conn = OpenConnection();

            List<MapAttributeRow> rows = conn.Query<MapAttributeRow>(
                "SELECT Id, Name, Type FROM MapAttributes ORDER BY SortOrder").ToList();

            Dictionary<long, AttributeDefinition> byId = new();
            List<AttributeDefinition> attributes = new();
            foreach (MapAttributeRow row in rows)
            {
                AttributeDefinition attribute = new AttributeDefinition(AttributeScope.Map) { Id = (int)row.Id, Name = row.Name, Type = row.Type };
                byId[row.Id] = attribute;
                attributes.Add(attribute);
            }

            if (byId.Count > 0)
            {
                string ids = string.Join(",", byId.Keys);
                IEnumerable<ValueOptionRow> optionRows = conn.Query<ValueOptionRow>(
                    $"SELECT MapAttributeId, Value FROM MapAttributeValueOptions WHERE MapAttributeId IN ({ids}) ORDER BY SortOrder");
                foreach (ValueOptionRow row in optionRows)
                    byId[row.MapAttributeId].ValueOptions.Add(row.Value);
            }

            return attributes;
        }

        // Every map, each carrying one MapAttributeValue per given definition — including attributes it
        // has no stored value for, which stay null. Taking the definitions as a parameter keeps a single
        // source of truth for them and lets the caller reuse the same instances across every map, so the
        // editor and the filter bar agree on object identity.
        public List<Map> GetAllMaps(IReadOnlyCollection<AttributeDefinition> attributes)
        {
            using SqliteConnection conn = OpenConnection();

            List<MapRow> mapRows = conn.Query<MapRow>("SELECT Id, Name FROM Maps ORDER BY Name").ToList();

            Dictionary<long, Map> mapsById = new();
            List<Map> maps = new();
            foreach (MapRow row in mapRows)
            {
                Map map = new() { Id = (int)row.Id, Name = row.Name };
                foreach (AttributeDefinition attribute in attributes)
                    map.AttributeValues.Add(new AttributeValue(attribute));
                mapsById[row.Id] = map;
                maps.Add(map);
            }

            if (mapsById.Count > 0 && attributes.Count > 0)
            {
                string ids = string.Join(",", mapsById.Keys);
                IEnumerable<MapAttributeValueRow> valueRows = conn.Query<MapAttributeValueRow>(
                    $"SELECT MapId, MapAttributeId, Value FROM MapAttributeValues WHERE MapId IN ({ids})");

                foreach (MapAttributeValueRow row in valueRows)
                {
                    AttributeValue? value = mapsById[row.MapId].AttributeValues
                        .FirstOrDefault(v => v.Definition.Id == row.MapAttributeId);
                    // A stored row for an attribute that's since been deleted is simply ignored.
                    value?.ApplyStoredValue(row.Value);
                }
            }

            return maps;
        }

        // Called during replay import for whatever name the replay reported. Returns null for a blank
        // name rather than inventing a map called "" — matching how the Games migration leaves blank
        // legacy names with a null MapId.
        public Map? GetOrCreateMap(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            using SqliteConnection conn = OpenConnection();

            MapRow? existing = conn.QueryFirstOrDefault<MapRow>("SELECT Id, Name FROM Maps WHERE Name = @name", new { name });
            if (existing != null)
                return new Map { Id = (int)existing.Id, Name = existing.Name };

            long id = conn.ExecuteScalar<long>(
                "INSERT INTO Maps (Name) VALUES (@name); SELECT last_insert_rowid();", new { name });
            MapsChanged?.Invoke();
            return new Map { Id = (int)id, Name = name };
        }

        public void InsertMap(Map map)
        {
            using SqliteConnection conn = OpenConnection();
            map.Id = (int)conn.ExecuteScalar<long>(
                "INSERT INTO Maps (Name) VALUES (@name); SELECT last_insert_rowid();", new { name = map.Name });
            MapsChanged?.Invoke();
        }

        public void UpdateMap(Map map)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("UPDATE Maps SET Name = @name WHERE Id = @id", new { name = map.Name, id = map.Id });
            MapsChanged?.Invoke();
        }

        public void DeleteMap(int id)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM Maps WHERE Id = @id", new { id });
            MapsChanged?.Invoke();
        }

        public void InsertAttribute(AttributeDefinition attribute, int sortOrder)
        {
            using SqliteConnection conn = OpenConnection();
            attribute.Id = (int)conn.ExecuteScalar<long>(@"
                INSERT INTO MapAttributes (Name, Type, SortOrder) VALUES (@name, @type, @sortOrder);
                SELECT last_insert_rowid();",
                new { name = attribute.Name, type = attribute.Type, sortOrder });
            MapsChanged?.Invoke();
        }

        public void UpdateAttribute(AttributeDefinition attribute)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("UPDATE MapAttributes SET Name = @name, Type = @type WHERE Id = @id",
                new { name = attribute.Name, type = attribute.Type, id = attribute.Id });
            MapsChanged?.Invoke();
        }

        public void DeleteAttribute(int id)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM MapAttributes WHERE Id = @id", new { id });
            MapsChanged?.Invoke();
        }

        // A null value deletes the row rather than storing one — absence is how "unset" is represented,
        // since the stored encoding can't distinguish an empty string from 0/false.
        public void SaveValue(int mapId, int mapAttributeId, string? value)
        {
            using SqliteConnection conn = OpenConnection();
            if (value == null)
            {
                conn.Execute("DELETE FROM MapAttributeValues WHERE MapId = @mapId AND MapAttributeId = @mapAttributeId",
                    new { mapId, mapAttributeId });
            }
            else
            {
                conn.Execute(@"
                    INSERT INTO MapAttributeValues (MapId, MapAttributeId, Value)
                    VALUES (@mapId, @mapAttributeId, @value)
                    ON CONFLICT(MapId, MapAttributeId) DO UPDATE SET Value = @value",
                    new { mapId, mapAttributeId, value });
            }

            MapsChanged?.Invoke();
        }

        public void InsertValueOption(int mapAttributeId, string value, int sortOrder)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("INSERT INTO MapAttributeValueOptions (MapAttributeId, Value, SortOrder) VALUES (@mapAttributeId, @value, @sortOrder)",
                new { mapAttributeId, value, sortOrder });
            MapsChanged?.Invoke();
        }

        public void DeleteValueOption(int mapAttributeId, string value)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM MapAttributeValueOptions WHERE MapAttributeId = @mapAttributeId AND Value = @value",
                new { mapAttributeId, value });
            MapsChanged?.Invoke();
        }

        // Plain classes with settable properties, not positional records — see the equivalent note in
        // BuildRepository: Dapper's constructor materialization skips the widening/type-handler path.
        private class MapRow
        {
            public long Id { get; set; }
            public string Name { get; set; } = "";
        }

        private class MapAttributeRow
        {
            public long Id { get; set; }
            public string Name { get; set; } = "";
            public AttributeType Type { get; set; }
        }

        private class MapAttributeValueRow
        {
            public long MapId { get; set; }
            public int MapAttributeId { get; set; }
            public string Value { get; set; } = "";
        }

        private class ValueOptionRow
        {
            public long MapAttributeId { get; set; }
            public string Value { get; set; } = "";
        }
    }
}
