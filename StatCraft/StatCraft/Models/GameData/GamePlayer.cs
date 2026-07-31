using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData
{
    internal class GamePlayer
    {
        // Id of this player's own GamePlayers row, once persisted. GameBuilds/GameAttributeValues
        // reference this rather than the game itself, since build/attribute tracking is inherently about
        // one specific player's performance in a game.
        public int? GamePlayerId { get; set; }

        public required string Clan { get; set; }
        public string FormattedClan => string.IsNullOrWhiteSpace(Clan) ? "" : $"[{Clan}]";
        public required string Name { get; set; }
        public required long Mmr { get; set; }
        public required char Race { get; set; }
        public required bool Random { get; set; }
    }
}
