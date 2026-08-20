using System.Net.Http;
using System.Threading;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.Util;
using StatCraft.Services.BackgroundService;
using StatCraft.Services.BattlenetApi;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;
using StatCraft.Styles;
using StatCraft.Tests.Mocks;
using StatCraft.ViewModels.Windows;
using StatCraft.ViewModels.Windows.DataComponents;

namespace StatCraft.Tests;

// Reproduces a real-session bug report: the Data tab's table would sometimes jump back to the top
// while the user was scrolled through it. DataPageViewModel.ApplyFilters used to Clear() and rebuild
// every GameDataRowViewModel from scratch on every reload — including the background reload that runs
// whenever a replay is imported (see OnGameParsed) — which raises a collection Reset the DataGrid
// responds to by resetting its scroll position, for reasons that had nothing to do with anything the
// user had touched. These pin the fix: reloading has to reuse the same row instance for a game that's
// still in view, only touching rows that actually entered or left the filtered set.
public class DataPageViewModelTests : IAsyncDisposable
{
    private readonly string _dbPath;
    private readonly GameDataRepository _gameDataRepository;
    private readonly ReplayWatcherService _replayWatcherService;
    private readonly SettingsRepository _settingsRepository;
    private readonly DataPageViewModel _viewModel;
    private readonly int _sc2ProfileId;
    private readonly Sc2Profile _profile;

    public DataPageViewModelTests()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempRoot);
        _dbPath = Path.Combine(tempRoot, "statcraft.db");
        string settingsPath = Path.Combine(tempRoot, "settings.json");

        AccountRepository accountRepository = new(_dbPath);
        accountRepository.Initialize();
        BuildRepository buildRepository = new(_dbPath);
        buildRepository.Initialize();
        // Before GameDataRepository, whose MapName -> MapId migration writes into the Maps table.
        MapRepository mapRepository = new(_dbPath);
        mapRepository.Initialize();
        _gameDataRepository = new GameDataRepository(_dbPath);
        _gameDataRepository.Initialize();

        BattleNetAccount account = new()
        {
            BattleTag = "Player#1234", AccountSub = "sub-1", EncryptedAccessToken = [1],
            TokenExpiresAtUtc = DateTimeOffset.UtcNow, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        accountRepository.InsertAccount(account);
        _profile = new Sc2Profile { BattleNetAccountId = account.Id, RegionId = "1", RealmId = "1", ProfileId = 111, Name = "Player" };
        accountRepository.UpsertProfile(_profile);
        _sc2ProfileId = _profile.Id;

        _settingsRepository = new SettingsRepository(settingsPath);
        _replayWatcherService = new ReplayWatcherService(new MockLogger());
        Sc2LadderService ladderService = new(new HttpClient(), new StubTokenProvider(), new MockLogger());
        ReplayDataExtractor replayDataExtractor = new();
        ReplayImportService replayImportService = new(new MockLogger(), replayDataExtractor, _gameDataRepository,
            mapRepository, ladderService);

        _viewModel = new DataPageViewModel(_settingsRepository, _replayWatcherService, replayImportService,
            accountRepository, buildRepository, _gameDataRepository, ladderService, new MockLogger(), replayDataExtractor);
    }

    // The "Use Team Colors" setting can be toggled mid-session — already-visible rows must pick it up
    // immediately (via DataPageViewModel.OnSettingsChanged -> GameDataRowViewModel.RefreshTeamColors)
    // rather than only the next time a row happens to get rebuilt.
    [Fact]
    public async Task TogglingUseTeamColors_AfterRowsAreAlreadyVisible_UpdatesTheirTabColorsImmediately()
    {
        InsertGame();
        await _viewModel.SetActiveProfile(_profile);

        GameDataRowViewModel row = Assert.Single(_viewModel.Games);
        PlayerBuildTrackerViewModel opponentTracker = Assert.Single(row.OtherPlayers);

        _settingsRepository.Save(new AppSettingsData { UseTeamColors = true });

        Assert.Same(Colors.OpponentRed, opponentTracker.NameColor);
    }

    [Fact]
    public async Task ReloadingGames_WithAnAdditionalGame_KeepsExistingRowsAsTheSameInstance()
    {
        GameData game1 = InsertGame();
        await _viewModel.SetActiveProfile(_profile);

        GameDataRowViewModel originalRow = Assert.Single(_viewModel.Games);
        Assert.Equal(game1.GameId, originalRow.GameId);

        InsertGame();
        await _viewModel.SetActiveProfile(_profile);

        Assert.Equal(2, _viewModel.Games.Count);
        GameDataRowViewModel? survivingRow = _viewModel.Games.FirstOrDefault(g => g.GameId == game1.GameId);
        Assert.Same(originalRow, survivingRow);
    }

    private GameData InsertGame()
    {
        ParsedReplayData replay = new()
        {
            GameLengthSeconds = 600,
            ReplayPath = Guid.NewGuid() + ".SC2Replay",
            ReplayTimestamp = DateTimeOffset.Now,
            Win = 1m,
            Player = new GamePlayer { Name = "Me", Clan = "", Mmr = 3000, Race = 'Z', Random = false },
            Allies = [],
            Opponents = [new GamePlayer { Name = "Foe", Clan = "", Mmr = 3100, Race = 'T', Random = false }],
        };
        GameData game = new() { ReplayData = replay };
        _gameDataRepository.InsertGame(game, _sc2ProfileId);
        return game;
    }

    public async ValueTask DisposeAsync()
    {
        await _replayWatcherService.Stop();
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

    private sealed class StubTokenProvider() : BlizzardAppTokenProvider(null!, null!, null!, new MockLogger())
    {
        public override Task<string?> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }
}
