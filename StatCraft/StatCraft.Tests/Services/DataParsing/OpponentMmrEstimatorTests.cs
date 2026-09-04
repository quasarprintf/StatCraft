using StatCraft.Services.DataParsing;

namespace StatCraft.Tests;

public class OpponentMmrEstimatorTests
{
    private const double K = 32.0;
    private const double D = 880.0;

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
        Assert.InRange(estimated.Value, opponentMmr - 5, opponentMmr + 5);
    }

    [Fact]
    public void Estimate_DrawWithNoChange_ReturnsPlayersOwnMmr()
    {
        long? estimated = OpponentMmrEstimator.Estimate(4000, 0, 0.5m);

        Assert.Equal(4000, estimated);
    }

    [Theory]
    [InlineData(32)]  // exactly K: expectedScore hits 0, the open boundary
    [InlineData(40)]  // larger than K is possible under real Elo is impossible under a win
    public void Estimate_WinWithChangeAtOrAboveK_ReturnsNull(long change)
    {
        Assert.Null(OpponentMmrEstimator.Estimate(4000, change, 1m));
    }

    [Theory]
    [InlineData(-32)] // exactly -K: expectedScore hits 1, the open boundary
    [InlineData(-40)]
    public void Estimate_LossWithChangeAtOrBelowNegativeK_ReturnsNull(long change)
    {
        Assert.Null(OpponentMmrEstimator.Estimate(4000, change, 0m));
    }

    [Fact]
    public void Estimate_WinWithLargeGain_EstimatesOpponentWellAboveThePlayer()
    {
        long? estimated = OpponentMmrEstimator.Estimate(4000, 30, 1m);

        Assert.NotNull(estimated);
        Assert.True(estimated.Value > 4000 + 500);
    }

    [Fact]
    public void Estimate_LossWithLargeDrop_EstimatesOpponentWellBelowThePlayer()
    {
        long? estimated = OpponentMmrEstimator.Estimate(4000, -30, 0m);

        Assert.NotNull(estimated);
        Assert.True(estimated.Value < 4000 - 500);
    }
}
