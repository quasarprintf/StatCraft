using StatCraft.Services;

namespace StatCraft.Tests;

public class ReplayWatcherServiceTests : IDisposable
{
    private readonly string _folderPath;
    private readonly RecordingReplayWatcherService _watcher;

    public ReplayWatcherServiceTests()
    {
        _folderPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_folderPath);
        _watcher = new RecordingReplayWatcherService();
    }

    [Fact]
    public void Start_IgnoresFilesThatExistBeforeWatchingBegins()
    {
        File.WriteAllText(Path.Combine(_folderPath, "old1.SC2Replay"), "");
        File.WriteAllText(Path.Combine(_folderPath, "old2.SC2Replay"), "");

        _watcher.Start(_folderPath);
        _watcher.CheckNow();

        Assert.Empty(_watcher.ProcessedFiles);
    }

    [Fact]
    public void CheckNow_NewFileAppearsAfterStart_IsProcessed()
    {
        File.WriteAllText(Path.Combine(_folderPath, "old.SC2Replay"), "");
        _watcher.Start(_folderPath);

        string newFile = Path.Combine(_folderPath, "new.SC2Replay");
        File.WriteAllText(newFile, "");
        _watcher.CheckNow();

        Assert.Equal([newFile], _watcher.ProcessedFiles);
    }

    [Fact]
    public void CheckNow_SameFileSeenTwice_IsOnlyProcessedOnce()
    {
        _watcher.Start(_folderPath);

        string newFile = Path.Combine(_folderPath, "new.SC2Replay");
        File.WriteAllText(newFile, "");
        _watcher.CheckNow();
        _watcher.CheckNow();

        Assert.Equal([newFile], _watcher.ProcessedFiles);
    }

    [Fact]
    public void Stop_ThenRestart_ForgetsPreviouslyKnownFiles()
    {
        string file = Path.Combine(_folderPath, "existing.SC2Replay");
        File.WriteAllText(file, "");

        _watcher.Start(_folderPath);
        _watcher.CheckNow();
        Assert.Empty(_watcher.ProcessedFiles);

        _watcher.Stop();
        _watcher.Start(_folderPath);
        _watcher.CheckNow();

        Assert.Empty(_watcher.ProcessedFiles);
    }

    [Fact]
    public void CheckNow_FolderDoesNotExist_DoesNotThrow()
    {
        _watcher.Start(Path.Combine(_folderPath, "does-not-exist"));
        _watcher.CheckNow();

        Assert.Empty(_watcher.ProcessedFiles);
    }

    public void Dispose()
    {
        _watcher.Dispose();
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

    private class RecordingReplayWatcherService : ReplayWatcherService
    {
        public List<string> ProcessedFiles { get; } = [];

        protected override void ProcessReplay(string filePath) => ProcessedFiles.Add(filePath);
    }
}
