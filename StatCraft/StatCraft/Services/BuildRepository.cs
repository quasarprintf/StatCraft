using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using StatCraft.ViewModels;

namespace StatCraft.Services
{
    public class BuildRepository
    {
        private readonly string _dbPath;
        private readonly string _connectionString;

        public BuildRepository(string dbPath)
        {
            _dbPath = dbPath;
            _connectionString = $"Data Source={dbPath}";
        }

        public void Initialize()
        {
            string? dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS BuildNodes (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    Matchup     INTEGER NOT NULL,
                    ParentId    INTEGER REFERENCES BuildNodes(Id) ON DELETE CASCADE,
                    Name        TEXT    NOT NULL DEFAULT '',
                    Description TEXT    NOT NULL DEFAULT '',
                    SortOrder   INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS BuildAttributes (
                    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    BuildNodeId  INTEGER NOT NULL REFERENCES BuildNodes(Id) ON DELETE CASCADE,
                    Name         TEXT    NOT NULL DEFAULT '',
                    Type         INTEGER NOT NULL DEFAULT 0,
                    DefaultValue TEXT    NOT NULL DEFAULT '',
                    SortOrder    INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS AttributeValueOptions (
                    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    BuildAttributeId INTEGER NOT NULL REFERENCES BuildAttributes(Id) ON DELETE CASCADE,
                    Value            TEXT    NOT NULL,
                    SortOrder        INTEGER NOT NULL DEFAULT 0
                );";
            cmd.ExecuteNonQuery();

            try
            {
                using SqliteCommand migrateCmd = conn.CreateCommand();
                migrateCmd.CommandText = "ALTER TABLE BuildAttributes ADD COLUMN DefaultValue TEXT NOT NULL DEFAULT ''";
                migrateCmd.ExecuteNonQuery();
            }
            catch (SqliteException)
            {
                // Column already exists.
            }
        }

        public List<BuildNode> GetBuildsForMatchup(Matchup matchup)
        {
            using SqliteConnection conn = OpenConnection();

            Dictionary<long, BuildNode> nodeDict = new Dictionary<long, BuildNode>();
            Dictionary<long, long?> parentMap = new Dictionary<long, long?>();

            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, ParentId, Name, Description FROM BuildNodes WHERE Matchup = @matchup ORDER BY SortOrder";
                cmd.Parameters.AddWithValue("@matchup", (int)matchup);
                using SqliteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    long id = reader.GetInt64(0);
                    long? parentId = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
                    nodeDict[id] = new BuildNode
                    {
                        Id = (int)id,
                        Name = reader.GetString(2),
                        Description = reader.GetString(3),
                    };
                    parentMap[id] = parentId;
                }
            }

            if (nodeDict.Count > 0)
            {
                Dictionary<long, BuildAttribute> attrDict = new Dictionary<long, BuildAttribute>();
                string nodeIds = string.Join(",", nodeDict.Keys);

                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT Id, BuildNodeId, Name, Type, DefaultValue FROM BuildAttributes WHERE BuildNodeId IN ({nodeIds}) ORDER BY SortOrder";
                    using SqliteDataReader reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        long id = reader.GetInt64(0);
                        long buildNodeId = reader.GetInt64(1);
                        BuildAttribute attr = new BuildAttribute
                        {
                            Id = (int)id,
                            Name = reader.GetString(2),
                            Type = (AttributeType)reader.GetInt32(3),
                        };
                        ApplyDefaultValue(attr, reader.GetString(4));
                        attrDict[id] = attr;
                        nodeDict[buildNodeId].Attributes.Add(attr);
                    }
                }

                if (attrDict.Count > 0)
                {
                    string attrIds = string.Join(",", attrDict.Keys);
                    using SqliteCommand cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT BuildAttributeId, Value FROM AttributeValueOptions WHERE BuildAttributeId IN ({attrIds}) ORDER BY SortOrder";
                    using SqliteDataReader reader = cmd.ExecuteReader();
                    while (reader.Read())
                        attrDict[reader.GetInt64(0)].ValueOptions.Add(reader.GetString(1));
                }
            }

            List<BuildNode> roots = new List<BuildNode>();
            foreach ((long id, BuildNode node) in nodeDict)
            {
                long? parentId = parentMap[id];
                if (parentId.HasValue && nodeDict.TryGetValue(parentId.Value, out BuildNode? parent))
                    parent.Children.Add(node);
                else
                    roots.Add(node);
            }

            return roots;
        }

        public void InsertBuild(BuildNode node, Matchup matchup, int? parentId, int sortOrder)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO BuildNodes (Matchup, ParentId, Name, Description, SortOrder)
                VALUES (@matchup, @parentId, @name, @description, @sortOrder);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@matchup", (int)matchup);
            cmd.Parameters.AddWithValue("@parentId", (object?)parentId ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("@name", node.Name);
            cmd.Parameters.AddWithValue("@description", node.Description);
            cmd.Parameters.AddWithValue("@sortOrder", sortOrder);
            node.Id = (int)(long)cmd.ExecuteScalar()!;
        }

        public void UpdateBuild(BuildNode node)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE BuildNodes SET Name = @name, Description = @description WHERE Id = @id";
            cmd.Parameters.AddWithValue("@name", node.Name);
            cmd.Parameters.AddWithValue("@description", node.Description);
            cmd.Parameters.AddWithValue("@id", node.Id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteBuild(int id)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM BuildNodes WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void InsertAttribute(BuildAttribute attr, int buildNodeId, int sortOrder)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO BuildAttributes (BuildNodeId, Name, Type, DefaultValue, SortOrder)
                VALUES (@buildNodeId, @name, @type, @defaultValue, @sortOrder);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@buildNodeId", buildNodeId);
            cmd.Parameters.AddWithValue("@name", attr.Name);
            cmd.Parameters.AddWithValue("@type", (int)attr.Type);
            cmd.Parameters.AddWithValue("@defaultValue", SerializeDefaultValue(attr));
            cmd.Parameters.AddWithValue("@sortOrder", sortOrder);
            attr.Id = (int)(long)cmd.ExecuteScalar()!;
        }

        public void UpdateAttribute(BuildAttribute attr)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE BuildAttributes SET Name = @name, Type = @type, DefaultValue = @defaultValue WHERE Id = @id";
            cmd.Parameters.AddWithValue("@name", attr.Name);
            cmd.Parameters.AddWithValue("@type", (int)attr.Type);
            cmd.Parameters.AddWithValue("@defaultValue", SerializeDefaultValue(attr));
            cmd.Parameters.AddWithValue("@id", attr.Id);
            cmd.ExecuteNonQuery();
        }

        private static string SerializeDefaultValue(BuildAttribute attr) => attr.Type switch
        {
            AttributeType.Numeric => attr.NumericValue.ToString(CultureInfo.InvariantCulture),
            AttributeType.Bool => attr.BoolValue.ToString(CultureInfo.InvariantCulture),
            AttributeType.Percent => attr.PercentValue.ToString(CultureInfo.InvariantCulture),
            AttributeType.Values => attr.SelectedValue ?? string.Empty,
            _ => string.Empty,
        };

        private static void ApplyDefaultValue(BuildAttribute attr, string defaultValue)
        {
            switch (attr.Type)
            {
                case AttributeType.Numeric:
                    if (decimal.TryParse(defaultValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal numeric))
                        attr.NumericValue = numeric;
                    break;
                case AttributeType.Bool:
                    if (bool.TryParse(defaultValue, out bool boolValue))
                        attr.BoolValue = boolValue;
                    break;
                case AttributeType.Percent:
                    if (decimal.TryParse(defaultValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal percent))
                        attr.PercentValue = percent;
                    break;
                case AttributeType.Values:
                    attr.SelectedValue = string.IsNullOrEmpty(defaultValue) ? null : defaultValue;
                    break;
            }
        }

        public void DeleteAttribute(int id)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM BuildAttributes WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void InsertValueOption(int attributeId, string value, int sortOrder)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO AttributeValueOptions (BuildAttributeId, Value, SortOrder) VALUES (@attrId, @value, @sortOrder)";
            cmd.Parameters.AddWithValue("@attrId", attributeId);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.Parameters.AddWithValue("@sortOrder", sortOrder);
            cmd.ExecuteNonQuery();
        }

        public void DeleteValueOption(int attributeId, string value)
        {
            using SqliteConnection conn = OpenConnection();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM AttributeValueOptions WHERE BuildAttributeId = @attrId AND Value = @value";
            cmd.Parameters.AddWithValue("@attrId", attributeId);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.ExecuteNonQuery();
        }

        private SqliteConnection OpenConnection()
        {
            SqliteConnection conn = new SqliteConnection(_connectionString);
            conn.Open();
            using SqliteCommand pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON";
            pragma.ExecuteNonQuery();
            return conn;
        }
    }
}
