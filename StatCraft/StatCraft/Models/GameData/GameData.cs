using StatCraft.Models.GameData.Maps;

namespace StatCraft.Models.GameData
{
    internal class GameData
    {
        public int? GameId { get; set; }
        public int Sc2ProfileId { get; set; }

        public Map? Map { get; set; }

        public GameType GameType { get; set; }
        public required ParsedReplayData ReplayData { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
}
