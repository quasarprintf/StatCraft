using System.Collections.Generic;
using System.Linq;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Maps;
using StatCraft.Models.GameData.Replays;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;

namespace StatCraft.Tests;

public class ReplayDataExtractorTests : IDisposable
{
    // Parse resolves the replay's map name against the Maps table (creating the row if it's new), so
    // these need a real repository rather than the bare extractor they used before.
    private readonly string _dbPath;
    private readonly MapRepository _mapRepository;
    private readonly ReplayDataExtractor _extractor;

    public ReplayDataExtractorTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");
        _mapRepository = new MapRepository(_dbPath);
        _mapRepository.Initialize();
        _extractor = new ReplayDataExtractor(_mapRepository);
    }

    [Fact]
    public void Parse_PlayerWins_SetsWinToOne()
    {
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200],
            teams: [0, 1],
            winningIndices: [0]);

        ParsedReplayData result = _extractor.Parse(raw, CreateProfile(100));

        Assert.Equal(1m, result.Win);
    }

    [Fact]
    public void Parse_PlayerLoses_SetsWinToZero()
    {
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200],
            teams: [0, 1],
            winningIndices: [1]);

        ParsedReplayData result = _extractor.Parse(raw, CreateProfile(100));

        Assert.Equal(0m, result.Win);
    }

    [Fact]
    public void Parse_Draw_SetsWinToOneHalf()
    {
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200],
            teams: [0, 1],
            winningIndices: [],
            isDraw: true);

        ParsedReplayData result = _extractor.Parse(raw, CreateProfile(100));

        Assert.Equal(0.5m, result.Win);
    }

    [Fact]
    public void Parse_TeamGame_SplitsAlliesAndOpponentsByTeam()
    {
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200, 300, 400],
            teams: [0, 0, 1, 1],
            winningIndices: [0, 1],
            names: ["Me", "Ally", "Foe1", "Foe2"]);

        ParsedReplayData result = _extractor.Parse(raw, CreateProfile(100));

        GamePlayer ally = Assert.Single(result.Allies);
        Assert.Equal("Ally", ally.Name);
        Assert.Equal(["Foe1", "Foe2"], result.Opponents.Select(o => o.Name));
    }

    [Fact]
    public void Parse_Draw_StillSplitsAlliesByTeam()
    {
        // Team membership must come from PlayerTeams, not from WinningPlayerIndices, since a draw
        // leaves WinningPlayerIndices empty and can't be used to infer sides.
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200, 300, 400],
            teams: [0, 0, 1, 1],
            winningIndices: [],
            isDraw: true,
            names: ["Me", "Ally", "Foe1", "Foe2"]);

        ParsedReplayData result = _extractor.Parse(raw, CreateProfile(100));

        GamePlayer ally = Assert.Single(result.Allies);
        Assert.Equal("Ally", ally.Name);
        Assert.Equal(["Foe1", "Foe2"], result.Opponents.Select(o => o.Name));
    }

    [Fact]
    public void Parse_PlayerNotInReplay_ThrowsInvalidOperationException()
    {
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200],
            teams: [0, 1],
            winningIndices: [0]);

        Assert.Throws<InvalidOperationException>(() => _extractor.Parse(raw, CreateProfile(999)));
    }

    [Fact]
    public void Parse_NullClan_DefaultsToEmptyString()
    {
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200],
            teams: [0, 1],
            winningIndices: [0],
            clans: [null, null]);

        ParsedReplayData result = _extractor.Parse(raw, CreateProfile(100));

        Assert.Equal("", result.Player.Clan);
    }

    [Fact]
    public void Parse_NullMmr_DefaultsToZero()
    {
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200],
            teams: [0, 1],
            winningIndices: [0],
            mmrs: [null, null]);

        ParsedReplayData result = _extractor.Parse(raw, CreateProfile(100));

        Assert.Equal(0, result.Player.Mmr.ParsedMmr);
    }

    [Fact]
    public void Parse_MapsTopLevelFieldsFromRawReplayData()
    {
        DateTimeOffset timestamp = new DateTimeOffset(2026, 3, 4, 9, 15, 0, TimeSpan.Zero);
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200],
            teams: [0, 1],
            winningIndices: [0],
            mapName: "Site Delta",
            gameLengthSeconds: 725,
            replayPath: @"C:\Replays\game.SC2Replay",
            replayTimestamp: timestamp);

        ParsedReplayData result = _extractor.Parse(raw, CreateProfile(100));

        Assert.Equal(725, result.GameLengthSeconds);
        Assert.Equal(@"C:\Replays\game.SC2Replay", result.ReplayPath);
        Assert.Equal(timestamp, result.ReplayTimestamp);
    }

    [Fact]
    public void Parse_BuildsPlayerFromMatchedProfile()
    {
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200],
            teams: [0, 1],
            winningIndices: [0],
            names: ["Me", "Opponent"],
            clans: ["ABC", null],
            races: ['Z', 'T'],
            randomRace: [true, false],
            mmrs: [3500, 3200]);

        ParsedReplayData result = _extractor.Parse(raw, CreateProfile(100));

        Assert.Equal("Me", result.Player.Name);
        Assert.Equal("ABC", result.Player.Clan);
        Assert.Equal('Z', result.Player.Race);
        Assert.True(result.Player.Random);
        Assert.Equal(3500, result.Player.Mmr.ParsedMmr);
    }

    // Each player's in-game color (see GamePlayer.ColorArgb) is what the Data tab's build tabs are
    // colored by, so it has to survive the same index-parallel-to-per-player reframing every other
    // field here does.
    [Fact]
    public void Parse_ThreadsEachPlayersColorArgbThrough()
    {
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200],
            teams: [0, 1],
            winningIndices: [0],
            colorsArgb: [unchecked((int)0xFFFF0000), unchecked((int)0xFF0000FF)]);

        ParsedReplayData result = _extractor.Parse(raw, CreateProfile(100));

        Assert.Equal(unchecked((int)0xFFFF0000), result.Player.ColorArgb);
        Assert.Equal(unchecked((int)0xFF0000FF), Assert.Single(result.Opponents).ColorArgb);
    }

    // A moved, deleted, or otherwise unreadable replay file is exactly the case this exists for — an
    // old row whose color was never captured, backfilled by re-reading a file that may no longer be
    // where the game's ReplayPath says it is. It has to degrade to "no color" rather than throw, since
    // it always runs as a best-effort UI backfill (see PlayerBuildTrackerViewModel).
    [Fact]
    public async Task TryResolvePlayerColorAsync_FileDoesNotExist_ReturnsNull()
    {
        int? color = await _extractor.TryResolvePlayerColorAsync("does-not-exist.SC2Replay", "AnyPlayer");

        Assert.Null(color);
    }

    private static Sc2Profile CreateProfile(int profileId, string name = "Me") => new()
    {
        ProfileId = profileId,
        Name = name,
    };

    // Parse resolves the replay's map name to a row in the Maps table, so every game imported on a map
    // ends up pointing at the same map the Maps tab edits, rather than a name copied onto the game.
    [Fact]
    public void Parse_AttachesTheMapForTheReplaysMapName()
    {
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200],
            teams: [0, 1],
            winningIndices: [0],
            mapName: "Altitude LE");

        ParsedReplayData result = _extractor.Parse(raw, CreateProfile(100));

        Assert.Equal("Altitude LE", result.Map?.Name);
        Assert.Equal(_mapRepository.GetAllMaps([]).Single(m => m.Name == "Altitude LE").Id, result.Map?.Id);
    }

    [Fact]
    public void Parse_MapAlreadyKnown_ReusesTheExistingMapRatherThanAddingAnother()
    {
        Map existing = new() { Name = "Altitude LE" };
        _mapRepository.InsertMap(existing);
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200],
            teams: [0, 1],
            winningIndices: [0],
            mapName: "Altitude LE");

        ParsedReplayData result = _extractor.Parse(raw, CreateProfile(100));

        Assert.Equal(existing.Id, result.Map?.Id);
        Assert.Single(_mapRepository.GetAllMaps([]));
    }

    // A replay with no map name has nothing to attach, and a game without a map can't be shown or
    // filtered by map — so parsing fails rather than storing a game that points at nothing.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_ReplayHasNoMapName_Throws(string mapName)
    {
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200],
            teams: [0, 1],
            winningIndices: [0],
            mapName: mapName);

        Assert.Throws<InvalidOperationException>(() => _extractor.Parse(raw, CreateProfile(100)));
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private static RawReplayData CreateRawReplayData(
        IReadOnlyList<int> profileIds,
        IReadOnlyList<int> teams,
        IReadOnlyList<int> winningIndices,
        bool isDraw = false,
        IReadOnlyList<string>? names = null,
        IReadOnlyList<string?>? clans = null,
        IReadOnlyList<char>? races = null,
        IReadOnlyList<bool>? randomRace = null,
        IReadOnlyList<int>? colorsArgb = null,
        IReadOnlyList<long?>? mmrs = null,
        string mapName = "Map",
        int gameLengthSeconds = 600,
        string replayPath = "replay.SC2Replay",
        DateTimeOffset? replayTimestamp = null)
    {
        int count = profileIds.Count;

        return new RawReplayData
        {
            MapName = mapName,
            PlayerNames = (names ?? Enumerable.Range(0, count).Select(i => $"Player{i}")).ToList(),
            PlayerClans = (clans ?? Enumerable.Repeat<string?>(null, count)).ToList(),
            PlayerRaces = (races ?? Enumerable.Repeat('T', count)).ToList(),
            PlayerRandomRace = (randomRace ?? Enumerable.Repeat(false, count)).ToList(),
            PlayerColorsArgb = (colorsArgb ?? Enumerable.Repeat(0, count)).ToList(),
            PlayerMmrs = (mmrs ?? Enumerable.Repeat<long?>(1000, count)).ToList(),
            PlayerTeams = teams.ToList(),
            PlayerProfileIds = profileIds.ToList(),
            IsDraw = isDraw,
            WinningPlayerIndices = winningIndices.ToList(),
            GameLengthSeconds = gameLengthSeconds,
            ReplayPath = replayPath,
            ReplayTimestamp = replayTimestamp ?? new DateTimeOffset(2026, 1, 15, 18, 30, 0, TimeSpan.Zero),
        };
    }
}
