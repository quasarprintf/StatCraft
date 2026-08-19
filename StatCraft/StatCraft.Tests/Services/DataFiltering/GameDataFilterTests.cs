using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Maps;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DataFiltering;

namespace StatCraft.Tests;

public class GameDataFilterTests
{
    [Fact]
    public void Matches_EmptyCriteria_MatchesEverything()
    {
        Map altitude = new() { Name = "Altitude LE" };
        GameData game = CreateGame(altitude);
        Assert.True(GameDataFilter.Matches(game, GameFilterCriteria.Empty));
    }

    [Theory]
    [InlineData(2026, 1, 15, true)]
    [InlineData(2026, 1, 10, false)]
    [InlineData(2026, 1, 20, false)]
    public void Matches_DateRange_IsInclusiveOnBothEnds(int year, int month, int day, bool expected)
    {
        GameFilterCriteria criteria = GameFilterCriteria.Empty with
        {
            FromDate = new DateOnly(2026, 1, 15),
            ToDate = new DateOnly(2026, 1, 15),
        };

        Map altitude = new() { Name = "Altitude LE" };
        GameData game = CreateGame(altitude, replayTimestamp: new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(expected, GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_MapNotInSet_ReturnsFalse()
    {
        Map altitude = new() { Name = "Altitude LE", Id = 1 };
        Map deathaura = new() { Name = "Deathaura LE", Id = 2 };
        GameData game = CreateGame(altitude);
        GameFilterCriteria criteria = GameFilterCriteria.Empty with { Maps = new HashSet<Map> { deathaura } };
        Assert.False(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_MapInSet_ReturnsTrue()
    {
        Map altitude = new() { Name = "Altitude LE", Id = 1 };
        Map deathaura = new() { Name = "Deathaura LE", Id = 2 };
        GameData game = CreateGame(altitude);
        GameFilterCriteria criteria = GameFilterCriteria.Empty with { Maps = new HashSet<Map> { altitude } };
        Assert.True(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_OutcomeNotInSet_ReturnsFalse()
    {
        Map altitude = new() { Name = "Altitude LE" };
        GameData game = CreateGame(map: altitude, win: 1m);
        GameFilterCriteria criteria = GameFilterCriteria.Empty with { Outcomes = new HashSet<GameOutcome> { GameOutcome.Loss } };
        Assert.False(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_OutcomeInSet_ReturnsTrue()
    {
        Map altitude = new() { Name = "Altitude LE" };
        GameData game = CreateGame(map: altitude, win: 1m);
        GameFilterCriteria criteria = GameFilterCriteria.Empty with { Outcomes = new HashSet<GameOutcome> { GameOutcome.Win } };
        Assert.True(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_MatchupPairs_OrsAcrossOpponents()
    {
        Map altitude = new() { Name = "Altitude LE" };
        GamePlayer opponentZ = new() { Name = "A", Clan = "", Mmr = 3000, Race = 'Z', Random = false };
        GamePlayer opponentP = new() { Name = "B", Clan = "", Mmr = 3000, Race = 'P', Random = false };
        GameData game = CreateGame(map: altitude, selfRace: 'T', opponents: [opponentZ, opponentP]);

        // Only TvP is checked — should still match because one of the two opponents is Protoss.
        GameFilterCriteria criteria = GameFilterCriteria.Empty with
        {
            MatchupPairs = new HashSet<(Race, Race)> { (Race.Terran, Race.Protoss) },
        };
        Assert.True(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_MatchupPairs_NoOpponentMatchesPlayerRacePair_ReturnsFalse()
    {
        Map altitude = new() { Name = "Altitude LE" };
        GamePlayer opponentZ = new() { Name = "A", Clan = "", Mmr = 3000, Race = 'Z', Random = false };
        GameData game = CreateGame(map: altitude, selfRace: 'T', opponents: [opponentZ]);

        GameFilterCriteria criteria = GameFilterCriteria.Empty with
        {
            MatchupPairs = new HashSet<(Race, Race)> { (Race.Terran, Race.Protoss) },
        };
        Assert.False(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_OpponentMmrRange_OrsAcrossOpponents()
    {
        Map altitude = new() { Name = "Altitude LE" };
        GamePlayer low = new() { Name = "A", Clan = "", Mmr = 2000, Race = 'Z', Random = false };
        GamePlayer high = new() { Name = "B", Clan = "", Mmr = 3500, Race = 'P', Random = false };
        GameData game = CreateGame(map: altitude, opponents: [low, high]);

        GameFilterCriteria criteria = GameFilterCriteria.Empty with { MinOpponentMmr = 3000, MaxOpponentMmr = 4000 };
        Assert.True(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_OpponentMmrRange_NoOpponentInRange_ReturnsFalse()
    {
        Map altitude = new() { Name = "Altitude LE" };
        GamePlayer low = new() { Name = "A", Clan = "", Mmr = 2000, Race = 'Z', Random = false };
        GameData game = CreateGame(map: altitude, opponents: [low]);

        GameFilterCriteria criteria = GameFilterCriteria.Empty with { MinOpponentMmr = 3000, MaxOpponentMmr = 4000 };
        Assert.False(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_BuildIds_ExactIdMatches()
    {
        Map altitude = new() { Name = "Altitude LE" };
        GameData game = CreateGame(map: altitude, selfBuildIds: [5]);
        GameFilterCriteria criteria = GameFilterCriteria.Empty with { BuildIds = new HashSet<int> { 5 } };
        Assert.True(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_BuildIds_DescendantIdMatchesViaExpandedCriteria()
    {
        BuildNode parent = new() { Id = 1 };
        BuildNode child = new() { Id = 2 };
        parent.Children.Add(child);

        // Criteria.BuildIds is expected to already be subtree-expanded by the time it reaches Matches
        // (mirrors DataPageFiltersViewModel.BuildCriteria), so build it via CollectSubtreeIds here.
        HashSet<int> expandedIds = GameDataFilter.CollectSubtreeIds(parent).ToHashSet();

        Map altitude = new() { Name = "Altitude LE" };
        GameData game = CreateGame(map: altitude, selfBuildIds: [child.Id]);
        GameFilterCriteria criteria = GameFilterCriteria.Empty with { BuildIds = expandedIds };
        Assert.True(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_BuildIds_NotInSet_ReturnsFalse()
    {
        Map altitude = new() { Name = "Altitude LE" };
        GameData game = CreateGame(map: altitude, selfBuildIds: [99]);
        GameFilterCriteria criteria = GameFilterCriteria.Empty with { BuildIds = new HashSet<int> { 5 } };
        Assert.False(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void CollectSubtreeIds_ReturnsNodeAndAllDescendants()
    {
        BuildNode root = new() { Id = 1 };
        BuildNode child = new() { Id = 2 };
        BuildNode grandchild = new() { Id = 3 };
        child.Children.Add(grandchild);
        root.Children.Add(child);

        Assert.Equal([1, 2, 3], GameDataFilter.CollectSubtreeIds(root).OrderBy(id => id));
    }

    private static GameData CreateGame(Map map, decimal win = 1m, char selfRace = 'T',
        int[]? selfBuildIds = null, GamePlayer[]? opponents = null, DateTimeOffset? replayTimestamp = null)
    {
        ParsedReplayData replay = new()
        {
            GameLengthSeconds = 600,
            ReplayPath = "replay.SC2Replay",
            ReplayTimestamp = replayTimestamp ?? new DateTimeOffset(2026, 1, 15, 18, 30, 0, TimeSpan.Zero),
            Win = win,
            Player = new GamePlayer
            {
                Name = "Me", Clan = "", Mmr = 3000, Race = selfRace, Random = false,
                BuildIds = selfBuildIds?.ToList() ?? [],
            },
            Allies = [],
            Opponents = opponents ?? [new GamePlayer { Name = "Foe", Clan = "", Mmr = 3100, Race = 'Z', Random = false }],
        };
        return new GameData { Map = map, ReplayData = replay };
    }
}
