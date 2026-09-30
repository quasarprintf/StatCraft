using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.Util;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.ViewModels.Windows;

// Lets the base replay folder — set once during first-run setup by SettingsPromptViewModel — be
// changed afterward without reinstalling or hand-editing Settings.json. Saves through the same
// SettingsRepository the startup prompt uses, so DataPageViewModel picks up the change and redirects
// its replay watcher immediately if a session is currently active.
public partial class SettingsPageViewModel : ViewModelBase
{
    private readonly SettingsRepository _settingsRepo;

    // Set once the constructor has finished reading the saved settings in. Until then, assigning those
    // properties is hydration rather than a user edit, and must not save the file it just read.
    private readonly bool _loaded;

    public SettingsPageViewModel(SettingsRepository settingsRepository)
    {
        _settingsRepo = settingsRepository;
        AppSettingsData settings = _settingsRepo.Load();
        BaseReplayFolderPath = settings.BaseReplayFolderPath ?? "";
        UseTeamColors = settings.UseTeamColors;
        _loaded = true;
    }

    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [ObservableProperty] public partial string BaseReplayFolderPath { get; set; } = "";

    [ObservableProperty] public partial bool UseTeamColors { get; set; }

    partial void OnUseTeamColorsChanged(bool value)
    {
        if (!_loaded)
            return;

        _settingsRepo.Save(new AppSettingsData { BaseReplayFolderPath = BaseReplayFolderPath, UseTeamColors = value });
    }

    [NotifyPropertyChangedFor(nameof(HasError))]
    [ObservableProperty] public partial string ErrorMessage { get; set; } = "";

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    // Shown only until the path is touched again, so it can't be mistaken for still describing the
    // current (possibly since-edited) text in the box.
    [ObservableProperty] public partial bool JustSaved { get; set; }

    partial void OnBaseReplayFolderPathChanged(string value)
    {
        ErrorMessage = "";
        JustSaved = false;
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(BaseReplayFolderPath);

    // Same "Accounts" subfolder check the first-run prompt uses (SettingsPromptViewModel.Continue) —
    // a path that fails it would leave the replay watcher pointed somewhere that will never see a
    // replay.
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (!Directory.Exists(Path.Combine(BaseReplayFolderPath, "Accounts")))
        {
            ErrorMessage = "This folder doesn't contain an \"Accounts\" subfolder. Select your StarCraft II replay folder.";
            JustSaved = false;
            return;
        }

        ErrorMessage = "";
        _settingsRepo.Save(new AppSettingsData { BaseReplayFolderPath = BaseReplayFolderPath });
        JustSaved = true;
    }
}
