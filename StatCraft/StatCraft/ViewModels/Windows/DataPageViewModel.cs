using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Services.BackgroundService;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;

namespace StatCraft.ViewModels
{
    public partial class DataPageViewModel : ViewModelBase
    {
        private readonly SettingsRepository _settingsRepository;
        private readonly ReplayWatcherService _replayWatcherService;
        private readonly BuildRepository _buildRepository;
        private readonly GameDataRepository _gameDataRepository;
        private readonly Dictionary<Matchup, ObservableCollection<BuildNode>> _buildTreeCache = new();
        private bool _buildTreeCacheDirty;

        public DataPageViewModel(SettingsRepository settingsRepository, ReplayWatcherService replayWatcherService,
            BuildRepository buildRepository, GameDataRepository gameDataRepository)
        {
            _settingsRepository = settingsRepository;
            _replayWatcherService = replayWatcherService;
            _buildRepository = buildRepository;
            _gameDataRepository = gameDataRepository;
            _replayWatcherService.GameParsed += OnGameParsed;
            _buildRepository.BuildsChanged += OnBuildsChanged;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveProfileLabel))]
        private Sc2Profile? _activeProfile;

        public string ActiveProfileLabel => ActiveProfile == null ? "No active session" : ActiveProfile.DisplayName;

        public ObservableCollection<GameDataRowViewModel> Games { get; } = [];

        public event Action? SessionRequested;

        [RelayCommand]
        private void BeginSession() => SessionRequested?.Invoke();

        public async Task SetActiveProfile(Sc2Profile? profile)
        {
            ActiveProfile = profile;
            Games.Clear();

            if (profile == null)
            {
                await _replayWatcherService.Stop();
                return;
            }

            foreach (GameData game in _gameDataRepository.GetGamesForProfile(profile.Id))
                Games.Add(WrapGame(game));

            string baseReplayFolderPath = _settingsRepository.Load().BaseReplayFolderPath ?? "";
            string replayFolderPath = Path.Combine(baseReplayFolderPath, profile.ReplayFolderPathSuffix);
            await _replayWatcherService.Start(replayFolderPath, profile);
        }

        private void OnGameParsed(GameData game) => Dispatcher.UIThread.Post(() => Games.Add(WrapGame(game)));

        // Don't reload immediately — builds can change many times in a row while editing on the Builds
        // tab. Just remember a reload is owed, and pay for it once when the user actually comes back
        // to the Data tab (see NotifyActivated).
        private void OnBuildsChanged() => _buildTreeCacheDirty = true;

        // Called by DataPage's code-behind when the Data tab becomes visible.
        public void NotifyActivated()
        {
            if (!_buildTreeCacheDirty)
                return;

            _buildTreeCacheDirty = false;
            RefreshBuildTreeCache();
        }

        // Refresh every cached matchup tree in place, so any GameDataRowViewModel/BuildPathPicker
        // holding a reference to one of these collections picks up the change automatically via its
        // own CollectionChanged notifications, without needing to touch existing rows individually.
        private void RefreshBuildTreeCache()
        {
            foreach ((Matchup matchup, ObservableCollection<BuildNode> tree) in _buildTreeCache)
            {
                tree.Clear();
                foreach (BuildNode node in _buildRepository.GetBuildsForMatchup(matchup))
                    tree.Add(node);
            }
        }

        private GameDataRowViewModel WrapGame(GameData game)
        {
            Matchup? matchup = MatchupResolver.FromOpponents(game.ReplayData.Opponents);
            return new GameDataRowViewModel(game, _gameDataRepository, GetBuildTree(matchup));
        }

        private ObservableCollection<BuildNode>? GetBuildTree(Matchup? matchup)
        {
            if (matchup == null)
                return null;

            if (!_buildTreeCache.TryGetValue(matchup.Value, out ObservableCollection<BuildNode>? tree))
            {
                tree = new ObservableCollection<BuildNode>(_buildRepository.GetBuildsForMatchup(matchup.Value));
                _buildTreeCache[matchup.Value] = tree;
            }

            return tree;
        }
    }
}
