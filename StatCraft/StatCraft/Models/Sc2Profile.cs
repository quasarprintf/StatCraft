namespace StatCraft.Models
{
    public class Sc2Profile
    {
        public string RegionLabel { get; set; } = "";
        public string RegionId { get; set; } = "";
        public string RealmId { get; set; } = "";
        public string ProfileId { get; set; } = "";
        public string Name { get; set; } = "";

        public string DisplayName => $"{Name} ({RegionLabel})";
    }
}
