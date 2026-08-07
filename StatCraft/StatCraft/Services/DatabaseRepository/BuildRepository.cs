using StatCraft.Models.GameData.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
using StatCraft.ViewModels;
using StatCraft.Models.GameData.Attributes.DynamicAttribute;

namespace StatCraft.Services.DatabaseRepository
{
    public class BuildRepository : SqliteRepository
    {
        // Raised after any build/attribute/value-option is inserted, updated, or deleted, so other
        // parts of the app (e.g. BuildPathPicker's menu) can refresh their view of the build tree.
        public event Action? BuildsChanged;

        public BuildRepository(string dbPath) : base(dbPath)
        {
        }

        public void Initialize()
        {
            EnsureDatabaseFolderExists();

            using SqliteConnection conn = OpenConnection();
            conn.Execute(@"
                CREATE TABLE IF NOT EXISTS BuildNodes (
                    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    PlayerRace  INTEGER NOT NULL DEFAULT 0,
                    Matchups    INTEGER NOT NULL DEFAULT 0,
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
                );");

            RunMigrations(conn, nameof(BuildRepository), Migrations);
        }

        // No migrations exist yet — CreateTables above already reflects the current schema in full.
        // Append future schema changes here as named methods, in order; RunMigrations tracks how many
        // of them a given database has already applied.
        private static readonly Action<SqliteConnection>[] Migrations = [];

        // All builds for a player race, regardless of which opponent races they support — used by the
        // Builds tab, which needs to show/edit every build, not just ones matching the current filter.
        public List<BuildNode> GetBuildsForPlayerRace(Race playerRace) =>
            LoadTree("PlayerRace = @playerRace", new { playerRace });

        // Only builds that support the given opponent race — used by the Data tab's build picker, which
        // only ever needs the exact-matchup subtree for a played game.
        public List<BuildNode> GetBuildsForMatchup(Race playerRace, Matchups matchups) =>
            LoadTree("PlayerRace = @playerRace AND (Matchups & @flag) != 0", new { playerRace, flag = matchups });

        // Every build across every player race — used by the Data tab's build filter, which (unlike the
        // Builds tab or the per-game build picker) isn't scoped to a single race or matchup.
        public List<BuildNode> GetAllBuilds() => LoadTree("1=1", new { });

        private static Matchups ToMatchupFlag(Race race) => race switch
        {
            Race.Z => Matchups.VsZ,
            Race.T => Matchups.VsT,
            Race.P => Matchups.VsP,
            _ => Matchups.None,
        };

        // Plain classes with settable properties, not positional records — Dapper's constructor-based
        // materialization requires constructor parameter types to exactly match the raw column types,
        // which the property-setter path used for a parameterless-constructible type doesn't require.
        private class BuildNodeRow
        {
            public long Id { get; set; }
            public long? ParentId { get; set; }
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public Race PlayerRace { get; set; }
            public Matchups Matchups { get; set; }
        }

        private class BuildAttributeRow
        {
            public long Id { get; set; }
            public long BuildNodeId { get; set; }
            public string Name { get; set; } = "";
            public AttributeType Type { get; set; }
            public string DefaultValue { get; set; } = "";
        }

        private class ValueOptionRow
        {
            public long BuildAttributeId { get; set; }
            public string Value { get; set; } = "";
        }

        private List<BuildNode> LoadTree(string whereClause, object parameters)
        {
            using SqliteConnection conn = OpenConnection();

            List<BuildNodeRow> nodeRows = conn.Query<BuildNodeRow>(
                $"SELECT Id, ParentId, Name, Description, PlayerRace, Matchups FROM BuildNodes WHERE {whereClause} ORDER BY SortOrder",
                parameters).ToList();

            Dictionary<long, BuildNode> nodeDict = new();
            Dictionary<long, long?> parentMap = new();
            foreach (BuildNodeRow row in nodeRows)
            {
                nodeDict[row.Id] = new BuildNode
                {
                    Id = (int)row.Id,
                    Name = row.Name,
                    Description = row.Description,
                    PlayerRace = row.PlayerRace,
                    Matchups = row.Matchups,
                };
                parentMap[row.Id] = row.ParentId;
            }

            if (nodeDict.Count > 0)
            {
                Dictionary<long, DynamicAttribute> attrDict = new();
                string nodeIds = string.Join(",", nodeDict.Keys);

                List<BuildAttributeRow> attrRows = conn.Query<BuildAttributeRow>(
                    $"SELECT Id, BuildNodeId, Name, Type, DefaultValue FROM BuildAttributes WHERE BuildNodeId IN ({nodeIds}) ORDER BY SortOrder").ToList();
                foreach (BuildAttributeRow row in attrRows)
                {
                    DynamicAttribute attr = new DynamicAttribute { Id = (int)row.Id, Name = row.Name, Type = row.Type };
                    attr.ApplyValue(row.DefaultValue);
                    attrDict[row.Id] = attr;
                    nodeDict[row.BuildNodeId].Attributes.Add(attr);
                }

                if (attrDict.Count > 0)
                {
                    string attrIds = string.Join(",", attrDict.Keys);
                    List<ValueOptionRow> optionRows = conn.Query<ValueOptionRow>(
                        $"SELECT BuildAttributeId, Value FROM AttributeValueOptions WHERE BuildAttributeId IN ({attrIds}) ORDER BY SortOrder").ToList();
                    foreach (ValueOptionRow row in optionRows)
                        attrDict[row.BuildAttributeId].ValueOptions.Add(row.Value);
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

        public void InsertBuild(BuildNode node, int? parentId, int sortOrder)
        {
            using SqliteConnection conn = OpenConnection();
            node.Id = (int)conn.ExecuteScalar<long>(@"
                INSERT INTO BuildNodes (PlayerRace, Matchups, ParentId, Name, Description, SortOrder)
                VALUES (@playerRace, @matchups, @parentId, @name, @description, @sortOrder);
                SELECT last_insert_rowid();",
                new { playerRace = node.PlayerRace, matchups = node.Matchups, parentId, name = node.Name, description = node.Description, sortOrder });
            BuildsChanged?.Invoke();
        }

        public void UpdateBuild(BuildNode node)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("UPDATE BuildNodes SET Name = @name, Description = @description, Matchups = @matchups WHERE Id = @id",
                new { name = node.Name, description = node.Description, matchups = node.Matchups, id = node.Id });
            BuildsChanged?.Invoke();
        }

        public void DeleteBuild(int id)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM BuildNodes WHERE Id = @id", new { id });
            BuildsChanged?.Invoke();
        }

        public void InsertAttribute(DynamicAttribute attr, int buildNodeId, int sortOrder)
        {
            using SqliteConnection conn = OpenConnection();
            attr.Id = (int)conn.ExecuteScalar<long>(@"
                INSERT INTO BuildAttributes (BuildNodeId, Name, Type, DefaultValue, SortOrder)
                VALUES (@buildNodeId, @name, @type, @defaultValue, @sortOrder);
                SELECT last_insert_rowid();",
                new { buildNodeId, name = attr.Name, type = attr.Type, defaultValue = attr.SerializeValue(), sortOrder });
            BuildsChanged?.Invoke();
        }

        public void UpdateAttribute(DynamicAttribute attr)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("UPDATE BuildAttributes SET Name = @name, Type = @type, DefaultValue = @defaultValue WHERE Id = @id",
                new { name = attr.Name, type = attr.Type, defaultValue = attr.SerializeValue(), id = attr.Id });
            BuildsChanged?.Invoke();
        }

        public void DeleteAttribute(int id)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM BuildAttributes WHERE Id = @id", new { id });
            BuildsChanged?.Invoke();
        }

        public void InsertValueOption(int attributeId, string value, int sortOrder)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("INSERT INTO AttributeValueOptions (BuildAttributeId, Value, SortOrder) VALUES (@attrId, @value, @sortOrder)",
                new { attrId = attributeId, value, sortOrder });
            BuildsChanged?.Invoke();
        }

        public void DeleteValueOption(int attributeId, string value)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM AttributeValueOptions WHERE BuildAttributeId = @attrId AND Value = @value",
                new { attrId = attributeId, value });
            BuildsChanged?.Invoke();
        }
    }
}
