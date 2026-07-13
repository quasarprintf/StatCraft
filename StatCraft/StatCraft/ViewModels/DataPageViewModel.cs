using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models;
using StatCraft.Services;

namespace StatCraft.ViewModels
{
    public partial class DataPageViewModel : ViewModelBase
    {
        private readonly SettingsRepository _settingsRepository;
        private readonly ReplayWatcherService _replayWatcherService;

        public DataPageViewModel(SettingsRepository settingsRepository, ReplayWatcherService replayWatcherService)
        {
            _settingsRepository = settingsRepository;
            _replayWatcherService = replayWatcherService;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveProfileLabel))]
        private Sc2Profile? _activeProfile;

        public string ActiveProfileLabel => ActiveProfile == null ? "No active session" : ActiveProfile.DisplayName;

        public event Action? SessionRequested;

        [RelayCommand]
        private void BeginSession() => SessionRequested?.Invoke();

        public void SetActiveProfile(Sc2Profile? profile)
        {
            ActiveProfile = profile;

            if (profile == null)
            {
                _replayWatcherService.Stop();
                return;
            }

            string baseReplayFolderPath = _settingsRepository.Load().BaseReplayFolderPath ?? "";
            string replayFolderPath = Path.Combine(baseReplayFolderPath, profile.ReplayFolderPathSuffix);
            _replayWatcherService.Start(replayFolderPath);
        }
    }
}
