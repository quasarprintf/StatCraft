using System;
using System.Collections.Generic;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Maps;
using StatCraft.Models.GameData.Race;

namespace StatCraft.Services.DataFiltering
{
    // A null or empty set (or null bound, for the numeric/date ranges) on any dimension means "inactive"
    // — no restriction from that dimension, whether because its filter chip is hidden or visible with
    // nothing checked/entered yet.
    internal record GameFilterCriteria(
        DateOnly? FromDate,
        DateOnly? ToDate,
        IReadOnlySet<Map>? Maps,
        IReadOnlySet<(Race Player, Race Opponent)>? MatchupPairs,
        IReadOnlySet<GameOutcome>? Outcomes,
        long? MinOpponentMmr,
        long? MaxOpponentMmr,
        IReadOnlySet<int>? BuildIds)
    {
        internal static readonly GameFilterCriteria Empty = new(null, null, null, null, null, null, null, null);
    }
}
