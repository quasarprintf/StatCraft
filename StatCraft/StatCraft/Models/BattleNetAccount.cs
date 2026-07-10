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
    }
}
