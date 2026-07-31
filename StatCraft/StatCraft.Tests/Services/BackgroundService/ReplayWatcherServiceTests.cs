using StatCraft.Services.BackgroundService;

namespace StatCraft.Tests;

public class ReplayWatcherServiceTests : IAsyncDisposable
{
    private readonly string _folderPath;
    private readonly ReplayWatcherService _watcher;
    private readonly List<string> _foundFiles = [];

    public ReplayWatcherServiceTests()
    {
        _folderPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_folderPath);

        _watcher = new ReplayWatcherService(new Mocks.MockLogger());
        _watcher.NewReplayFileFound += _foundFiles.Add;
    }

    [Fact]
    public void WatchedFolderPath_BeforeStart_IsNull()
    {
        Assert.Null(_watcher.WatchedFolderPath);
    }

    [Fact]
    public async Task WatchedFolderPath_AfterStart_ReflectsTheWatchedFolder()
    {
        await _watcher.Start(_folderPath);

        Assert.Equal(_folderPath, _watcher.WatchedFolderPath);
    }

    [Fact]
    public async Task WatchedFolderPath_AfterStop_IsNullAgain()
    {
        await _watcher.Start(_folderPath);
        await _watcher.Stop();

        Assert.Null(_watcher.WatchedFolderPath);
    }

    [Fact]
    public async Task Start_IgnoresFilesThatExistBeforeWatchingBegins()
    {
        File.WriteAllText(Path.Combine(_folderPath, "old1.SC2Replay"), "");
        File.WriteAllText(Path.Combine(_folderPath, "old2.SC2Replay"), "");

        await _watcher.Start(_folderPath);
        _watcher.CheckNow();

        Assert.Empty(_foundFiles);
    }

    [Fact]
    public async Task CheckNow_NewFileAppearsAfterStart_IsReported()
    {
        File.WriteAllText(Path.Combine(_folderPath, "old.SC2Replay"), "");
        await _watcher.Start(_folderPath);

        string newFile = Path.Combine(_folderPath, "new.SC2Replay");
        File.WriteAllText(newFile, "");
        _watcher.CheckNow();

        Assert.Equal([newFile], _foundFiles);
    }

    [Fact]
    public async Task CheckNow_SameFileSeenTwice_IsOnlyReportedOnce()
    {
        await _watcher.Start(_folderPath);

        string newFile = Path.Combine(_folderPath, "new.SC2Replay");
        File.WriteAllText(newFile, "");
        _watcher.CheckNow();
        _watcher.CheckNow();

        Assert.Equal([newFile], _foundFiles);
    }

    [Fact]
    public async Task Stop_ThenRestart_ForgetsPreviouslyKnownFiles()
    {
        string file = Path.Combine(_folderPath, "existing.SC2Replay");
        File.WriteAllText(file, "");

        await _watcher.Start(_folderPath);
        _watcher.CheckNow();
        Assert.Empty(_foundFiles);

        await _watcher.Stop();
        await _watcher.Start(_folderPath);
        _watcher.CheckNow();

        Assert.Empty(_foundFiles);
    }

    [Fact]
    public async Task CheckNow_FolderDoesNotExist_DoesNotThrow()
    {
        await _watcher.Start(Path.Combine(_folderPath, "does-not-exist"));
        _watcher.CheckNow();

        Assert.Empty(_foundFiles);
    }

    [Fact]
    public void CheckNow_NeverStarted_DoesNotThrow()
    {
        _watcher.CheckNow();

        Assert.Empty(_foundFiles);
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
}
