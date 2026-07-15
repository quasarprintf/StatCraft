using System.Collections.Generic;

namespace StatCraft.Models.Battlenet
{
    public static class Sc2Regions
    {
        private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
        {
            ["1"] = "NA",
            ["2"] = "EU",
            ["3"] = "KR",
            ["5"] = "CN",
        };

        public static string GetLabel(string regionId) => Labels.GetValueOrDefault(regionId, regionId);
    }
}
