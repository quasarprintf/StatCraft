using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models;
using StatCraft.Models.Util;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.ViewModels
{
    // Lets the base replay folder — set once during first-run setup by SettingsPromptViewModel — be
    // changed afterward without reinstalling or hand-editing Settings.json. Saves through the same
    // SettingsRepository the startup prompt uses, so DataPageViewModel picks up the change and redirects
    // its replay watcher immediately if a session is currently active.
    public partial class SettingsPageViewModel : ViewModelBase
    {
        private readonly SettingsRepository _settingsRepository;

        public SettingsPageViewModel(SettingsRepository settingsRepository)
        {
            _settingsRepository = settingsRepository;
            BaseReplayFolderPath = _settingsRepository.Load().BaseReplayFolderPath ?? "";
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
        private string _baseReplayFolderPath = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string _errorMessage = "";

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
            _settingsRepository.Save(new AppSettingsData { BaseReplayFolderPath = BaseReplayFolderPath });
            JustSaved = true;
        }
    }
}
