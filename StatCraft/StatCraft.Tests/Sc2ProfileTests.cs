using StatCraft.Models;

namespace StatCraft.Tests;

public class Sc2ProfileTests
{
    [Fact]
    public void RegionLabel_IsDerivedFromRegionId()
    {
        Sc2Profile profile = new Sc2Profile { RegionId = "3" };
        Assert.Equal("KR", profile.RegionLabel);
    }

    [Fact]
    public void DisplayName_CombinesRegionLabelAndName()
    {
        Sc2Profile profile = new Sc2Profile { RegionId = "2", Name = "Serral" };
        Assert.Equal("EU - Serral", profile.DisplayName);
    }
}
