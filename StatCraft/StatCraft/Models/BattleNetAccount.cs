using System;

namespace StatCraft.Models
{
    public class BattleNetAccount
    {
        public int Id { get; set; }
        public string BattleTag { get; set; } = "";
        public string AccountSub { get; set; } = "";
        public byte[] EncryptedAccessToken { get; set; } = [];
        public byte[]? EncryptedRefreshToken { get; set; }
        public DateTimeOffset TokenExpiresAtUtc { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }

        public string Sc2RegionId { get; set; } = "";
        public string Sc2RealmId { get; set; } = "";
        public string Sc2ProfileId { get; set; } = "";
        public string Sc2ProfileName { get; set; } = "";

        public string DisplayName => string.IsNullOrEmpty(Sc2ProfileName) ? BattleTag : $"{Sc2ProfileName} ({BattleTag})";
    }
}
