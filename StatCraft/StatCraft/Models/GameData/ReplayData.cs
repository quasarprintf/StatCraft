using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData
{
    internal class ReplayData
    {
        public required string MapName { get; set; }
        public required ICollection<string> PlayerNames { get; set; }
        public required ICollection<string?> PlayerClans { get; set; }
        public required ICollection<char> PlayerRaces { get; set; }
        public required ICollection<bool> PlayerRandomRace { get; set; }
        public required ICollection<long?> PlayerMmrs { get; set; }
        public bool IsDraw { get; set; }
        public required ICollection<int> WinningPlayerIndices { get; set; }
        public int GameLengthSeconds { get; set; }
        public required string ReplayPath { get; set; }
    }
}
