using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models
{
    internal class ReplayData
    {
        public required string MapName { get; set; }
        public required ICollection<string> PlayerNames { get; set; }
        public required ICollection<string?> PlayerClans { get; set; }
        public required ICollection<char> PlayerRaces { get; set; }
        public required ICollection<bool> PlayerRandomRace { get; set; }
        public ICollection<int>? PlayerMmrs { get; set; } //TODO: not sure if this can be extracted from replay, or if we need to get this from battlenet api
        public bool IsDraw { get; set; }
        public required ICollection<int> WinningPlayerIndices { get; set; }
        public int GameLengthSeconds { get; set; }
        public required string ReplayPath { get; set; }
    }
}
