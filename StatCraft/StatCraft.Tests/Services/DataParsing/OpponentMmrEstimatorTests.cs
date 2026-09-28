using StatCraft.Services.DataParsing;

namespace StatCraft.Tests;

public class OpponentMmrEstimatorTests
{
    // Reimplements the forward curve independently of OpponentMmrEstimator, so these tests actually pin
    // the relationship rather than just restating the implementation. Coefficients from the published
    // experimental fit this model came from, which differ slightly from the ones tuned to real ladder
    // history — hence the tolerances below.
    private static double ForwardMmrChange(long playerMmr, long opponentMmr, decimal win)
    {
        if (win != 0m && win != 1m)
            return 0;

        bool won = win == 1m;
        double gap = won ? opponentMmr - playerMmr : playerMmr - opponentMmr;
        double magnitude = Math.Max(22.72 + 0.03115 * gap + 0.000012 * gap * gap, 0);
        return won ? magnitude : -magnitude;
    }

    // Beating someone stronger is worth more than beating someone weaker, and the same gap costs the
    // mirror amount on a loss — the single property the whole model rests on.
    [Fact]
    public void PredictedChange_ScalesWithHowMuchStrongerTheOpponentWas()
    {
        double beatStronger = OpponentMmrEstimator.PredictedChange(4000, 4500, 1m);
        double beatEven = OpponentMmrEstimator.PredictedChange(4000, 4000, 1m);
        double beatWeaker = OpponentMmrEstimator.PredictedChange(4000, 3500, 1m);

        Assert.True(beatStronger > beatEven);
        Assert.True(beatEven > beatWeaker);
        // The mirror: losing to someone that much weaker costs what beating them would have gained.
        Assert.Equal(-beatStronger, OpponentMmrEstimator.PredictedChange(4000, 3500, 0m), 3);
        Assert.Equal(-beatWeaker, OpponentMmrEstimator.PredictedChange(4000, 4500, 0m), 3);
    }

    // Only over the gaps the two fits agree on: past about 700 they diverge (the published one is
    // steeper — 60 points where this one says 52), so the extremes are covered by their own tests below.
    [Theory]
    [InlineData(4000, 4000, 1)]
    [InlineData(4000, 4500, 0)]
    [InlineData(4000, 3500, 1)]
    [InlineData(4000, 4300, 1)]
    public void PredictedChange_MatchesIndependentForwardFormula(long playerMmr, long opponentMmr, decimal win)
    {
        double predicted = OpponentMmrEstimator.PredictedChange(playerMmr, opponentMmr, win);
        double expected = ForwardMmrChange(playerMmr, opponentMmr, win);

        Assert.InRange(predicted, expected - 4, expected + 4);
    }

    // An Elo model can never move a rating by more than its own K factor; this one has no such ceiling,
    // which is the whole reason it replaced it. A real recorded game lost 52 points at this rating gap.
    [Fact]
    public void PredictedChange_LosingToAMuchWeakerOpponent_ExceedsWhatAnEloKFactorAllowed()
    {
        double predicted = OpponentMmrEstimator.PredictedChange(5306, 4368, 0m);

        Assert.InRange(predicted, -56, -48);
    }

    // Beating someone far enough below you really does round to +0 in game, so the reward has to decay
    // to nothing rather than bottoming out — and it must not start growing again at absurd gaps. Exactly
    // where it first reaches zero moves whenever the curve is retuned, so these gaps sit well past it.
    [Theory]
    [InlineData(1500)]
    [InlineData(2000)]
    [InlineData(4000)]
    public void PredictedChange_BeatingAFarWeakerOpponent_DecaysToZero(long gap)
    {
        Assert.Equal(0, OpponentMmrEstimator.PredictedChange(5300, 5300 - gap, 1m));
        Assert.Equal(0, OpponentMmrEstimator.PredictedChange(5300, 5300 + gap, 0m));
    }

    // The tail of the curve is a quadratic on its way back up, so the whole stretch between where it
    // crosses zero and where it turns is negative before it is clamped. Retuning the curve moves that
    // stretch around, and an unclamped win would pay out as a loss — so the sign holds across the range,
    // not just at the gaps the tests above happen to name.
    [Fact]
    public void PredictedChange_NeverContradictsTheResultAtAnyGap()
    {
        for (long gap = 0; gap <= 4000; gap += 50)
        {
            Assert.True(OpponentMmrEstimator.PredictedChange(5300, 5300 - gap, 1m) >= 0, $"win at gap {gap}");
            Assert.True(OpponentMmrEstimator.PredictedChange(5300, 5300 + gap, 0m) <= 0, $"loss at gap {gap}");
        }
    }

    // And it shrinks smoothly on the way there, rather than stepping off a cliff.
    [Fact]
    public void PredictedChange_ShrinksMonotonicallyAsTheOpponentGetsWeaker()
    {
        long[] gaps = [900, 1000, 1100, 1200];
        double[] rewards = gaps.Select(gap => OpponentMmrEstimator.PredictedChange(5300, 5300 - gap, 1m)).ToArray();

        Assert.True(rewards[0] > rewards[1] && rewards[1] > rewards[2] && rewards[2] >= rewards[3]);
        Assert.InRange(rewards[0], 1, 5);
    }

    // PredictedChange and Estimate are meant to be inverses: predicting from a known opponent MMR, then
    // estimating back from that change, should recover the same MMR.
    [Theory]
    [InlineData(4000, 4200, 1)]
    [InlineData(4000, 3800, 0)]
    [InlineData(5300, 4400, 0)]
    [InlineData(5400, 6000, 1)]
    public void PredictedChangeAndEstimate_AreConsistentInverses(long playerMmr, long opponentMmr, decimal win)
    {
        double predicted = OpponentMmrEstimator.PredictedChange(playerMmr, opponentMmr, win);

        long? estimated = OpponentMmrEstimator.Estimate(playerMmr, (long)Math.Round(predicted), win);

        Assert.NotNull(estimated);
        // Rounding the change to a whole number loses precision going back the other way.
        Assert.InRange(estimated.Value, opponentMmr - 25, opponentMmr + 25);
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

    // A draw never moves either player's MMR in StarCraft II, so it says nothing whatsoever about the
    // opponent's rating — every possible opponent is consistent with it.
    [Theory]
    [InlineData(4000, 4000)]
    [InlineData(4000, 5000)]
    public void PredictedChange_Draw_IsAlwaysZero(long playerMmr, long opponentMmr)
    {
        Assert.Equal(0, OpponentMmrEstimator.PredictedChange(playerMmr, opponentMmr, 0.5m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Estimate_Draw_ReturnsNull(long change)
    {
        Assert.Null(OpponentMmrEstimator.Estimate(4000, change, 0.5m));
    }

    // A change whose direction contradicts the result isn't this model's output at all, so there's
    // nothing to invert.
    [Theory]
    [InlineData(-20, 1)]
    [InlineData(20, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 0)]
    public void Estimate_ChangeContradictsTheResult_ReturnsNull(long change, decimal win)
    {
        Assert.Null(OpponentMmrEstimator.Estimate(4000, change, win));
    }

    // A change of nothing is produced by every gap past the flat end of the curve, so it cannot identify
    // the opponent — while the smallest non-zero changes still can, near the tail.
    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(-3, 0)]
    public void Estimate_SmallestNonZeroChanges_StillIdentifyAFarOffOpponent(long change, decimal win)
    {
        long? estimated = OpponentMmrEstimator.Estimate(5300, change, win);

        Assert.NotNull(estimated);
        Assert.InRange(Math.Abs(estimated.Value - 5300), 700, 1200);
    }

    [Fact]
    public void PredictedChange_MatchingRecordedMmr_StaysWithinMaxPlausibleResidualOfActualChange()
    {
        // A game that actually played out as this model predicts (no bad data) should never look
        // suspicious — the same invariant the model was fit to hold across 97 real games.
        double predicted = OpponentMmrEstimator.PredictedChange(4000, 4200, 1m);
        long actualChange = (long)Math.Round(predicted);

        Assert.True(Math.Abs(predicted - actualChange) <= OpponentMmrEstimator.MaxPlausibleResidual);
    }
}
