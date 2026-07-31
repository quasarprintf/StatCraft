using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData
{
    internal class GameData
    {
        public int? GameId { get; set; }
        public required ParsedReplayData ReplayData { get; set; }

        // Id of the GamePlayers row representing the tracked (active session) user themselves within
        // this game — not an ally or opponent. GameBuilds/GameAttributeValues are tied to this id, not
        // to GameId directly, since build/attribute tracking is inherently about this specific player's
        // performance in the game.
        public int? SelfGamePlayerId { get; set; }

        public List<int> BuildIds { get; set; } = [];
        public string Notes { get; set; } = string.Empty;
        public List<GameAttributeValue> AttributeValues { get; set; } = [];
    }
}
