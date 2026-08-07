using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData
{
    internal class ParsedReplayData
    {
        public int GameLengthSeconds { get; set; }
        public required string ReplayPath { get; set; }
        public required DateTimeOffset ReplayTimestamp { get; set; }
        public decimal Win { get; set; } //0 = lose, 1 = win, 0.5 = draw
        public required GamePlayer Player { get; set; }
        public GamePlayer[] Allies { get; set; } = Array.Empty<GamePlayer>();
        public required GamePlayer[] Opponents { get; set; }
        public bool IsMatchmade { get; set; }

        public bool IsRatedOneVsOne => Allies.Length == 0 && Opponents.Length == 1 && Player.Mmr > 0;
    }
}
