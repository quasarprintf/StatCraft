namespace StatCraft.Models.GameData
{
    internal class GameData
    {
        public int? GameId { get; set; }
        public int Sc2ProfileId { get; set; }
        public required ParsedReplayData ReplayData { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
}
