using System;
using System.IO;

namespace StatCraft.Models
{
    public class Sc2Profile
    {
        public int Id { get; set; }
        public int BattleNetAccountId { get; set; }
        public string RegionId { get; set; } = "";
        public string RealmId { get; set; } = "";
        public string ProfileId { get; set; } = "";
        public string Name { get; set; } = "";

        // Populated by AccountRepository.GetAllProfiles()'s join, or set directly during the linking flow.
        public BattleNetAccount? Account { get; set; }

        public string RegionLabel => Sc2Regions.GetLabel(RegionId);
        public string DisplayName => $"{RegionLabel} - {Name}";
        public string ReplayFolderPathSuffix => String.Format($"Accounts{{0}}{Account?.AccountSub}{{0}}{RealmId}-S2-{RegionId}-{ProfileId}{{0}}Replays{{0}}Multiplayer", Path.DirectorySeparatorChar);
    }
}
