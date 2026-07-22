using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData
{
    internal class GameData
    {
        public int? GameId { get; set; }
        public required ParsedReplayData ReplayData { get; set; }
        public int? BuildId { get; set; }
        public string Notes { get; set; } = string.Empty;
    }
}
