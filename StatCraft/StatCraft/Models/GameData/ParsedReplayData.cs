using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData
{
    internal class ParsedReplayData
    {
        public required string MapName { get; set; }
        public int GameLengthSeconds { get; set; }
        public required string ReplayPath { get; set; }
        public decimal Win { get; set; } //0 = lose, 1 = win, 0.5 = draw
        public required GamePlayer Player { get; set; }
        public GamePlayer[] Allies { get; set; } = Array.Empty<GamePlayer>();
        public required GamePlayer[] Opponents { get; set; }
    }
}
