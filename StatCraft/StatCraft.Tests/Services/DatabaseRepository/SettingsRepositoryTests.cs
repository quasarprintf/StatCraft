using StatCraft.Models;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.Tests;

public class SettingsRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly SettingsRepository _repository;

    public SettingsRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);
        _repository = new SettingsRepository(Path.Combine(_tempRoot, "Settings.json"));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsThePath()
    {
        _repository.Save(new AppSettingsData { BaseReplayFolderPath = @"C:\Replays" });
        Assert.Equal(@"C:\Replays", _repository.Load().BaseReplayFolderPath);
    }

    // The Settings tab relies on this to redirect DataPageViewModel's replay watcher the moment a change
    // is saved, without the user having to end and restart their session.
    [Fact]
    public void Save_RaisesSettingsChanged()
    {
        bool raised = false;
        _repository.SettingsChanged += () => raised = true;

        _repository.Save(new AppSettingsData { BaseReplayFolderPath = @"C:\Replays" });

        Assert.True(raised);
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
