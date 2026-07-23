using System.Collections.Generic;
using System.Linq;
using StatCraft.Models.GameData.Builds;

namespace StatCraft.Services.DataParsing
{
    internal static class BuildPathHelper
    {
        // DFS over all roots; returns the root-to-target path (inclusive), or null if not found.
        internal static List<BuildNode>? FindPath(IEnumerable<BuildNode> roots, int targetId)
        {
            foreach (BuildNode root in roots)
            {
                List<BuildNode>? path = FindPath(root, targetId);
                if (path != null)
                    return path;
            }
            return null;
        }

        private static List<BuildNode>? FindPath(BuildNode node, int targetId)
        {
            if (node.Id == targetId)
                return [node];

            foreach (BuildNode child in node.Children)
            {
                List<BuildNode>? childPath = FindPath(child, targetId);
                if (childPath != null)
                {
                    childPath.Insert(0, node);
                    return childPath;
                }
            }

            return null;
        }

        internal static List<BuildAttribute> FlattenAttributes(IEnumerable<BuildNode> path) =>
            path.SelectMany(n => n.Attributes).ToList();
    }
}
