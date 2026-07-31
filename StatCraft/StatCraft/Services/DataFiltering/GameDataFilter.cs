using System;
using System.Collections.Generic;
using System.Linq;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DataParsing;

namespace StatCraft.Services.DataFiltering
{
    // Pure, Avalonia-free predicate over one game's data — the testable heart of the Data tab's filter
    // bar. Every dimension is ANDed together; matchup/MMR/build each OR across the game's own
    // opponents/build list, since a game can have more than one opponent (team games).
    internal static class GameDataFilter
    {
        internal static bool Matches(GameData game, GameFilterCriteria criteria)
        {
            ParsedReplayData replay = game.ReplayData;

            DateOnly played = DateOnly.FromDateTime(replay.ReplayTimestamp.ToLocalTime().DateTime);
            if (criteria.FromDate is { } from && played < from)
                return false;
            if (criteria.ToDate is { } to && played > to)
                return false;

            if (HasAny(criteria.Maps) && !criteria.Maps!.Contains(replay.MapName))
                return false;

            if (HasAny(criteria.Outcomes) && !criteria.Outcomes!.Contains(GameOutcomeExtensions.FromWin(replay.Win)))
                return false;

            if (HasAny(criteria.MatchupPairs))
            {
                Race? selfRace = replay.Player.Race.AsRace();
                bool anyMatch = selfRace != null && criteria.MatchupPairs!.Any(pair =>
                    pair.Player == selfRace && replay.Opponents.Any(o => o.Race.AsRace() == pair.Opponent));
                if (!anyMatch)
                    return false;
            }

            if (criteria.MinOpponentMmr != null || criteria.MaxOpponentMmr != null)
            {
                long min = criteria.MinOpponentMmr ?? long.MinValue;
                long max = criteria.MaxOpponentMmr ?? long.MaxValue;
                if (!replay.Opponents.Any(o => o.Mmr >= min && o.Mmr <= max))
                    return false;
            }

            if (HasAny(criteria.BuildIds) && !replay.Player.BuildIds.Any(id => criteria.BuildIds!.Contains(id)))
                return false;

            return true;
        }

        private static bool HasAny<T>(IReadOnlySet<T>? set) => set != null && set.Count > 0;

        // A build node's own id plus every descendant id — checking a build in the filter should also
        // match games where a more specific build beneath it was selected. Mirrors the same subtree
        // collection already used elsewhere for build-deletion reference checks.
        internal static IEnumerable<int> CollectSubtreeIds(BuildNode node)
        {
            yield return node.Id;
            foreach (BuildNode child in node.Children)
                foreach (int id in CollectSubtreeIds(child))
                    yield return id;
        }
    }
}
