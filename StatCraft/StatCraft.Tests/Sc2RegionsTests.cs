using StatCraft.Models.Battlenet;

namespace StatCraft.Tests;

public class Sc2RegionsTests
{
    [Theory]
    [InlineData("1", "NA")]
    [InlineData("2", "EU")]
    [InlineData("3", "KR")]
    [InlineData("5", "CN")]
    public void GetLabel_KnownRegionId_ReturnsExpectedLabel(string regionId, string expectedLabel)
    {
        Assert.Equal(expectedLabel, Sc2Regions.GetLabel(regionId));
    }

    [Fact]
    public void GetLabel_UnknownRegionId_ReturnsRegionIdItself()
    {
        Assert.Equal("99", Sc2Regions.GetLabel("99"));
    }
}
