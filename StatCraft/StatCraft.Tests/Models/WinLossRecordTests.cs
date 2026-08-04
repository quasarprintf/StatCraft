using StatCraft.Models.GameData;

namespace StatCraft.Tests;

public class WinLossRecordTests
{
    [Fact]
    public void Empty_HasNoRateAndBlankLabel()
    {
        WinLossRecord record = WinLossRecord.From([]);

        Assert.Null(record.WinRate);
        Assert.Equal("", record.Label);
    }

    [Fact]
    public void CountsEachOutcome()
    {
        WinLossRecord record = WinLossRecord.From(
            [GameOutcome.Win, GameOutcome.Win, GameOutcome.Loss, GameOutcome.Draw]);

        Assert.Equal(2, record.Wins);
        Assert.Equal(1, record.Losses);
        Assert.Equal(1, record.Draws);
        Assert.Equal(4, record.Total);
    }

    [Fact]
    public void DrawCountsAsHalfAWin()
    {
        // One win, one draw: 1.5 of 2 = 75%.
        WinLossRecord record = WinLossRecord.From([GameOutcome.Win, GameOutcome.Draw]);

        Assert.Equal(0.75, record.WinRate!.Value, 5);
    }

    [Fact]
    public void AllDraws_IsExactlyHalf()
    {
        WinLossRecord record = WinLossRecord.From([GameOutcome.Draw, GameOutcome.Draw]);

        Assert.Equal(0.5, record.WinRate!.Value, 5);
    }

    [Fact]
    public void DrawsSitInBothHalvesOfTheRatio()
    {
        // 2 wins, 1 loss, 1 draw -> 2.5 of 4 = 62.5%. Discarding the draw would give 66.7% instead.
        WinLossRecord record = WinLossRecord.From(
            [GameOutcome.Win, GameOutcome.Win, GameOutcome.Loss, GameOutcome.Draw]);

        Assert.Equal(0.625, record.WinRate!.Value, 5);
    }

    [Fact]
    public void LabelOmitsDrawCountWhenThereAreNone()
    {
        WinLossRecord record = WinLossRecord.From([GameOutcome.Win, GameOutcome.Loss]);

        Assert.StartsWith("1-1", record.Label);
        Assert.DoesNotContain("1-1-", record.Label);
    }

    [Fact]
    public void LabelIncludesDrawCountWhenPresent()
    {
        WinLossRecord record = WinLossRecord.From([GameOutcome.Win, GameOutcome.Loss, GameOutcome.Draw]);

        Assert.StartsWith("1-1-1", record.Label);
    }

    [Fact]
    public void LabelShowsRecordAlongsideRate()
    {
        // A bare percentage hides how few games it came from.
        WinLossRecord record = WinLossRecord.From([GameOutcome.Win]);

        Assert.StartsWith("1-0", record.Label);
        Assert.Contains("100", record.Label);
    }
}
