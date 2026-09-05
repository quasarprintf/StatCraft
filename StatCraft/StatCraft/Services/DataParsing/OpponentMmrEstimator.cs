using System;

namespace StatCraft.Services.DataParsing
{
    internal static class OpponentMmrEstimator
    {
        //TODO: low confidence in these values, revisit later
        private const double RatingScaleDivisor = 840.0;
        private const double KFactor = 43.5;

        internal const double MaxPlausibleResidual = 2.0;

        // What the tracked player's own MmrChange should have been, given the recorded opponent MMR under
        // this Elo model — compare against their actual MmrChange (see MaxPlausibleResidual) to judge
        // whether the recorded opponent MMR is trustworthy.
        internal static double PredictedChange(long playerMmr, long opponentMmr, decimal playerWin)
        {
            double expectedScore = 1.0 / (1.0 + Math.Pow(10, (opponentMmr - playerMmr) / RatingScaleDivisor));
            return KFactor * ((double)playerWin - expectedScore);
        }

        // Returns the estimated pre-game MMR for the opponent — the inverse of PredictedChange, solving
        // for the opponent MMR that would have produced playerMmrChange exactly — or null if
        // playerMmrChange/playerWin don't correspond to a mathematically valid probability under these
        // constants (e.g. a magnitude of change larger than KFactor itself allows).
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
