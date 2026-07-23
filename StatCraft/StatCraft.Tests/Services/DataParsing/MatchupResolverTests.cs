using StatCraft.Models.GameData;
using StatCraft.Services.DataParsing;
using StatCraft.ViewModels;

namespace StatCraft.Tests;

public class MatchupResolverTests
{
    [Theory]
    [InlineData('P', Matchup.VsP)]
    [InlineData('T', Matchup.VsT)]
    [InlineData('Z', Matchup.VsZ)]
    public void FromOpponents_KnownRace_ReturnsMatchingMatchup(char race, Matchup expected)
    {
        GamePlayer[] opponents = [CreateOpponent(race)];

        Matchup? result = MatchupResolver.FromOpponents(opponents);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FromOpponents_UnresolvedRace_ReturnsNull()
    {
        GamePlayer[] opponents = [CreateOpponent('?')];

        Matchup? result = MatchupResolver.FromOpponents(opponents);

        Assert.Null(result);
    }

    [Fact]
    public void FromOpponents_NoOpponents_ReturnsNull()
    {
        Matchup? result = MatchupResolver.FromOpponents([]);

        Assert.Null(result);
    }

    [Fact]
    public void FromOpponents_UsesFirstOpponentOnly()
    {
        GamePlayer[] opponents = [CreateOpponent('T'), CreateOpponent('Z')];

        Matchup? result = MatchupResolver.FromOpponents(opponents);

        Assert.Equal(Matchup.VsT, result);
    }

    private static GamePlayer CreateOpponent(char race) => new()
    {
        Name = "Opponent",
        Clan = "",
        Mmr = 0,
        Race = race,
        Random = false,
    };
}
