using StatCraft.Services.DataParsing;

namespace StatCraft.Tests;

public class OpponentMmrEstimatorTests
{
    private const double K = 43.55;
    private const double D = 850.0;

    // Reimplements the forward Elo formula independently of OpponentMmrEstimator, so these tests actually
    // pin the inverse relationship rather than just restating the implementation.
    private static long ForwardMmrChange(long playerMmr, long opponentMmr, decimal win)
    {
        double expectedScore = 1.0 / (1.0 + Math.Pow(10, (opponentMmr - playerMmr) / D));
        return (long)Math.Round(K * ((double)win - expectedScore));
    }

    // Kept away from the KFactor boundary (a large rating gap pushes expectedScore close to 0 or 1, where
    // the inverse function's sensitivity to ForwardMmrChange's own rounding blows up) — the boundary
    // itself is covered separately below by the ReturnsNull theories.
    [Theory]
    [InlineData(4000, 4000, 1)]
    [InlineData(4000, 4150, 1)] // beating a moderately stronger opponent
    [InlineData(4000, 3850, 0)] // losing to a moderately weaker opponent
    [InlineData(2000, 2100, 0.5)]
    public void Estimate_RoundTrip_RecoversKnownOpponentMmr(long playerMmr, long opponentMmr, decimal win)
    {
        long change = ForwardMmrChange(playerMmr, opponentMmr, win);

        long? estimated = OpponentMmrEstimator.Estimate(playerMmr, change, win);

        Assert.NotNull(estimated);
        // Rounding the forward change to a whole number loses some precision going back the other way.
        Assert.InRange(estimated.Value, opponentMmr - 20, opponentMmr + 20);
    }

    [Fact]
    public void Estimate_DrawWithNoChange_ReturnsPlayersOwnMmr()
    {
        long? estimated = OpponentMmrEstimator.Estimate(4000, 0, 0.5m);

        Assert.Equal(4000, estimated);
    }

    [Theory]
    [InlineData(44)]  // above K: expectedScore would be <= 0, the open boundary
    [InlineData(50)]
    public void Estimate_WinWithChangeAtOrAboveK_ReturnsNull(long change)
    {
        Assert.Null(OpponentMmrEstimator.Estimate(4000, change, 1m));
    }

    [Theory]
    [InlineData(-44)] // below -K: expectedScore would be >= 1, the open boundary
    [InlineData(-50)]
    public void Estimate_LossWithChangeAtOrBelowNegativeK_ReturnsNull(long change)
    {
        Assert.Null(OpponentMmrEstimator.Estimate(4000, change, 0m));
    }

    [Fact]
    public void Estimate_WinWithLargeGain_EstimatesOpponentWellAboveThePlayer()
    {
        long? estimated = OpponentMmrEstimator.Estimate(4000, 40, 1m);

        Assert.NotNull(estimated);
        Assert.True(estimated.Value > 4000 + 500);
    }

    [Fact]
    public void Estimate_LossWithLargeDrop_EstimatesOpponentWellBelowThePlayer()
    {
        long? estimated = OpponentMmrEstimator.Estimate(4000, -40, 0m);

        Assert.NotNull(estimated);
        Assert.True(estimated.Value < 4000 - 500);
    }

    [Theory]
    [InlineData(4000, 4000, 1)]
    [InlineData(4000, 4500, 0)]
    [InlineData(4000, 3500, 0.5)]
    public void PredictedChange_MatchesIndependentForwardFormula(long playerMmr, long opponentMmr, decimal win)
    {
        double predicted = OpponentMmrEstimator.PredictedChange(playerMmr, opponentMmr, win);

        Assert.InRange(predicted, ForwardMmrChange(playerMmr, opponentMmr, win) - 0.6, ForwardMmrChange(playerMmr, opponentMmr, win) + 0.6);
    }

    // PredictedChange and Estimate are meant to be inverses of each other: predicting from a known
    // opponent MMR, then estimating back from that predicted change, should recover the same MMR.
    [Theory]
    [InlineData(4000, 4200, 1)]
    [InlineData(4000, 3800, 0)]
    [InlineData(5000, 5000, 0.5)]
    public void PredictedChangeAndEstimate_AreConsistentInverses(long playerMmr, long opponentMmr, decimal win)
    {
        double predicted = OpponentMmrEstimator.PredictedChange(playerMmr, opponentMmr, win);

        long? estimated = OpponentMmrEstimator.Estimate(playerMmr, (long)Math.Round(predicted), win);

        Assert.NotNull(estimated);
        Assert.InRange(estimated.Value, opponentMmr - 25, opponentMmr + 25);
    }

    [Fact]
    public void PredictedChange_MatchingRecordedMmr_StaysWithinMaxPlausibleResidualOfActualChange()
    {
        // A game that actually played out exactly as this model predicts (no bad data) should never look
        // suspicious — the same invariant OpponentMmrEstimator was fit to hold across 45 real games.
        double predicted = OpponentMmrEstimator.PredictedChange(4000, 4200, 1m);
        long actualChange = (long)Math.Round(predicted);

        Assert.True(Math.Abs(predicted - actualChange) <= OpponentMmrEstimator.MaxPlausibleResidual);
    }
}
