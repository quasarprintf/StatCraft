using Dapper;
using Microsoft.Data.Sqlite;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Services.BackgroundService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.Services.DatabaseRepository
{
    public class AttributeRepository : SqliteRepository
    {
        public event Action? AttributesChanged;

        public AttributeRepository(string dbPath, ILogger? logger = null) : base(dbPath, logger)
        {
        }

        public List<AttributeDefinition> GetAllAttributes()
        {
            using SqliteConnection conn = OpenConnection();

            List<AttributeDefinitionRow> rows = conn.Query<AttributeDefinitionRow>(
                "SELECT Id, Name, Type, Scope FROM AttributeDefinitions WHERE Scope = @scope ORDER BY SortOrder",
                new { scope = AttributeScope.Map }).ToList();

            Dictionary<long, AttributeDefinition> byId = new();
            List<AttributeDefinition> attributes = new();
            foreach (AttributeDefinitionRow row in rows)
            {
                AttributeDefinition attribute = new AttributeDefinition(row.Scope) { Id = (int)row.Id, Name = row.Name, Type = row.Type };
                byId[row.Id] = attribute;
                attributes.Add(attribute);
            }

            if (byId.Count > 0)
            {
                string ids = string.Join(",", byId.Keys);
                IEnumerable<ValueOptionRow> optionRows = conn.Query<ValueOptionRow>(
                    $"SELECT AttributeId, Value FROM AttributeValueOptions WHERE AttributeId IN ({ids}) ORDER BY SortOrder");
                foreach (ValueOptionRow row in optionRows)
                    byId[row.AttributeId].ValueOptions.Add(row.Value);
            }

            return attributes;
        }

        public void InsertAttribute(AttributeDefinition attribute, int sortOrder)
        {
            using SqliteConnection conn = OpenConnection();
            attribute.Id = (int)conn.ExecuteScalar<long>(@"
                INSERT INTO AttributeDefinitions (Name, Type, Scope, DefaultValue, SortOrder) VALUES (@name, @type, @scope, '', @sortOrder);
                SELECT last_insert_rowid();",
                new { name = attribute.Name, type = attribute.Type, scope = attribute.Scope, sortOrder });
            AttributesChanged?.Invoke();
        }

        public void UpdateAttribute(AttributeDefinition attribute)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("UPDATE AttributeDefinitions SET Name = @name, Type = @type WHERE Id = @id AND Scope = @scope",
                new { name = attribute.Name, type = attribute.Type, id = attribute.Id, scope = AttributeScope.Map });
            AttributesChanged?.Invoke();
        }

        public void DeleteAttribute(int id)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM AttributeDefinitions WHERE Id = @id AND Scope = @scope", new { id, scope = AttributeScope.Map });
            AttributesChanged?.Invoke();
        }

        public void InsertValueOption(int mapAttributeId, string value, int sortOrder)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("INSERT INTO AttributeValueOptions (AttributeId, Value, SortOrder) VALUES (@mapAttributeId, @value, @sortOrder)",
                new { mapAttributeId, value, sortOrder });
            AttributesChanged?.Invoke();
        }

        public void DeleteValueOption(int mapAttributeId, string value)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM AttributeValueOptions WHERE AttributeId = @mapAttributeId AND Value = @value",
                new { mapAttributeId, value });
            AttributesChanged?.Invoke();
        }

        private class AttributeDefinitionRow
        {
            public long Id { get; set; }
            public string Name { get; set; } = "";
            public AttributeType Type { get; set; }
            public AttributeScope Scope { get; set; }
        }

        private class ValueOptionRow
        {
            public long AttributeId { get; set; }
            public string Value { get; set; } = "";
        }
    }
}
