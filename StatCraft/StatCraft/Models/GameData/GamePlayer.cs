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
        // MMR as recorded in the replay itself, i.e. going *into* the game.
        public required long Mmr { get; set; }

        // MMR read back from the Battle.net ladder API shortly after the game, i.e. coming *out* of it.
        // Null whenever it couldn't be determined — no saved API credentials, the profile has no placed
        // ladder this season, the game wasn't ranked 1v1, or the API hadn't caught up before we gave up
        // polling. Only ever populated for the tracked user's own row; opponents' ratings aren't
        // retrievable without knowing their region/realm/profile ids.
        public long? MmrAfter { get; set; }

        public long? MmrChange => MmrAfter.HasValue ? MmrAfter.Value - Mmr : null;
        public required char Race { get; set; }
        public required bool Random { get; set; }

        // The player's actual in-game color (packed 0xAARRGGBB, matching Avalonia's Color.FromUInt32),
        // as assigned by the replay itself — not derivable from anything else about the player. Null for
        // rows recorded before this was captured; ReplayDataExtractor.TryResolvePlayerColorAsync backfills
        // those on demand by re-reading the replay file at GameData.ReplayData.ReplayPath.
        public int? ColorArgb { get; set; }

        public List<int> BuildIds { get; set; } = [];
        public List<GameAttributeValue> AttributeValues { get; set; } = [];
    }
}
