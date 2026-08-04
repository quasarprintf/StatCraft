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
        GameData game = CreateGame();
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

        GameData game = CreateGame(replayTimestamp: new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(expected, GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_MapNotInSet_ReturnsFalse()
    {
        GameData game = CreateGame(mapName: "Altitude");
        GameFilterCriteria criteria = GameFilterCriteria.Empty with { Maps = new HashSet<string> { "Other Map" } };
        Assert.False(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_MapInSet_ReturnsTrue()
    {
        GameData game = CreateGame(mapName: "Altitude");
        GameFilterCriteria criteria = GameFilterCriteria.Empty with { Maps = new HashSet<string> { "Altitude" } };
        Assert.True(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_OutcomeNotInSet_ReturnsFalse()
    {
        GameData game = CreateGame(win: 1m);
        GameFilterCriteria criteria = GameFilterCriteria.Empty with { Outcomes = new HashSet<GameOutcome> { GameOutcome.Loss } };
        Assert.False(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_OutcomeInSet_ReturnsTrue()
    {
        GameData game = CreateGame(win: 1m);
        GameFilterCriteria criteria = GameFilterCriteria.Empty with { Outcomes = new HashSet<GameOutcome> { GameOutcome.Win } };
        Assert.True(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_MatchupPairs_OrsAcrossOpponents()
    {
        GamePlayer opponentZ = new() { Name = "A", Clan = "", Mmr = 3000, Race = 'Z', Random = false };
        GamePlayer opponentP = new() { Name = "B", Clan = "", Mmr = 3000, Race = 'P', Random = false };
        GameData game = CreateGame(selfRace: 'T', opponents: [opponentZ, opponentP]);

        // Only TvP is checked — should still match because one of the two opponents is Protoss.
        GameFilterCriteria criteria = GameFilterCriteria.Empty with
        {
            MatchupPairs = new HashSet<(Race, Race)> { (Race.T, Race.P) },
        };
        Assert.True(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_MatchupPairs_NoOpponentMatchesPlayerRacePair_ReturnsFalse()
    {
        GamePlayer opponentZ = new() { Name = "A", Clan = "", Mmr = 3000, Race = 'Z', Random = false };
        GameData game = CreateGame(selfRace: 'T', opponents: [opponentZ]);

        GameFilterCriteria criteria = GameFilterCriteria.Empty with
        {
            MatchupPairs = new HashSet<(Race, Race)> { (Race.T, Race.P) },
        };
        Assert.False(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_OpponentMmrRange_OrsAcrossOpponents()
    {
        GamePlayer low = new() { Name = "A", Clan = "", Mmr = 2000, Race = 'Z', Random = false };
        GamePlayer high = new() { Name = "B", Clan = "", Mmr = 3500, Race = 'P', Random = false };
        GameData game = CreateGame(opponents: [low, high]);

        GameFilterCriteria criteria = GameFilterCriteria.Empty with { MinOpponentMmr = 3000, MaxOpponentMmr = 4000 };
        Assert.True(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_OpponentMmrRange_NoOpponentInRange_ReturnsFalse()
    {
        GamePlayer low = new() { Name = "A", Clan = "", Mmr = 2000, Race = 'Z', Random = false };
        GameData game = CreateGame(opponents: [low]);

        GameFilterCriteria criteria = GameFilterCriteria.Empty with { MinOpponentMmr = 3000, MaxOpponentMmr = 4000 };
        Assert.False(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_BuildIds_ExactIdMatches()
    {
        GameData game = CreateGame(selfBuildIds: [5]);
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

        GameData game = CreateGame(selfBuildIds: [child.Id]);
        GameFilterCriteria criteria = GameFilterCriteria.Empty with { BuildIds = expandedIds };
        Assert.True(GameDataFilter.Matches(game, criteria));
    }

    [Fact]
    public void Matches_BuildIds_NotInSet_ReturnsFalse()
    {
        GameData game = CreateGame(selfBuildIds: [99]);
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

    private static GameData CreateGame(string mapName = "Map", decimal win = 1m, char selfRace = 'T',
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
        return new GameData { Map = new Map { Id = 1, Name = mapName }, ReplayData = replay };
    }
}
