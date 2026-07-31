using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData
{
    internal class GamePlayer
    {
        public int? GamePlayerId { get; set; }

        public required string Clan { get; set; }
        public string FormattedClan => string.IsNullOrWhiteSpace(Clan) ? "" : $"[{Clan}]";
        public required string Name { get; set; }
        public required long Mmr { get; set; }
        public required char Race { get; set; }
        public required bool Random { get; set; }

        // This player's own selected build path(s) and per-build attribute values, keyed by GamePlayerId
        // above — every tracked player (self, allies, opponents) can have their own build selections.
        public List<int> BuildIds { get; set; } = [];
        public List<GameAttributeValue> AttributeValues { get; set; } = [];
    }
}
