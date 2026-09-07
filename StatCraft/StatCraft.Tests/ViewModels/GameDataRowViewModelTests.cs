using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;
using StatCraft.Tests.Mocks;
using StatCraft.ViewModels.Windows.DataComponents;
using StatCraft.ViewModels.Windows.DataComponents.GameRow;
using System.Collections.ObjectModel;

namespace StatCraft.Tests;

public class GameDataRowViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly GameDataRepository _gameDataRepository;
    private readonly ObservableCollection<AttributeDefinition> _gameAttributes = [];
    private readonly MockLogger _logger = new();
    private readonly ReplayDataExtractor _replayDataExtractor = new();
    private readonly int _sc2ProfileId;

    public GameDataRowViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");

        AccountRepository accountRepository = new(_dbPath);
        accountRepository.Initialize();
        new BuildRepository(_dbPath).Initialize();
        new MapRepository(_dbPath).Initialize();
        _gameDataRepository = new GameDataRepository(_dbPath);
        _gameDataRepository.Initialize();

        BattleNetAccount account = new()
        {
            BattleTag = "Player#1234", AccountSub = "sub-1", EncryptedAccessToken = [1],
            TokenExpiresAtUtc = DateTimeOffset.UtcNow, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        accountRepository.InsertAccount(account);
        Sc2Profile profile = new() { BattleNetAccountId = account.Id, RegionId = "1", RealmId = "1", ProfileId = 111, Name = "Player" };
        accountRepository.UpsertProfile(profile);
        _sc2ProfileId = profile.Id;
    }

    // Reproduces a real-session bug report: notes typed into a row would later appear empty. The DB
    // write always succeeded — what was missing was updating the underlying GameData in memory, so a
    // row rebuilt from the same GameData (exactly what every Data tab filter change does to every
    // visible row via DataPageViewModel.ApplyFilters/WrapGame) read the stale pre-edit value instead.
    [Fact]
    public void EditingNotes_ThenRewrappingTheSameGameData_ShowsTheEditedValue()
    {
        GameData game = CreateGame();
        _gameDataRepository.InsertGame(game, _sc2ProfileId);

        GameDataRowViewModel firstRow = new(game, _gameDataRepository, _gameAttributes, "Player", (_, _) => null, _logger, _replayDataExtractor);
        firstRow.Notes = "Lost to a cheese rush";

        // Simulates DataPageViewModel.WrapGame reconstructing every visible row from the same
        // underlying GameData after a filter change — the row instance is new, but the GameData isn't.
        GameDataRowViewModel secondRow = new(game, _gameDataRepository, _gameAttributes, "Player", (_, _) => null, _logger, _replayDataExtractor);

        Assert.Equal("Lost to a cheese rush", secondRow.Notes);
    }

    // Reproduces a real-session bug report: an opponent's MMR was replay-parsed as garbage, corrected
    // via OpponentMmrEstimator once the tracked player's post-game MMR resolved (see
    // ReplayImportService.TryCorrectOpponentMmr), and the DB updated correctly — but the Data page kept
    // showing the original parsed value instead of the estimate. The underlying GamePlayer.Mmr is
    // mutated in place rather than replaced, so OpponentRowViewModel has to notice the change itself
    // (via PlayerMmr.MmrChanged) rather than being reconstructed.
    [Fact]
    public void OpponentMmrIsCorrectedAfterRowIsBuilt_DisplayedMmrUpdates()
    {
        GameData game = CreateGame();
        _gameDataRepository.InsertGame(game, _sc2ProfileId);

        GameDataRowViewModel row = new(game, _gameDataRepository, _gameAttributes, "Player", (_, _) => null, _logger, _replayDataExtractor);
        OpponentRowViewModel opponentRow = Assert.Single(row.Opponents);
        Assert.Equal(3100, opponentRow.Mmr);

        // Simulates TryCorrectOpponentMmr discarding the replay-parsed 3100 as implausible and storing
        // an Elo-estimated value instead, minutes after the row was already constructed and displayed.
        GamePlayer opponent = game.ReplayData.Opponents[0];
        opponent.Mmr.EstimatedMmr = 3400;

        Assert.Equal(3400, opponentRow.Mmr);
    }

    private static GameData CreateGame()
    {
        ParsedReplayData replay = new()
        {
            GameLengthSeconds = 600,
            ReplayPath = Guid.NewGuid() + ".SC2Replay",
            ReplayTimestamp = DateTimeOffset.UtcNow,
            Win = 1m,
            Player = new GamePlayer { Name = "Me", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3000 }, Race = 'Z', Random = false },
            Allies = [],
            Opponents = [new GamePlayer { Name = "Foe", Clan = "", Mmr = new PlayerMmr { ParsedMmr = 3100 }, Race = 'T', Random = false }],
        };
        return new GameData { ReplayData = replay };
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
}
