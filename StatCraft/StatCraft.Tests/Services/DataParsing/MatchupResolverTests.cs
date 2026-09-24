using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Race;
using StatCraft.Models.GameData.Replays;
using StatCraft.Services.DataParsing;

namespace StatCraft.Tests;

public class MatchupResolverTests
{
    [Theory]
    [InlineData('Z', Matchups.VsZ)]
    [InlineData('T', Matchups.VsT)]
    [InlineData('P', Matchups.VsP)]
    public void FromOpponents_SingleKnownRace_ReturnsMatchingFlag(char race, Matchups expected)
    {
        ReplayPlayer[] opponents = [CreateOpponent(race)];

        Matchups result = MatchupResolver.FromOpponents(opponents);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FromOpponents_UnresolvedRace_ReturnsNone()
    {
        ReplayPlayer[] opponents = [CreateOpponent('?')];

        Matchups result = MatchupResolver.FromOpponents(opponents);

        Assert.Equal(Matchups.None, result);
    }

    [Fact]
    public void FromOpponents_NoOpponents_ReturnsNone()
    {
        Matchups result = MatchupResolver.FromOpponents([]);

        Assert.Equal(Matchups.None, result);
    }

    [Fact]
    public void FromOpponents_MultipleDifferentRaces_CombinesFlags()
    {
        ReplayPlayer[] opponents = [CreateOpponent('T'), CreateOpponent('Z')];

        Matchups result = MatchupResolver.FromOpponents(opponents);

        Assert.Equal(Matchups.VsT | Matchups.VsZ, result);
    }

    [Fact]
    public void FromOpponents_MultipleSameRace_DoesNotDuplicateFlag()
    {
        ReplayPlayer[] opponents = [CreateOpponent('Z'), CreateOpponent('Z')];

        Matchups result = MatchupResolver.FromOpponents(opponents);

        Assert.Equal(Matchups.VsZ, result);
    }

    [Fact]
    public void FromOpponents_MixOfKnownAndUnresolvedRace_IgnoresUnresolved()
    {
        ReplayPlayer[] opponents = [CreateOpponent('P'), CreateOpponent('?')];

        Matchups result = MatchupResolver.FromOpponents(opponents);

        Assert.Equal(Matchups.VsP, result);
    }

    [Theory]
    [InlineData('Z', Race.Zerg)]
    [InlineData('T', Race.Terran)]
    [InlineData('P', Race.Protoss)]
    public void AsRace_KnownChar_ReturnsMatchingRace(char raw, Race expected)
    {
        Race? result = raw.AsRace();

        Assert.Equal(expected, result);
    }

    [Fact]
    public void AsRace_UnknownChar_ReturnsNull()
    {
        Race? result = '?'.AsRace();

        Assert.Null(result);
    }

    private static ReplayPlayer CreateOpponent(char race) => new()
    {
        Name = "Opponent",
        Clan = "",
        Mmr = new PlayerMmr { ParsedMmr = 0 },
        Race = race,
        Random = false,
    };
}
