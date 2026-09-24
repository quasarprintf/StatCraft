using StatCraft.Models.GameData.Maps;
using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Models.GameData.Replays;

public class ParsedReplayData
{
    public required Map Map { get; set; }
    public int GameLengthSeconds { get; set; }
    public required string ReplayPath { get; set; }
    public required DateTimeOffset ReplayTimestamp { get; set; }
    public decimal Win { get; set; } //0 = lose, 1 = win, 0.5 = draw
    public required ReplayPlayer Player { get; set; }
    public ReplayPlayer[] Allies { get; set; } = Array.Empty<ReplayPlayer>();
    public required ReplayPlayer[] Opponents { get; set; }
    public bool IsMatchmade { get; set; }

    public bool IsRatedOneVsOne => Allies.Length == 0 && Opponents.Length == 1 && Player.Mmr.Mmr > 0;
}
