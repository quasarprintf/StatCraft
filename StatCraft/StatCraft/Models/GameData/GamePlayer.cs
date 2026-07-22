using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData
{
    internal class GamePlayer
    {
        public required string Clan { get; set; }
        public required string Name { get; set; }
        public required long Mmr { get; set; }
        public required char Race { get; set; }
        public required bool Random { get; set; }
    }
}
