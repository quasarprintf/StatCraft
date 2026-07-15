using StatCraft.Services;

namespace StatCraft.Tests;

public class ReplayWatcherServiceTests : IAsyncDisposable
{
    private readonly string _folderPath;
    private readonly RecordingReplayWatcherService _watcher;

    public ReplayWatcherServiceTests()
    {
        _folderPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_folderPath);

        _watcher = new RecordingReplayWatcherService(new Mocks.MockLogger());
    }

    [Fact]
    public async Task Start_IgnoresFilesThatExistBeforeWatchingBegins()
    {
        File.WriteAllText(Path.Combine(_folderPath, "old1.SC2Replay"), "");
        File.WriteAllText(Path.Combine(_folderPath, "old2.SC2Replay"), "");

        await _watcher.Start(_folderPath, new Models.Sc2Profile());
        _watcher.CheckNow();

        Assert.Empty(_watcher.ProcessedFiles);
    }

    [Fact]
    public async Task CheckNow_NewFileAppearsAfterStart_IsProcessed()
    {
        File.WriteAllText(Path.Combine(_folderPath, "old.SC2Replay"), "");
        await _watcher.Start(_folderPath, new Models.Sc2Profile());

        string newFile = Path.Combine(_folderPath, "new.SC2Replay");
        File.WriteAllText(newFile, "");
        _watcher.CheckNow();

        Assert.Equal([newFile], _watcher.ProcessedFiles);
    }

    [Fact]
    public async Task CheckNow_SameFileSeenTwice_IsOnlyProcessedOnce()
    {
        await _watcher.Start(_folderPath, new Models.Sc2Profile());

        string newFile = Path.Combine(_folderPath, "new.SC2Replay");
        File.WriteAllText(newFile, "");
        _watcher.CheckNow();
        _watcher.CheckNow();

        Assert.Equal([newFile], _watcher.ProcessedFiles);
    }

    [Fact]
    public async Task Stop_ThenRestart_ForgetsPreviouslyKnownFiles()
    {
        string file = Path.Combine(_folderPath, "existing.SC2Replay");
        File.WriteAllText(file, "");

        await _watcher.Start(_folderPath, new Models.Sc2Profile());
        _watcher.CheckNow();
        Assert.Empty(_watcher.ProcessedFiles);

        await _watcher.Stop();
        await _watcher.Start(_folderPath, new Models.Sc2Profile());
        _watcher.CheckNow();

        Assert.Empty(_watcher.ProcessedFiles);
    }

    [Fact]
    public async Task CheckNow_FolderDoesNotExist_DoesNotThrow()
    {
        await _watcher.Start(Path.Combine(_folderPath, "does-not-exist"), new Models.Sc2Profile());
        _watcher.CheckNow();

        Assert.Empty(_watcher.ProcessedFiles);
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

    private class RecordingReplayWatcherService(ILogger logger) : ReplayWatcherService(logger)
    {
        public List<string> ProcessedFiles { get; } = [];

        protected override void ProcessReplay(string filePath) => ProcessedFiles.Add(filePath);
    }
}
