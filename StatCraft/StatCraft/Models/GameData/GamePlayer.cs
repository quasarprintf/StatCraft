using StatCraft.Models.GameData.Replays;
using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData;

public class GamePlayer
{
    // What the replay itself reported about this player — name, clan, race, in-game color, and the
    // MMR going into the game. Everything below is recorded by StatCraft after the fact instead.
    public required ReplayPlayer ReplayPlayer { get; set; }

    // MMR read back from the Battle.net ladder API shortly after the game, i.e. coming *out* of it.
    // Null whenever it couldn't be determined — no saved API credentials, the profile has no placed
    // ladder this season, the game wasn't ranked 1v1, or the API hadn't caught up before we gave up
    // polling. Only ever populated for the tracked user's own row; opponents' ratings aren't
    // retrievable without knowing their region/realm/profile ids.
    public long? MmrAfter { get; set; }

    public long? MmrChange => MmrAfter.HasValue ? MmrAfter.Value - ReplayPlayer.Mmr.Mmr : null;

    public List<int> BuildIds { get; set; } = [];
    public List<BuildDetailValue> BuildDetailValues { get; set; } = [];
}
