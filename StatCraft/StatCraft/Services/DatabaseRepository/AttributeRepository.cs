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

        public List<AttributeDefinition> GetAllAttributes(AttributeScope? scope = null)
        {
            using SqliteConnection conn = OpenConnection();

            string query = "SELECT Id, Name, Type, Scope, DefaultValue, Description FROM AttributeDefinitions";
            object? parameters = null;
            if (scope != null)
            {
                query += " WHERE Scope = @scope ORDER BY SortOrder";
                parameters = new { scope = scope };
            }
            List<AttributeDefinitionRow> rows = conn.Query<AttributeDefinitionRow>(query,
               parameters
               ).ToList();

            Dictionary<long, AttributeDefinition> byId = new();
            List<AttributeDefinition> attributes = new();
            foreach (AttributeDefinitionRow row in rows)
            {
                AttributeDefinition attribute = new AttributeDefinition(row.Scope, row.Type, row.DefaultValue) { Id = (int)row.Id, Name = row.Name, Description = row.Description };
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
                INSERT INTO AttributeDefinitions (Name, Type, Scope, DefaultValue, SortOrder, Description) VALUES (@name, @type, @scope, @defaultValue, @sortOrder, @description);
                SELECT last_insert_rowid();",
                new { name = attribute.Name, type = attribute.Type, scope = attribute.Scope, defaultValue = attribute.DefaultValue.Serialize() ?? "", sortOrder, description = attribute.Description });
            AttributesChanged?.Invoke();
        }

        public void UpdateAttribute(AttributeDefinition attribute)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("UPDATE AttributeDefinitions SET Name = @name, Type = @type, DefaultValue = @defaultValue, Description = @description WHERE Id = @id",
                new { name = attribute.Name, type = attribute.Type, defaultValue = attribute.DefaultValue.Serialize() ?? "", description = attribute.Description, id = attribute.Id, scope = AttributeScope.Map });
            AttributesChanged?.Invoke();
        }

        public void DeleteAttribute(int id)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM AttributeDefinitions WHERE Id = @id", new { id });
            AttributesChanged?.Invoke();
        }

        public void InsertValueOption(int attributeId, string value, int sortOrder)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("INSERT INTO AttributeValueOptions (AttributeId, Value, SortOrder) VALUES (@attributeId, @value, @sortOrder)",
                new { attributeId, value, sortOrder });
            AttributesChanged?.Invoke();
        }

        public void DeleteValueOption(int attributeId, string value)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM AttributeValueOptions WHERE AttributeId = @attributeId AND Value = @value",
                new { attributeId, value });
            AttributesChanged?.Invoke();
        }

        private class AttributeDefinitionRow
        {
            public long Id { get; set; }
            public string Name { get; set; } = "";
            public AttributeType Type { get; set; }
            public AttributeScope Scope { get; set; }
            public string DefaultValue { get; set; } = "";
            public string Description { get; set; } = "";
        }

        private class ValueOptionRow
        {
            public long AttributeId { get; set; }
            public string Value { get; set; } = "";
        }
    }
}
