using StatCraft.Models.GameData;

namespace StatCraft.Tests;

// IsRatedOneVsOne gates both post-game MMR polling and session-baseline seeding, so a wrong answer
// here either silently drops MMR tracking or seeds a baseline from a rating that doesn't exist.
public class ParsedReplayDataTests
{
    [Fact]
    public void RankedOneVsOne_IsRated()
    {
        Assert.True(CreateReplay().IsRatedOneVsOne);
    }

    [Fact]
    public void UnrankedOrCustom_ReportsNoRating_IsNotRated()
    {
        // Unranked and custom games come back with Mmr 0, so there's nothing to compare against.
        Assert.False(CreateReplay(selfMmr: 0).IsRatedOneVsOne);
    }

    [Fact]
    public void TeamGame_IsNotRated()
    {
        GamePlayer ally = Player("Ally", 'T');
        Assert.False(CreateReplay(allies: [ally], opponents: [Player("A", 'Z'), Player("B", 'P')]).IsRatedOneVsOne);
    }

    [Fact]
    public void TwoOpponentsWithoutAllies_IsNotRated()
    {
        // Free-for-all: still not a 1v1 ladder rating.
        Assert.False(CreateReplay(opponents: [Player("A", 'Z'), Player("B", 'P')]).IsRatedOneVsOne);
    }

    [Fact]
    public void NegativeRating_IsNotRated()
    {
        Assert.False(CreateReplay(selfMmr: -1).IsRatedOneVsOne);
    }

    private static GamePlayer Player(string name, char race, long mmr = 3000) =>
        new() { Name = name, Clan = "", Mmr = new PlayerMmr { ParsedMmr = mmr }, Race = race, Random = false };

    private static ParsedReplayData CreateReplay(long selfMmr = 3000, GamePlayer[]? allies = null, GamePlayer[]? opponents = null) => new()
    {
        GameLengthSeconds = 600,
        ReplayPath = "replay.SC2Replay",
        ReplayTimestamp = DateTimeOffset.UtcNow,
        Win = 1m,
        Player = Player("Me", 'P', selfMmr),
        Allies = allies ?? [],
        Opponents = opponents ?? [Player("Foe", 'Z', 3100)],
    };
}
