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

        // GameOptions.Amm from the replay — false means a custom game.
        public bool IsMatchmade { get; set; }

        // Whether this game has a ladder rating that can be attributed to it. Team games carry their own
        // per-team rating that doesn't correspond to a 1v1 ladder, and an unranked or custom game reports
        // no rating at all (Mmr 0) — in both cases there's nothing meaningful to compare against.
        public bool IsRatedOneVsOne => Allies.Length == 0 && Opponents.Length == 1 && Player.Mmr > 0;
    }
}
