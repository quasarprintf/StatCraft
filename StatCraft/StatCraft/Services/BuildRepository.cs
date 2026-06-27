using System.Collections.Generic;
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
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
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
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    BuildNodeId INTEGER NOT NULL REFERENCES BuildNodes(Id) ON DELETE CASCADE,
                    Name        TEXT    NOT NULL DEFAULT '',
                    Type        INTEGER NOT NULL DEFAULT 0,
                    SortOrder   INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS AttributeValueOptions (
                    Id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    BuildAttributeId INTEGER NOT NULL REFERENCES BuildAttributes(Id) ON DELETE CASCADE,
                    Value            TEXT    NOT NULL,
                    SortOrder        INTEGER NOT NULL DEFAULT 0
                );";
            cmd.ExecuteNonQuery();
        }

        public List<BuildNode> GetBuildsForMatchup(Matchup matchup)
        {
            using var conn = OpenConnection();

            var nodeDict = new Dictionary<long, BuildNode>();
            var parentMap = new Dictionary<long, long?>();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT Id, ParentId, Name, Description FROM BuildNodes WHERE Matchup = @matchup ORDER BY SortOrder";
                cmd.Parameters.AddWithValue("@matchup", (int)matchup);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var id = reader.GetInt64(0);
                    var parentId = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
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
                var attrDict = new Dictionary<long, BuildAttribute>();
                var nodeIds = string.Join(",", nodeDict.Keys);

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"SELECT Id, BuildNodeId, Name, Type FROM BuildAttributes WHERE BuildNodeId IN ({nodeIds}) ORDER BY SortOrder";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var id = reader.GetInt64(0);
                        var buildNodeId = reader.GetInt64(1);
                        var attr = new BuildAttribute
                        {
                            Id = (int)id,
                            Name = reader.GetString(2),
                            Type = (AttributeType)reader.GetInt32(3),
                        };
                        attrDict[id] = attr;
                        nodeDict[buildNodeId].Attributes.Add(attr);
                    }
                }

                if (attrDict.Count > 0)
                {
                    var attrIds = string.Join(",", attrDict.Keys);
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT BuildAttributeId, Value FROM AttributeValueOptions WHERE BuildAttributeId IN ({attrIds}) ORDER BY SortOrder";
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                        attrDict[reader.GetInt64(0)].ValueOptions.Add(reader.GetString(1));
                }
            }

            var roots = new List<BuildNode>();
            foreach (var (id, node) in nodeDict)
            {
                var parentId = parentMap[id];
                if (parentId.HasValue && nodeDict.TryGetValue(parentId.Value, out var parent))
                    parent.Children.Add(node);
                else
                    roots.Add(node);
            }

            return roots;
        }

        public void InsertBuild(BuildNode node, Matchup matchup, int? parentId, int sortOrder)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
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
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE BuildNodes SET Name = @name, Description = @description WHERE Id = @id";
            cmd.Parameters.AddWithValue("@name", node.Name);
            cmd.Parameters.AddWithValue("@description", node.Description);
            cmd.Parameters.AddWithValue("@id", node.Id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteBuild(int id)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM BuildNodes WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void InsertAttribute(BuildAttribute attr, int buildNodeId, int sortOrder)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO BuildAttributes (BuildNodeId, Name, Type, SortOrder)
                VALUES (@buildNodeId, @name, @type, @sortOrder);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@buildNodeId", buildNodeId);
            cmd.Parameters.AddWithValue("@name", attr.Name);
            cmd.Parameters.AddWithValue("@type", (int)attr.Type);
            cmd.Parameters.AddWithValue("@sortOrder", sortOrder);
            attr.Id = (int)(long)cmd.ExecuteScalar()!;
        }

        public void UpdateAttribute(BuildAttribute attr)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE BuildAttributes SET Name = @name, Type = @type WHERE Id = @id";
            cmd.Parameters.AddWithValue("@name", attr.Name);
            cmd.Parameters.AddWithValue("@type", (int)attr.Type);
            cmd.Parameters.AddWithValue("@id", attr.Id);
            cmd.ExecuteNonQuery();
        }

        public void DeleteAttribute(int id)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM BuildAttributes WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public void InsertValueOption(int attributeId, string value, int sortOrder)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO AttributeValueOptions (BuildAttributeId, Value, SortOrder) VALUES (@attrId, @value, @sortOrder)";
            cmd.Parameters.AddWithValue("@attrId", attributeId);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.Parameters.AddWithValue("@sortOrder", sortOrder);
            cmd.ExecuteNonQuery();
        }

        public void DeleteValueOption(int attributeId, string value)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM AttributeValueOptions WHERE BuildAttributeId = @attrId AND Value = @value";
            cmd.Parameters.AddWithValue("@attrId", attributeId);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.ExecuteNonQuery();
        }

        private SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var pragma = conn.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = ON";
            pragma.ExecuteNonQuery();
            return conn;
        }
    }
}
