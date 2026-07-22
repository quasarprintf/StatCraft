using System.Collections.Generic;
using System.Linq;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Services.DataParsing;

namespace StatCraft.Tests;

public class ReplayDataExtractorTests
{
    private readonly ReplayDataExtractor _extractor = new();

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

        Assert.Equal(0, result.Player.Mmr);
    }

    [Fact]
    public void Parse_MapsTopLevelFieldsFromRawReplayData()
    {
        RawReplayData raw = CreateRawReplayData(
            profileIds: [100, 200],
            teams: [0, 1],
            winningIndices: [0],
            mapName: "Site Delta",
            gameLengthSeconds: 725,
            replayPath: @"C:\Replays\game.SC2Replay");

        ParsedReplayData result = _extractor.Parse(raw, CreateProfile(100));

        Assert.Equal("Site Delta", result.MapName);
        Assert.Equal(725, result.GameLengthSeconds);
        Assert.Equal(@"C:\Replays\game.SC2Replay", result.ReplayPath);
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
        Assert.Equal(3500, result.Player.Mmr);
    }

    private static Sc2Profile CreateProfile(int profileId, string name = "Me") => new()
    {
        ProfileId = profileId,
        Name = name,
    };

    private static RawReplayData CreateRawReplayData(
        IReadOnlyList<int> profileIds,
        IReadOnlyList<int> teams,
        IReadOnlyList<int> winningIndices,
        bool isDraw = false,
        IReadOnlyList<string>? names = null,
        IReadOnlyList<string?>? clans = null,
        IReadOnlyList<char>? races = null,
        IReadOnlyList<bool>? randomRace = null,
        IReadOnlyList<long?>? mmrs = null,
        string mapName = "Map",
        int gameLengthSeconds = 600,
        string replayPath = "replay.SC2Replay")
    {
        int count = profileIds.Count;

        return new RawReplayData
        {
            MapName = mapName,
            PlayerNames = (names ?? Enumerable.Range(0, count).Select(i => $"Player{i}")).ToList(),
            PlayerClans = (clans ?? Enumerable.Repeat<string?>(null, count)).ToList(),
            PlayerRaces = (races ?? Enumerable.Repeat('T', count)).ToList(),
            PlayerRandomRace = (randomRace ?? Enumerable.Repeat(false, count)).ToList(),
            PlayerMmrs = (mmrs ?? Enumerable.Repeat<long?>(1000, count)).ToList(),
            PlayerTeams = teams.ToList(),
            PlayerProfileIds = profileIds.ToList(),
            IsDraw = isDraw,
            WinningPlayerIndices = winningIndices.ToList(),
            GameLengthSeconds = gameLengthSeconds,
            ReplayPath = replayPath,
        };
    }
}
