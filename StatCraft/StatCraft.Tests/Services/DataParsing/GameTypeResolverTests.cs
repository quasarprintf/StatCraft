using StatCraft.Models.GameData;
using StatCraft.Services.DataParsing;

namespace StatCraft.Tests;

public class GameTypeResolverTests
{
    [Fact]
    public void NotMatchmade_IsCustom_EvenOnALadderMap()
    {
        // Custom games are routinely played on Blizzard ladder maps, so the map is no guide — only the
        // matchmaking flag is. Confirmed against real replays.
        ParsedReplayData replay = CreateReplay(isMatchmade: false);

        Assert.Equal(GameType.Custom, GameTypeResolver.Resolve(replay, lastKnownRankedMmr: 5239));
    }

    [Fact]
    public void MatchmadeStartingFromKnownRankedMmr_IsRanked()
    {
        ParsedReplayData replay = CreateReplay(selfMmr: 5239);

        Assert.Equal(GameType.Ranked, GameTypeResolver.Resolve(replay, lastKnownRankedMmr: 5239));
    }

    [Fact]
    public void MatchmadeStartingFromADifferentRating_IsUnranked()
    {
        // Unranked tracks its own hidden rating, so the game didn't start from the ranked one.
        ParsedReplayData replay = CreateReplay(selfMmr: 4400);

        Assert.Equal(GameType.Unranked, GameTypeResolver.Resolve(replay, lastKnownRankedMmr: 5239));
    }

    [Fact]
    public void MatchmadeWithNoKnownRankedMmr_FallsBackToRanked()
    {
        // No credentials, unplaced ladder, or a failed lookup — nothing to compare against.
        ParsedReplayData replay = CreateReplay(selfMmr: 5239);

        Assert.Equal(GameType.Ranked, GameTypeResolver.Resolve(replay, lastKnownRankedMmr: null));
    }

    [Fact]
    public void NotMatchmadeWithNoKnownMmr_IsStillCustom()
    {
        // The custom check never depends on the API, so it works offline and retroactively.
        Assert.Equal(GameType.Custom, GameTypeResolver.Resolve(CreateReplay(isMatchmade: false), null));
    }

    [Fact]
    public void ConsecutiveRankedGamesEachMatchTheirOwnStartingMmr()
    {
        // The reason the comparison tracks the latest known rating rather than the session's opening
        // one: after game 1 the ladder has moved, and game 2 starts from that new value.
        ParsedReplayData game1 = CreateReplay(selfMmr: 5239);
        ParsedReplayData game2 = CreateReplay(selfMmr: 5263);

        Assert.Equal(GameType.Ranked, GameTypeResolver.Resolve(game1, lastKnownRankedMmr: 5239));
        Assert.Equal(GameType.Ranked, GameTypeResolver.Resolve(game2, lastKnownRankedMmr: 5263));

        // Against a stale session-opening baseline, game 2 would have been misread as unranked.
        Assert.Equal(GameType.Unranked, GameTypeResolver.Resolve(game2, lastKnownRankedMmr: 5239));
    }

    private static ParsedReplayData CreateReplay(bool isMatchmade = true, long selfMmr = 3000) => new()
    {
        MapName = "Altitude LE",
        GameLengthSeconds = 600,
        ReplayPath = "replay.SC2Replay",
        ReplayTimestamp = DateTimeOffset.UtcNow,
        Win = 1m,
        IsMatchmade = isMatchmade,
        Player = new GamePlayer { Name = "Me", Clan = "", Mmr = selfMmr, Race = 'P', Random = false },
        Allies = [],
        Opponents = [new GamePlayer { Name = "Foe", Clan = "", Mmr = 3100, Race = 'Z', Random = false }],
    };
}
