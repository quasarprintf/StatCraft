namespace StatCraft.Models.GameData
{
    internal class GameData
    {
        public int? GameId { get; set; }
        public int Sc2ProfileId { get; set; }

        // Null for games imported before game-type detection existed — the replay flags it derives from
        // were never persisted, so those rows genuinely can't be reclassified.
        public GameType? GameType { get; set; }
        public required ParsedReplayData ReplayData { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
}
