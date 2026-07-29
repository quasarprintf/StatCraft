using StatCraft.Models.GameData;
using StatCraft.Services.DataParsing;
using StatCraft.ViewModels;

namespace StatCraft.Tests;

public class MatchupResolverTests
{
    [Theory]
    [InlineData('Z', 'Z', Matchup.ZvZ)]
    [InlineData('Z', 'T', Matchup.ZvT)]
    [InlineData('Z', 'P', Matchup.ZvP)]
    [InlineData('T', 'Z', Matchup.TvZ)]
    [InlineData('T', 'T', Matchup.TvT)]
    [InlineData('T', 'P', Matchup.TvP)]
    [InlineData('P', 'Z', Matchup.PvZ)]
    [InlineData('P', 'T', Matchup.PvT)]
    [InlineData('P', 'P', Matchup.PvP)]
    public void FromPlayerAndOpponents_KnownRaces_ReturnsMatchingMatchup(char playerRace, char opponentRace, Matchup expected)
    {
        GamePlayer[] opponents = [CreateOpponent(opponentRace)];

        Matchup? result = MatchupResolver.FromPlayerAndOpponents(playerRace, opponents);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FromPlayerAndOpponents_UnresolvedPlayerRace_ReturnsNull()
    {
        GamePlayer[] opponents = [CreateOpponent('Z')];

        Matchup? result = MatchupResolver.FromPlayerAndOpponents('?', opponents);

        Assert.Null(result);
    }

    [Fact]
    public void FromPlayerAndOpponents_UnresolvedOpponentRace_ReturnsNull()
    {
        GamePlayer[] opponents = [CreateOpponent('?')];

        Matchup? result = MatchupResolver.FromPlayerAndOpponents('Z', opponents);

        Assert.Null(result);
    }

    [Fact]
    public void FromPlayerAndOpponents_NoOpponents_ReturnsNull()
    {
        Matchup? result = MatchupResolver.FromPlayerAndOpponents('Z', []);

        Assert.Null(result);
    }

    [Fact]
    public void FromPlayerAndOpponents_UsesFirstOpponentOnly()
    {
        GamePlayer[] opponents = [CreateOpponent('T'), CreateOpponent('Z')];

        Matchup? result = MatchupResolver.FromPlayerAndOpponents('Z', opponents);

        Assert.Equal(Matchup.ZvT, result);
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
