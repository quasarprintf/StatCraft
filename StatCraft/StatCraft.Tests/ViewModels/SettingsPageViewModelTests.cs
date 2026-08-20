using StatCraft.Services.DatabaseRepository;
using StatCraft.ViewModels.Windows;

namespace StatCraft.Tests;

public class SettingsPageViewModelTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly SettingsRepository _settingsRepository;

    public SettingsPageViewModelTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempRoot);
        _settingsRepository = new SettingsRepository(Path.Combine(_tempRoot, "Settings.json"));
    }

    [Fact]
    public void Constructor_ExistingSetting_LoadsCurrentPath()
    {
        string replayFolder = Path.Combine(_tempRoot, "ValidReplayFolder");
        Directory.CreateDirectory(Path.Combine(replayFolder, "Accounts"));
        _settingsRepository.Save(new Models.Util.AppSettingsData { BaseReplayFolderPath = replayFolder });

        SettingsPageViewModel vm = new(_settingsRepository);

        Assert.Equal(replayFolder, vm.BaseReplayFolderPath);
    }

    [Fact]
    public void Save_FolderWithoutAccountsSubfolder_SetsErrorAndDoesNotPersist()
    {
        string original = Path.Combine(_tempRoot, "Original");
        Directory.CreateDirectory(Path.Combine(original, "Accounts"));
        _settingsRepository.Save(new Models.Util.AppSettingsData { BaseReplayFolderPath = original });

        string replayFolder = Path.Combine(_tempRoot, "NoAccountsHere");
        Directory.CreateDirectory(replayFolder);

        SettingsPageViewModel vm = new(_settingsRepository) { BaseReplayFolderPath = replayFolder };
        vm.SaveCommand.Execute(null);

        Assert.True(vm.HasError);
        Assert.False(vm.JustSaved);
        Assert.Equal(original, _settingsRepository.Load().BaseReplayFolderPath);
    }

    [Fact]
    public void Save_FolderWithAccountsSubfolder_PersistsAndSetsJustSaved()
    {
        string replayFolder = Path.Combine(_tempRoot, "ValidReplayFolder");
        Directory.CreateDirectory(Path.Combine(replayFolder, "Accounts"));

        SettingsPageViewModel vm = new(_settingsRepository) { BaseReplayFolderPath = replayFolder };
        vm.SaveCommand.Execute(null);

        Assert.False(vm.HasError);
        Assert.True(vm.JustSaved);
        Assert.Equal(replayFolder, _settingsRepository.Load().BaseReplayFolderPath);
    }

    // Editing the path after a save invalidates the confirmation and any leftover error — both would
    // otherwise describe text the box no longer shows.
    [Fact]
    public void ChangingPath_AfterSave_ClearsJustSavedAndError()
    {
        string replayFolder = Path.Combine(_tempRoot, "ValidReplayFolder");
        Directory.CreateDirectory(Path.Combine(replayFolder, "Accounts"));

        SettingsPageViewModel vm = new(_settingsRepository) { BaseReplayFolderPath = replayFolder };
        vm.SaveCommand.Execute(null);
        Assert.True(vm.JustSaved);

        vm.BaseReplayFolderPath = Path.Combine(_tempRoot, "SomewhereElse");

        Assert.False(vm.JustSaved);
        Assert.False(vm.HasError);
    }

    [Fact]
    public void SaveCommand_BlankPath_CannotExecute()
    {
        SettingsPageViewModel vm = new(_settingsRepository) { BaseReplayFolderPath = "   " };
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    // Unlike the folder path, this checkbox has no Save button of its own — it must persist as soon as
    // it's toggled.
    [Fact]
    public void TogglingUseTeamColors_PersistsImmediatelyWithoutSaveCommand()
    {
        SettingsPageViewModel vm = new(_settingsRepository);
        Assert.False(vm.UseTeamColors);

        vm.UseTeamColors = true;

        Assert.True(_settingsRepository.Load().UseTeamColors);
    }

    // Toggling the checkbox must not clobber a folder path that was already saved separately (or vice
    // versa) — Save() always writes a full AppSettingsData, so both fields have to round-trip together.
    [Fact]
    public void TogglingUseTeamColors_DoesNotClobberAnAlreadySavedFolderPath()
    {
        string replayFolder = Path.Combine(_tempRoot, "ValidReplayFolder");
        Directory.CreateDirectory(Path.Combine(replayFolder, "Accounts"));
        SettingsPageViewModel vm = new(_settingsRepository) { BaseReplayFolderPath = replayFolder };
        vm.SaveCommand.Execute(null);

        vm.UseTeamColors = true;

        Assert.Equal(replayFolder, _settingsRepository.Load().BaseReplayFolderPath);
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
