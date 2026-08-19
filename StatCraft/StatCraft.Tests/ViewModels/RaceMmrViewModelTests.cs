using StatCraft.Models.GameData.Race;
using StatCraft.Styles;
using StatCraft.ViewModels;

namespace StatCraft.Tests;

public class RaceMmrViewModelTests
{
    [Fact]
    public void NoSessionBaseline_ShowsRatingOnly()
    {
        // A ladder that wasn't placed when the session started has nothing to measure against.
        RaceMmrViewModel vm = new(LadderRace.Protoss, 5239, null);

        Assert.Null(vm.SessionChange);
        ColoredCharacter only = Assert.Single(vm.Characters);
        Assert.Equal("P: 5239", only.Text);
    }

    [Fact]
    public void UnchangedSinceSessionStart_SuppressesZeroDelta()
    {
        RaceMmrViewModel vm = new(LadderRace.Protoss, 5239, 5239);

        Assert.Equal(0, vm.SessionChange);
        Assert.Single(vm.Characters);
    }

    [Fact]
    public void GainedSinceSessionStart_AppendsPositiveDeltaInWinColour()
    {
        RaceMmrViewModel vm = new(LadderRace.Protoss, 5263, 5239);

        Assert.Equal(24, vm.SessionChange);
        Assert.Equal(2, vm.Characters.Count);
        Assert.Equal("P: 5263", vm.Characters[0].Text);
        Assert.Equal("(+24)", vm.Characters[1].Text);
        Assert.Equal(Colors.WinGreen, vm.Characters[1].Color);
    }

    [Fact]
    public void LostSinceSessionStart_AppendsNegativeDeltaInLossColour()
    {
        RaceMmrViewModel vm = new(LadderRace.Zerg, 4076, 4100);

        Assert.Equal(-24, vm.SessionChange);
        Assert.Equal("(-24)", vm.Characters[1].Text);
        Assert.Equal(Colors.LossRed, vm.Characters[1].Color);
    }

    [Fact]
    public void DeltaAccumulatesAcrossSession_NotJustTheLastGame()
    {
        // Baseline stays fixed at session start, so three wins read as one running total.
        RaceMmrViewModel afterThree = new(LadderRace.Terran, 4180, 4100);

        Assert.Equal(80, afterThree.SessionChange);
        Assert.Equal("(+80)", afterThree.Characters[1].Text);
    }

    [Theory]
    [InlineData(LadderRace.Protoss)]
    [InlineData(LadderRace.Terran)]
    [InlineData(LadderRace.Zerg)]
    [InlineData(LadderRace.Random)]
    public void RatingRunIsAlwaysRaceColoured(LadderRace race)
    {
        RaceMmrViewModel vm = new(race, 4000, 3900);

        Assert.NotNull(vm.Characters[0].Color);
        // The delta is coloured by outcome, never by race.
        Assert.Equal(Colors.WinGreen, vm.Characters[1].Color);
    }
}
