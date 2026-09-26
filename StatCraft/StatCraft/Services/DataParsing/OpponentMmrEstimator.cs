using System;

namespace StatCraft.Services.DataParsing;

internal static class OpponentMmrEstimator
{
    //follows the approach outlined in https://www.reddit.com/r/starcraft/comments/hbz39r/mmr_change_analysis_calculate_each_matchups_mmr/
    //with constants modified a bit to match my own data, and combined into one equation for both win and loss, instead of separate equations per the reddit post
    private const double Base = 21.55;
    private const double Slope = 0.0275;
    private const double Curvature = 0.0000070;

    private const double MinimumChange = 3.0;

    internal const double MaxPlausibleResidual = 3.5;

    // What the tracked player's own MMR change should have been, given the recorded opponent MMR under
    // this model — compare against their actual change (see MaxPlausibleResidual) to judge whether the
    // recorded opponent MMR is trustworthy. Always 0 for a draw.
    internal static double PredictedChange(long playerMmr, long opponentMmr, decimal playerWin)
    {
        if (playerWin != 0m && playerWin != 1m)
            return 0;

        bool won = playerWin == 1m;
        double gap = won ? opponentMmr - playerMmr : playerMmr - opponentMmr;
        double magnitude = Math.Max(Magnitude(gap), MinimumChange);
        return won ? magnitude : -magnitude;
    }

    // Returns the estimated pre-game MMR for the opponent — the inverse of PredictedChange, solving for
    // the opponent MMR whose gap would have produced playerMmrChange exactly — or null when the change
    // can't identify one:
    //   - a draw, which never moves either MMR and so says nothing about the opponent
    //   - a change whose direction contradicts the result
    //   - a change at or below MinimumChange, where every large enough gap gives the same answer
    internal static long? Estimate(long playerMmr, long playerMmrChange, decimal playerWin)
    {
        if (playerWin != 0m && playerWin != 1m)
            return null;

        bool won = playerWin == 1m;
        if (won != playerMmrChange > 0)
            return null;

        double magnitude = Math.Abs(playerMmrChange);
        if (magnitude <= MinimumChange)
            return null;

        // Magnitude(gap) = magnitude, solved as a quadratic in gap. Over any real rating gap the curve
        // is increasing (its vertex sits near -1960), so the larger root is the only meaningful one.
        double discriminant = Slope * Slope - 4 * Curvature * (Base - magnitude);
        if (discriminant < 0)
            return null;

        double gap = (-Slope + Math.Sqrt(discriminant)) / (2 * Curvature);
        return (long)Math.Round(won ? playerMmr + gap : playerMmr - gap);
    }

    private static double Magnitude(double gap) => Base + Slope * gap + Curvature * gap * gap;
}
