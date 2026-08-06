using System.Net.Http;
using StatCraft.Models.Battlenet;
using StatCraft.Services.BackgroundService;
using StatCraft.Services.BattlenetApi;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;
using StatCraft.Tests.Mocks;

namespace StatCraft.Tests;

// ReplayWatcherService invokes ImportReplay from an async void handler, so anything this method lets
// escape terminates the process rather than being reported. These pin the "returns a message rather
// than throwing" contract against the file states the watcher can realistically catch a replay in —
// it reports a file the moment it appears, which may be before StarCraft II has finished writing it.
public class ReplayImportServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ReplayImportService _service;

    public ReplayImportServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);
        string dbPath = Path.Combine(_tempRoot, "statcraft.db");

        new AccountRepository(dbPath).Initialize();
        new BuildRepository(dbPath).Initialize();
        // Before GameDataRepository, whose MapName -> MapId migration writes into the Maps table.
        MapRepository mapRepository = new(dbPath);
        mapRepository.Initialize();
        GameDataRepository gameDataRepository = new(dbPath);
        gameDataRepository.Initialize();

        _service = new ReplayImportService(
            new MockLogger(),
            new ReplayDataExtractor(),
            gameDataRepository,
            mapRepository,
            new Sc2LadderService(new HttpClient(), new StubTokenProvider(), new MockLogger()));
    }

    private static Sc2Profile Profile => new() { RegionId = "1", RealmId = "1", ProfileId = 1234567, Name = "TestPlayer" };

    // Only asserts that something was reported: the decoder raises ArgumentNullException here, which is
    // odd enough that a future version could reasonably change it, and any of the guard's branches is a
    // fine answer for a file that isn't there.
    [Fact]
    public async Task ImportReplay_FileDoesNotExist_ReturnsMessageInsteadOfThrowing()
    {
        string? error = await _service.ImportReplay(Path.Combine(_tempRoot, "nope.SC2Replay"), Profile);

        Assert.NotNull(error);
    }

    // A corrupt file is permanent, so the message has to say so rather than suggest retrying.
    [Fact]
    public async Task ImportReplay_FileIsNotAReplay_ReportsItAsInvalid()
    {
        string path = Path.Combine(_tempRoot, "garbage.SC2Replay");
        File.WriteAllText(path, "this is not an MPQ archive");

        string? error = await _service.ImportReplay(path, Profile);

        Assert.NotNull(error);
        Assert.Contains("valid StarCraft II replay", error);
    }

    // The watcher race that motivated the guard: the file is on disk but still held open for writing.
    // Unlike a corrupt file this is transient, so the message must point at retrying.
    [Fact]
    public async Task ImportReplay_FileLockedForWriting_ReportsItAsRetryable()
    {
        string path = Path.Combine(_tempRoot, "locked.SC2Replay");
        File.WriteAllText(path, "partially written");

        using FileStream holdOpen = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        string? error = await _service.ImportReplay(path, Profile);

        Assert.NotNull(error);
        Assert.Contains("still be in use", error);
    }

    private sealed class StubTokenProvider() : BlizzardAppTokenProvider(null!, null!, null!, new MockLogger())
    {
        // No credentials, so the post-import MMR polling no-ops — none of these tests reach it anyway,
        // since every one of them fails before the game is ever inserted.
        public override Task<string?> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
