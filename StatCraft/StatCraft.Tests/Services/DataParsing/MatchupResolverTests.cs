using StatCraft.Models.GameData;
using StatCraft.Services.DataParsing;
using StatCraft.ViewModels;

namespace StatCraft.Tests;

public class MatchupResolverTests
{
    [Theory]
    [InlineData('Z', 'Z', Race.Z, Race.Z)]
    [InlineData('Z', 'T', Race.Z, Race.T)]
    [InlineData('Z', 'P', Race.Z, Race.P)]
    [InlineData('T', 'Z', Race.T, Race.Z)]
    [InlineData('T', 'T', Race.T, Race.T)]
    [InlineData('T', 'P', Race.T, Race.P)]
    [InlineData('P', 'Z', Race.P, Race.Z)]
    [InlineData('P', 'T', Race.P, Race.T)]
    [InlineData('P', 'P', Race.P, Race.P)]
    public void FromPlayerAndOpponents_KnownRaces_ReturnsMatchingMatchup(char playerRace, char opponentRace, Race expectedPlayer, Race expectedOpponent)
    {
        GamePlayer[] opponents = [CreateOpponent(opponentRace)];

        List<Race> result = MatchupResolver.FromOpponents(playerRace, opponents);

        Assert.Equal((expectedPlayer, expectedOpponent), result);
    }

    [Fact]
    public void FromPlayerAndOpponents_UnresolvedPlayerRace_ReturnsNull()
    {
        GamePlayer[] opponents = [CreateOpponent('Z')];

        (Race Player, Race Opponent)? result = MatchupResolver.FromOpponents('?', opponents);

        Assert.Null(result);
    }

    [Fact]
    public void FromPlayerAndOpponents_UnresolvedOpponentRace_ReturnsNull()
    {
        GamePlayer[] opponents = [CreateOpponent('?')];

        (Race Player, Race Opponent)? result = MatchupResolver.FromOpponents('Z', opponents);

        Assert.Null(result);
    }

    [Fact]
    public void FromPlayerAndOpponents_NoOpponents_ReturnsNull()
    {
        (Race Player, Race Opponent)? result = MatchupResolver.FromOpponents('Z', []);

        Assert.Null(result);
    }

    [Fact]
    public void FromPlayerAndOpponents_UsesFirstOpponentOnly()
    {
        GamePlayer[] opponents = [CreateOpponent('T'), CreateOpponent('Z')];

        (Race Player, Race Opponent)? result = MatchupResolver.FromOpponents('Z', opponents);

        Assert.Equal((Race.Z, Race.T), result);
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
