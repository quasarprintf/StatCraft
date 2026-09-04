using System;

namespace StatCraft.Services.DataParsing
{
    // Estimates an opponent's pre-game MMR from the tracked player's own known MmrChange for that game,
    // by inverting the standard Elo rating-update formula. Blizzard doesn't publish SC2's actual K-factor
    // or rating-scale divisor, so the constants below are community-derived estimates, not confirmed
    // values — treat Estimate's result as an approximation, useful for catching an opponent MMR that's
    // wildly implausible (see ReplayDataExtractor's own ScaledRating guard for a confirmed example of
    // replay-parsed MMR being garbage), not as ground truth to prefer over a plausible recorded value.
    internal static class OpponentMmrEstimator
    {
        // Community-estimated SC2 rating-scale divisor (the "D" in 1 / (1 + 10^(diff/D))), from
        // FluffyMaguro/SC2-MMR-Stats' observed ΔELO = ΔMMR / 2.2 conversion off the standard chess Elo
        // divisor of 400 (400 * 2.2 = 880).
        private const double RatingScaleDivisor = 880.0;

        // Best-effort default K-factor (per-game MMR-change magnitude cap) for an established player —
        // not Blizzard-confirmed. Not valid for a player still in their placement matches, whose swings
        // are known to be larger than this.
        private const double KFactor = 32.0;

        // Returns the estimated pre-game MMR for the opponent, or null if playerMmrChange/playerWin don't
        // correspond to a mathematically valid probability under these constants (e.g. a magnitude of
        // change larger than KFactor itself allows) — callers should leave the recorded MMR alone in that
        // case rather than guess further.
        internal static long? Estimate(long playerMmr, long playerMmrChange, decimal playerWin)
        {
            double expectedScore = (double)playerWin - playerMmrChange / KFactor;
            if (expectedScore <= 0 || expectedScore >= 1)
                return null;

            double ratingDiff = RatingScaleDivisor * Math.Log10(1 / expectedScore - 1);
            return (long)Math.Round(playerMmr + ratingDiff);
        }
    }
}
