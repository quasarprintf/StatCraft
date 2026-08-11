using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData
{
    internal class RawReplayData
    {
        public required string MapName { get; set; }
        public required ICollection<string> PlayerNames { get; set; }
        public required ICollection<string?> PlayerClans { get; set; }
        public required ICollection<char> PlayerRaces { get; set; }
        public required ICollection<bool> PlayerRandomRace { get; set; }
        // Each player's in-game color, packed 0xAARRGGBB (matching Avalonia's Color.FromUInt32).
        public required ICollection<int> PlayerColorsArgb { get; set; }
        public required ICollection<long?> PlayerMmrs { get; set; }
        public required ICollection<int> PlayerTeams { get; set; }
        public required ICollection<int> PlayerProfileIds { get; set; }
        public bool IsMatchmade { get; set; } // GameOptions.Amm from the replay
        public bool IsDraw { get; set; }
        public required ICollection<int> WinningPlayerIndices { get; set; }
        public int GameLengthSeconds { get; set; }
        public required string ReplayPath { get; set; }
        public required DateTimeOffset ReplayTimestamp { get; set; }
    }
}
