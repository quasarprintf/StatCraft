using System;
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

        public event Action? Completed;

        private bool CanContinue() => !string.IsNullOrWhiteSpace(BaseReplayFolderPath);

        [RelayCommand(CanExecute = nameof(CanContinue))]
        private void Continue()
        {
            _settingsRepository.Save(new AppSettingsData { BaseReplayFolderPath = BaseReplayFolderPath });
            Completed?.Invoke();
        }
    }
}
