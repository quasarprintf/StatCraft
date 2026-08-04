using StatCraft.Models.GameData;

namespace StatCraft.Services.DataParsing
{
    // Decides whether a game was Ranked, Unranked, or Custom.
    //
    // Custom is certain: the replay's matchmaking flag is false for anything not queued through
    // matchmaking, which was confirmed against a broad sample of real replays — including custom games
    // played on ladder maps, which a map-name heuristic would get wrong.
    //
    // Ranked vs Unranked is inferred, because the replay carries no flag that separates them. Both modes
    // are matchmade, but they track *different* ratings: a ranked game starts from the player's ranked
    // MMR, an unranked game starts from a separate hidden one. So if the rating the replay recorded
    // going into the game matches the last ranked MMR we knew for that ladder, the game consumed ranked
    // MMR and was therefore ranked.
    //
    // The comparison is deliberately against the *most recently known* ranked MMR rather than the
    // session's opening value: after the first ranked game of a session the ladder has already moved, so
    // a fixed baseline would misread every subsequent ranked game as unranked. An unranked game never
    // moves ranked MMR, so the last known value stays valid across one.
    internal static class GameTypeResolver
    {
        internal static GameType Resolve(ParsedReplayData replay, long? lastKnownRankedMmr)
        {
            if (!replay.IsMatchmade)
                return GameType.Custom;

            // Nothing to compare against — no saved API credentials, an unplaced ladder, or a lookup that
            // failed. Matchmade games are overwhelmingly ranked, so that's the less wrong assumption.
            if (lastKnownRankedMmr is not { } known)
                return GameType.Ranked;

            return known == replay.Player.Mmr ? GameType.Ranked : GameType.Unranked;
        }
    }
}
