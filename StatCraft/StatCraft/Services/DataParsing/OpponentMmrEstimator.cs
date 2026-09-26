using System;

namespace StatCraft.Services.DataParsing;

// Models how the ladder moves a player's MMR for one game, so a replay-parsed opponent MMR can be
// sanity-checked and, when it's garbage, re-derived from the player's own observed MMR change.
//
// The magnitude of the change is quadratic in how much stronger the *other* player was — the same
// curve for a win and for a loss, mirrored:
//
//     magnitude = Base + Slope * gap + Curvature * gap^2
//     gap       = opponentMmr - playerMmr  on a win
//                 playerMmr - opponentMmr  on a loss
//
// This replaced an Elo model (K * (score - expected)), which structurally could not fit the data: an
// Elo change can never exceed its own K factor, yet one recorded game swings 52 points while the bulk
// of them pin K at about 43.9. Refit against 97 ranked 1v1 games from a real ladder history
// (2026-09-26), this curve lands within 3.2 points of every one of them — mean error 0.41, against
// 0.55 for the best Elo fit — and explains the five games Elo could not at all.
//
// Draws are not modelled: StarCraft II never changes either player's MMR for a draw, so a drawn game
// carries no information about the opponent's rating.
internal static class OpponentMmrEstimator
{
    // Fit (least squares) over those 97 games. Coefficients this shape were first published from a
    // separate experimental fit (22.76 / 0.03092 / 0.000011 on a win, 22.68 / 0.03138 / 0.000013 on a
    // loss) — close enough to corroborate the model, while these are tuned to this ladder history.
    private const double Base = 21.55;
    private const double Slope = 0.0275;
    private const double Curvature = 0.0000070;

    // Past a certain gap the ladder stops scaling the reward down and just awards a few points — the
    // recorded games flatten out at 3 (observed at gaps of 920 and 950 alike). Also keeps the curve
    // from turning back down at extreme gaps, where it would otherwise predict a negative magnitude.
    private const double MinimumChange = 3.0;

    // Every one of those 97 games lands within 3.2 of its own prediction (the spread is rounding, since
    // MMR is whole numbers), while games with a genuinely garbage opponent MMR miss by 6.6 or more.
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
