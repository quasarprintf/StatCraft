using StatCraft.Services;
using StatCraft.ViewModels;

namespace StatCraft.Tests;

public class SettingsPromptViewModelTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly SettingsRepository _settingsRepository;

    public SettingsPromptViewModelTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);
        _settingsRepository = new SettingsRepository(Path.Combine(_tempRoot, "Settings.json"));
    }

    [Fact]
    public void Continue_FolderWithoutAccountsSubfolder_SetsErrorAndDoesNotSave()
    {
        string replayFolder = Path.Combine(_tempRoot, "NoAccountsHere");
        Directory.CreateDirectory(replayFolder);

        SettingsPromptViewModel vm = new SettingsPromptViewModel(_settingsRepository) { BaseReplayFolderPath = replayFolder };
        bool completedRaised = false;
        vm.Completed += () => completedRaised = true;

        vm.ContinueCommand.Execute(null);

        Assert.True(vm.HasError);
        Assert.False(completedRaised);
        Assert.Null(_settingsRepository.Load().BaseReplayFolderPath);
    }

    [Fact]
    public void Continue_FolderWithAccountsSubfolder_SavesAndRaisesCompleted()
    {
        string replayFolder = Path.Combine(_tempRoot, "ValidReplayFolder");
        Directory.CreateDirectory(Path.Combine(replayFolder, "Accounts"));

        SettingsPromptViewModel vm = new SettingsPromptViewModel(_settingsRepository) { BaseReplayFolderPath = replayFolder };
        bool completedRaised = false;
        vm.Completed += () => completedRaised = true;

        vm.ContinueCommand.Execute(null);

        Assert.False(vm.HasError);
        Assert.True(completedRaised);
        Assert.Equal(replayFolder, _settingsRepository.Load().BaseReplayFolderPath);
    }

    [Fact]
    public void Continue_NonexistentFolder_SetsErrorAndDoesNotSave()
    {
        string replayFolder = Path.Combine(_tempRoot, "DoesNotExist");

        SettingsPromptViewModel vm = new SettingsPromptViewModel(_settingsRepository) { BaseReplayFolderPath = replayFolder };
        vm.ContinueCommand.Execute(null);

        Assert.True(vm.HasError);
        Assert.Null(_settingsRepository.Load().BaseReplayFolderPath);
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
