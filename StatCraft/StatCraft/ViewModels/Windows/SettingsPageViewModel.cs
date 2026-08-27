using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.Util;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.ViewModels.Windows
{
    // Lets the base replay folder — set once during first-run setup by SettingsPromptViewModel — be
    // changed afterward without reinstalling or hand-editing Settings.json. Saves through the same
    // SettingsRepository the startup prompt uses, so DataPageViewModel picks up the change and redirects
    // its replay watcher immediately if a session is currently active.
    public partial class SettingsPageViewModel : ViewModelBase
    {
        private readonly SettingsRepository _settingsRepo;

        public SettingsPageViewModel(SettingsRepository settingsRepository)
        {
            _settingsRepo = settingsRepository;
            AppSettingsData settings = _settingsRepo.Load();
            BaseReplayFolderPath = settings.BaseReplayFolderPath ?? "";
            // Assigned to the backing field, not the property, so hydrating this from disk doesn't
            // immediately trigger OnUseTeamColorsChanged and re-save the file it was just read from.
            _useTeamColors = settings.UseTeamColors;
        }

        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        [ObservableProperty] private string _baseReplayFolderPath = "";

        [ObservableProperty] private bool _useTeamColors;

        partial void OnUseTeamColorsChanged(bool value) =>
            _settingsRepo.Save(new AppSettingsData { BaseReplayFolderPath = BaseReplayFolderPath, UseTeamColors = value });

        [NotifyPropertyChangedFor(nameof(HasError))]
        [ObservableProperty] private string _errorMessage = "";

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        // Shown only until the path is touched again, so it can't be mistaken for still describing the
        // current (possibly since-edited) text in the box.
        [ObservableProperty] private bool _justSaved;

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
}
