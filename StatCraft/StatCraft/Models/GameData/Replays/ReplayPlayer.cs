using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData.Replays;

// Everything about a player that the replay file itself reports. A GamePlayer wraps one of these and
// adds what StatCraft records on top of it afterwards (post-game MMR, build selections).
public class ReplayPlayer
{
    public int? GamePlayerId { get; set; }

    public required string Clan { get; set; }
    public string FormattedClan => string.IsNullOrWhiteSpace(Clan) ? "" : $"[{Clan}]";
    public required string Name { get; set; }
    // MMR going *into* the game — see PlayerMmr for how ParsedMmr/EstimatedMmr/OverrideMmr resolve.
    public required PlayerMmr Mmr { get; set; }

    public required char Race { get; set; }
    public required bool Random { get; set; }

    // The player's actual in-game color (packed 0xAARRGGBB, matching Avalonia's Color.FromUInt32),
    // as assigned by the replay itself — not derivable from anything else about the player. Null for
    // rows recorded before this was captured; ReplayDataExtractor.TryResolvePlayerColorAsync backfills
    // those on demand by re-reading the replay file at GameData.ReplayData.ReplayPath.
    public int? ColorArgb { get; set; }
}
