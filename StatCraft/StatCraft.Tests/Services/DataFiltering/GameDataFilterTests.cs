using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Maps;
using StatCraft.Services.DataFiltering;

namespace StatCraft.Tests;

// The date/map/outcome/matchup/MMR tests that used to live here now run through the Games tab itself
// (DataPageViewModelTests, "Filtering" region), since the tab filters with DataPageFiltersViewModel.GetFilter()
// rather than GameDataFilter.Matches. The build tests stay here for now: the new filter doesn't cover builds
// yet (see the TODO in GetFilter), so these are still the only statement of how build filtering should
// behave — in particular, that checking a build also matches games that picked a build beneath it. Move
// them onto the page once the build filter exists.
public class GameDataFilterTests
{
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
        // (mirrors DataPageFiltersViewModel.ToBuildIdSet), so build it via CollectSubtreeIds here.
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

    private static GameData CreateGame(Map map, int[]? selfBuildIds = null)
    {
        ParsedReplayData replay = new()
        {
            GameLengthSeconds = 600,
            ReplayPath = "replay.SC2Replay",
            ReplayTimestamp = new DateTimeOffset(2026, 1, 15, 18, 30, 0, TimeSpan.Zero),
            Win = 1m,
            Player = new GamePlayer
            {
                Name = "Me", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3000 }, Race = 'T', Random = false,
                BuildIds = selfBuildIds?.ToList() ?? [],
            },
            Allies = [],
            Opponents = [new GamePlayer { Name = "Foe", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3100 }, Race = 'Z', Random = false }],
        };
        return new GameData { Map = map, ReplayData = replay };
    }
}
