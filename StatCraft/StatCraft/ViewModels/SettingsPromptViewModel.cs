using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models;
using StatCraft.Services;

namespace StatCraft.ViewModels
{
    public partial class SettingsPromptViewModel : ViewModelBase
    {
        private readonly SettingsRepository _settingsRepository;

        public SettingsPromptViewModel(SettingsRepository settingsRepository)
        {
            _settingsRepository = settingsRepository;
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
        private string _baseReplayFolderPath = "";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasError))]
        private string _errorMessage = "";

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public event Action? Completed;

        private bool CanContinue() => !string.IsNullOrWhiteSpace(BaseReplayFolderPath);

        [RelayCommand(CanExecute = nameof(CanContinue))]
        private void Continue()
        {
            if (!Directory.Exists(Path.Combine(BaseReplayFolderPath, "Accounts")))
            {
                ErrorMessage = "This folder doesn't contain an \"Accounts\" subfolder. Select your StarCraft II replay folder.";
                return;
            }

            ErrorMessage = "";
            _settingsRepository.Save(new AppSettingsData { BaseReplayFolderPath = BaseReplayFolderPath });
            Completed?.Invoke();
        }
    }
}
