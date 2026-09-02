using Dapper;
using Microsoft.Data.Sqlite;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Maps;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.BackgroundService;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StatCraft.Services.DatabaseRepository
{
    public class BuildRepository : SqliteRepository
    {
        // Raised after any build/attribute/value-option is inserted, updated, or deleted, so other
        // parts of the app (e.g. BuildPathPicker's menu) can refresh their view of the build tree.
        public event Action? BuildsChanged;

        public BuildRepository(string dbPath, ILogger? logger = null) : base(dbPath, logger)
        {
        }

        // All builds for a player race, regardless of which opponent races they support — used by the
        // Builds tab, which needs to show/edit every build, not just ones matching the current filter.
        // attributes populates each root's StaticAttributes (see LoadTree) — omit it for callers that
        // don't need static attributes at all, matching MapRepository.GetAllMaps' shape.
        public List<BuildNode> GetBuildsForPlayerRace(Race playerRace, IReadOnlyCollection<AttributeDefinition>? attributes = null) =>
            LoadTree("PlayerRace = @playerRace", new { playerRace }, attributes ?? []);

        // Only builds that support the given opponent race — used by the Data tab's build picker, which
        // only ever needs the exact-matchup subtree for a played game.
        public List<BuildNode> GetBuildsForMatchup(Race playerRace, Matchups matchups, IReadOnlyCollection<AttributeDefinition>? attributes = null) =>
            LoadTree("PlayerRace = @playerRace AND (Matchups & @flag) != 0", new { playerRace, flag = matchups }, attributes ?? []);

        // Every build across every player race — used by the Data tab's build filter, which (unlike the
        // Builds tab or the per-game build picker) isn't scoped to a single race or matchup.
        public List<BuildNode> GetAllBuilds(IReadOnlyCollection<AttributeDefinition>? attributes = null) =>
            LoadTree("1=1", new { }, attributes ?? []);

        private static Matchups ToMatchupFlag(Race race) => race switch
        {
            Race.Zerg => Matchups.VsZ,
            Race.Terran => Matchups.VsT,
            Race.Protoss => Matchups.VsP,
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

        private class BuildDetailsAttributeRow
        {
            public long Id { get; set; }
            public long BuildNodeId { get; set; }
            public string Name { get; set; } = "";
            public AttributeType Type { get; set; }
            public string DefaultValue { get; set; } = "";
            public AttributeScope Scope { get; set; }
        }

        private class ValueOptionRow
        {
            public long BuildAttributeId { get; set; }
            public string Value { get; set; } = "";
        }

        private class BuildAttributeValueRow
        {
            public long BuildId { get; set; }
            public int AttributeId { get; set; }
            public string Value { get; set; } = "";
        }

        private List<BuildNode> LoadTree(string whereClause, object parameters, IReadOnlyCollection<AttributeDefinition> attributes)
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
                Dictionary<long, AttributeValue> attrDict = new();
                string nodeIds = string.Join(",", nodeDict.Keys);

                List<BuildDetailsAttributeRow> attrRows = conn.Query<BuildDetailsAttributeRow>(
                    $"SELECT Id, BuildNodeId, Name, Type, DefaultValue, Scope FROM BuildDetailsAttributes WHERE BuildNodeId IN ({nodeIds}) ORDER BY SortOrder").ToList();
                foreach (BuildDetailsAttributeRow row in attrRows)
                {
                    AttributeDefinition definition = new AttributeDefinition(row.Scope) { Id = (int)row.Id, Name = row.Name, Type = row.Type };
                    definition.DefaultValue.ApplyStoredValue(row.DefaultValue);
                    attrDict[row.Id] = definition.DefaultValue;
                    nodeDict[row.BuildNodeId].Details.Add(definition);
                }

                if (attrDict.Count > 0)
                {
                    string attrIds = string.Join(",", attrDict.Keys);
                    List<ValueOptionRow> optionRows = conn.Query<ValueOptionRow>(
                        $"SELECT BuildAttributeId, Value FROM BuildDetailsAttributeValueOptions WHERE BuildAttributeId IN ({attrIds}) ORDER BY SortOrder").ToList();
                    foreach (ValueOptionRow row in optionRows)
                        attrDict[row.BuildAttributeId].Definition.ValueOptions.Add(row.Value);
                }

                // Static attributes (globally-defined, Scope.Build) — one stored row per (BuildId,
                // AttributeId), same shape as MapRepository.GetAllMaps' MapAttributeValues loading.
                if (attributes.Count > 0)
                {
                    Dictionary<int, AttributeDefinition> definitionMap = attributes.ToDictionary(d => d.Id);
                    List<BuildAttributeValueRow> staticRows = conn.Query<BuildAttributeValueRow>(
                        $"SELECT BuildId, AttributeId, Value FROM BuildAttributeValues WHERE BuildId IN ({nodeIds})").ToList();
                    foreach (BuildAttributeValueRow row in staticRows)
                    {
                        if (definitionMap.TryGetValue(row.AttributeId, out AttributeDefinition? definition))
                        {
                            AttributeValue value = new AttributeValue(definition);
                            value.ApplyStoredValue(row.Value);
                            nodeDict[row.BuildId].StaticAttributes.Add(value);
                        }
                    }
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

            // Mandatory static attributes apply to root builds only, not every node in the tree —
            // children may override a static attribute but aren't required to carry their own row,
            // matching BuildsPageViewModel.SyncAttributesFromRepository's own "only roots are updated
            // for mandatory toggle" convention. In-memory only, same as MapRepository.GetAllMaps' own
            // backfill — nothing is written back until the value is actually touched.
            foreach (AttributeDefinition definition in attributes.Where(a => a.IsMandatory))
                foreach (BuildNode root in roots)
                    if (!root.StaticAttributes.Any(v => v.Definition.Id == definition.Id))
                        root.StaticAttributes.Add(definition.DefaultValue.Clone());

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

        public void InsertAttribute(AttributeValue attr, int buildNodeId, int sortOrder)
        {
            using SqliteConnection conn = OpenConnection();
            attr.Definition.Id = (int)conn.ExecuteScalar<long>(@"
                INSERT INTO BuildDetailsAttributes (BuildNodeId, Name, Type, DefaultValue, Scope, SortOrder)
                VALUES (@buildNodeId, @name, @type, @defaultValue, @scope, @sortOrder);
                SELECT last_insert_rowid();",
                new { buildNodeId, name = attr.Definition.Name, type = attr.Definition.Type, defaultValue = attr.Serialize() ?? "", scope = attr.Definition.Scope, sortOrder });
            BuildsChanged?.Invoke();
        }

        public void UpdateAttribute(AttributeValue attr)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("UPDATE BuildDetailsAttributes SET Name = @name, Type = @type, DefaultValue = @defaultValue WHERE Id = @id",
                new { name = attr.Definition.Name, type = attr.Definition.Type, defaultValue = attr.Serialize() ?? "", id = attr.Definition.Id });
            BuildsChanged?.Invoke();
        }

        public void DeleteAttribute(int id)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM BuildDetailsAttributes WHERE Id = @id", new { id });
            BuildsChanged?.Invoke();
        }

        public void InsertValueOption(int attributeId, string value)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute(@"
                INSERT INTO BuildDetailsAttributeValueOptions (BuildAttributeId, Value, SortOrder)
                VALUES (@attrId, @value, (SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM BuildDetailsAttributeValueOptions WHERE BuildAttributeId = @attrId))",
                new { attrId = attributeId, value });
            BuildsChanged?.Invoke();
        }

        public void DeleteValueOption(int attributeId, string value)
        {
            using SqliteConnection conn = OpenConnection();
            conn.Execute("DELETE FROM BuildDetailsAttributeValueOptions WHERE BuildAttributeId = @attrId AND Value = @value",
                new { attrId = attributeId, value });
            BuildsChanged?.Invoke();
        }

        // A null value deletes the row rather than storing one — absence is how "unset" is represented,
        // since the stored encoding can't distinguish an empty string from 0/false.
        public void SaveStaticAttribute(int buildId, int attributeId, string? value)
        {
            using SqliteConnection conn = OpenConnection();
            if (value == null)
            {
                conn.Execute("DELETE FROM BuildAttributeValues WHERE BuildId = @buildId AND AttributeId = @attributeId",
                    new { buildId, attributeId });
            }
            else
            {
                conn.Execute(@"
                    INSERT INTO BuildAttributeValues (BuildId, AttributeId, Value)
                    VALUES (@buildId, @attributeId, @value)
                    ON CONFLICT(BuildId, AttributeId) DO UPDATE SET Value = @value",
                    new { buildId, attributeId, value });
            }

            BuildsChanged?.Invoke();
        }

        // Batched form of SaveStaticAttribute, for one attribute across many builds at once (e.g.
        // toggling IsMandatory) — at most two statements and one BuildsChanged instead of one of each per
        // build. Mirrors MapRepository.SaveValues.
        public void SaveStaticAttributes(List<BuildNode> builds, int buildAttributeId)
        {
            if (builds.Count == 0)
                return;

            List<int> deleteBuildIds = new List<int>();
            Dictionary<int, string> setValues = new Dictionary<int, string>();
            foreach (var build in builds)
            {
                AttributeValue? value = build.StaticAttributes.FirstOrDefault(v => v.Definition.Id == buildAttributeId);
                if (value == null || !value.HasValue)
                    deleteBuildIds.Add(build.Id);
                else
                    setValues[build.Id] = value.Serialize()!;
            }

            using SqliteConnection conn = OpenConnection();

            if (deleteBuildIds.Count > 0)
            {
                conn.Execute("DELETE FROM BuildAttributeValues WHERE BuildId IN @buildIds AND AttributeId = @buildAttributeId",
                    new { buildIds = deleteBuildIds, buildAttributeId });
            }
            if (setValues.Count > 0)
            {
                var rows = setValues.Select(kvp => new { buildId = kvp.Key, buildAttributeId, value = kvp.Value });
                conn.Execute(@"
                    INSERT INTO BuildAttributeValues (BuildId, AttributeId, Value)
                    VALUES (@buildId, @buildAttributeId, @value)
                    ON CONFLICT(BuildId, AttributeId) DO UPDATE SET Value = @value",
                    rows);
            }

            BuildsChanged?.Invoke();
        }
    }
}
