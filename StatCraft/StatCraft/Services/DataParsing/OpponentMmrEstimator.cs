using System;

namespace StatCraft.Services.DataParsing;

internal static class OpponentMmrEstimator
{
    //follows the approach outlined in https://www.reddit.com/r/starcraft/comments/hbz39r/mmr_change_analysis_calculate_each_matchups_mmr/
    //with constants modified a bit to match my own data, and combined into one equation for both win and loss, instead of separate equations per the reddit post
    private const double Base = 21.56;
    private const double Slope = 0.0281;
    private const double Curvature = 0.0000080;

    internal const double MaxPlausibleResidual = 3.5;

    // The curve is only meaningful while it is still falling: past its vertex a quadratic turns back up,
    // which would predict a *growing* reward for beating an ever-weaker opponent.
    private const double FlatGap = -Slope / (2 * Curvature);

    // What the tracked player's own MMR change should have been, given the recorded opponent MMR under
    // this model — compare against their actual change (see MaxPlausibleResidual) to judge whether the
    // recorded opponent MMR is trustworthy. Always 0 for a draw. Decays to 0 for a big enough gap, since
    // beating someone far enough below you really does round to +0 (and losing to someone far above, -0).
    internal static double PredictedChange(long playerMmr, long opponentMmr, decimal playerWin)
    {
        if (playerWin != 0m && playerWin != 1m)
            return 0;

        bool won = playerWin == 1m;
        double gap = won ? opponentMmr - playerMmr : playerMmr - opponentMmr;
        double magnitude = gap <= FlatGap ? 0 : Math.Max(Magnitude(gap), 0);
        return won ? magnitude : -magnitude;
    }

    // Returns the estimated pre-game MMR for the opponent — the inverse of PredictedChange, solving for
    // the opponent MMR whose gap would have produced playerMmrChange exactly — or null when the change
    // can't identify one:
    //   - a draw, which never moves either MMR and so says nothing about the opponent
    //   - a change whose direction contradicts the result
    //   - no change at all, which every gap past the flat end of the curve produces
    internal static long? Estimate(long playerMmr, long playerMmrChange, decimal playerWin)
    {
        if (playerWin != 0m && playerWin != 1m)
            return null;

        if (playerMmrChange == 0)
            return null;

        bool won = playerWin == 1m;
        if (won != playerMmrChange > 0)
            return null;

        // Magnitude(gap) = |change|, solved as a quadratic in gap. The larger root is the only one on the
        // falling branch, so it is the only meaningful answer.
        double magnitude = Math.Abs(playerMmrChange);
        double discriminant = Slope * Slope - 4 * Curvature * (Base - magnitude);
        if (discriminant < 0)
            return null;

        double gap = (-Slope + Math.Sqrt(discriminant)) / (2 * Curvature);
        return (long)Math.Round(won ? playerMmr + gap : playerMmr - gap);
    }

    private static double Magnitude(double gap) => Base + Slope * gap + Curvature * gap * gap;
}
