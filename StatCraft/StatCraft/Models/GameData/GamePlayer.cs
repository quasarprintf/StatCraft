using StatCraft.Models.GameData.Replays;
using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData;

public class GamePlayer
{
    public int? GamePlayerId { get; set; }
    public ReplayPlayer ReplayPlayer { get; set; }

    public long? MmrAfter { get; set; } //mmr after the game, per battlenet api
    public long? MmrChange => MmrAfter.HasValue ? MmrAfter.Value - ReplayPlayer.Mmr.Mmr : null;

    public List<int> BuildIds { get; set; } = [];
    public BuildDetailValues BuildDetailValues { get; set; } = new();

    public GamePlayer(ReplayPlayer replayPlayer)
    {
        ReplayPlayer = replayPlayer;
    }
}
