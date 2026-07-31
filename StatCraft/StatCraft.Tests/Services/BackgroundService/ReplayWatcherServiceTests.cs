using StatCraft.Models.Battlenet;
using StatCraft.Services.BackgroundService;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;

namespace StatCraft.Tests;

public class ReplayWatcherServiceTests : IAsyncDisposable
{
    private readonly string _folderPath;
    private readonly RecordingReplayWatcherService _watcher;

    public ReplayWatcherServiceTests()
    {
        _folderPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_folderPath);

        _watcher = new RecordingReplayWatcherService(new Mocks.MockLogger(), new ReplayDataExtractor(), new GameDataRepository(":memory:"));
    }

    [Fact]
    public void WatchedFolderPath_BeforeStart_IsNull()
    {
        Assert.Null(_watcher.WatchedFolderPath);
    }

    [Fact]
    public async Task WatchedFolderPath_AfterStart_ReflectsTheWatchedFolder()
    {
        await _watcher.Start(_folderPath, new Sc2Profile());

        Assert.Equal(_folderPath, _watcher.WatchedFolderPath);
    }

    [Fact]
    public async Task WatchedFolderPath_AfterStop_IsNullAgain()
    {
        await _watcher.Start(_folderPath, new Sc2Profile());
        await _watcher.Stop();

        Assert.Null(_watcher.WatchedFolderPath);
    }

    [Fact]
    public async Task Start_IgnoresFilesThatExistBeforeWatchingBegins()
    {
        File.WriteAllText(Path.Combine(_folderPath, "old1.SC2Replay"), "");
        File.WriteAllText(Path.Combine(_folderPath, "old2.SC2Replay"), "");

        await _watcher.Start(_folderPath, new Sc2Profile());
        await _watcher.CheckNow();

        Assert.Empty(_watcher.ProcessedFiles);
    }

    [Fact]
    public async Task CheckNow_NewFileAppearsAfterStart_IsProcessed()
    {
        File.WriteAllText(Path.Combine(_folderPath, "old.SC2Replay"), "");
        await _watcher.Start(_folderPath, new Sc2Profile());

        string newFile = Path.Combine(_folderPath, "new.SC2Replay");
        File.WriteAllText(newFile, "");
        await _watcher.CheckNow();

        Assert.Equal([newFile], _watcher.ProcessedFiles);
    }

    [Fact]
    public async Task CheckNow_SameFileSeenTwice_IsOnlyProcessedOnce()
    {
        await _watcher.Start(_folderPath, new Sc2Profile());

        string newFile = Path.Combine(_folderPath, "new.SC2Replay");
        File.WriteAllText(newFile, "");
        await _watcher.CheckNow();
        await _watcher.CheckNow();

        Assert.Equal([newFile], _watcher.ProcessedFiles);
    }

    [Fact]
    public async Task Stop_ThenRestart_ForgetsPreviouslyKnownFiles()
    {
        string file = Path.Combine(_folderPath, "existing.SC2Replay");
        File.WriteAllText(file, "");

        await _watcher.Start(_folderPath, new Sc2Profile());
        await _watcher.CheckNow();
        Assert.Empty(_watcher.ProcessedFiles);

        await _watcher.Stop();
        await _watcher.Start(_folderPath, new Sc2Profile());
        await _watcher.CheckNow();

        Assert.Empty(_watcher.ProcessedFiles);
    }

    [Fact]
    public async Task CheckNow_FolderDoesNotExist_DoesNotThrow()
    {
        await _watcher.Start(Path.Combine(_folderPath, "does-not-exist"), new Sc2Profile());
        await _watcher.CheckNow();

        Assert.Empty(_watcher.ProcessedFiles);
    }

    [Fact]
    public async Task ImportReplay_ProcessesTheGivenFileDirectly()
    {
        string file = Path.Combine(_folderPath, "manual.SC2Replay");
        File.WriteAllText(file, "");

        await _watcher.ImportReplay(file);

        Assert.Equal([file], _watcher.ProcessedFiles);
    }

    [Fact]
    public async Task ImportReplay_ReturnsWhateverProcessReplayReturns()
    {
        string file = Path.Combine(_folderPath, "bad.SC2Replay");
        File.WriteAllText(file, "");
        _watcher.NextResult = "Could not read \"bad.SC2Replay\" — it doesn't look like a valid StarCraft II replay.";

        string? result = await _watcher.ImportReplay(file);

        Assert.Equal(_watcher.NextResult, result);
    }

    [Fact]
    public async Task ImportReplay_Success_ReturnsNull()
    {
        string file = Path.Combine(_folderPath, "good.SC2Replay");
        File.WriteAllText(file, "");

        string? result = await _watcher.ImportReplay(file);

        Assert.Null(result);
    }

    [Fact]
    public async Task ImportReplay_FileOutsideWatchedFolder_IsStillProcessed()
    {
        string outsideFile = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + "-outside.SC2Replay");
        File.WriteAllText(outsideFile, "");
        try
        {
            await _watcher.Start(_folderPath, new Sc2Profile());

            await _watcher.ImportReplay(outsideFile);

            Assert.Equal([outsideFile], _watcher.ProcessedFiles);
        }
        finally
        {
            try
            {
                if (File.Exists(outsideFile))
                    File.Delete(outsideFile);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _watcher.DisposeAsync();
        try
        {
            if (Directory.Exists(_folderPath))
                Directory.Delete(_folderPath, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    private class RecordingReplayWatcherService(ILogger logger, ReplayDataExtractor replayDataExtractor, GameDataRepository gameDataRepository)
        : ReplayWatcherService(logger, replayDataExtractor, gameDataRepository)
    {
        public List<string> ProcessedFiles { get; } = [];
        public string? NextResult { get; set; }

        protected override Task<string?> ProcessReplay(string filePath)
        {
            ProcessedFiles.Add(filePath);
            return Task.FromResult(NextResult);
        }
    }
}
