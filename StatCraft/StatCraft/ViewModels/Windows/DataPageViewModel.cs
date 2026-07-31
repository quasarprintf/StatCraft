using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.BackgroundService;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.ViewModels
{
    public partial class DataPageViewModel : ViewModelBase
    {
        private readonly SettingsRepository _settingsRepository;
        private readonly ReplayWatcherService _replayWatcherService;
        private readonly BuildRepository _buildRepository;
        private readonly GameDataRepository _gameDataRepository;
        private readonly Dictionary<(Race Player, Matchups Opponent), ObservableCollection<BuildNode>> _buildTreeCache = new();
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
        [NotifyPropertyChangedFor(nameof(ActiveProfileLabel), nameof(HasActiveSession))]
        private Sc2Profile? _activeProfile;

        public string ActiveProfileLabel => ActiveProfile == null ? "No active session" : ActiveProfile.DisplayName;

        // Drives visibility of session-only actions (e.g. importing a replay file).
        public bool HasActiveSession => ActiveProfile != null;

        public ObservableCollection<GameDataRowViewModel> Games { get; } = [];

        public event Action? SessionRequested;

        [RelayCommand]
        private void BeginSession() => SessionRequested?.Invoke();

        // Raised so the view can show a file picker and, if a file was chosen, call ImportReplayFile.
        public event Action? ImportReplayRequested;

        [RelayCommand]
        private void ImportReplay() => ImportReplayRequested?.Invoke();

        // The folder the file picker should default to and validate the user's selection against.
        public string? ReplayFolderPath => _replayWatcherService.WatchedFolderPath;

        // Returns null on success, or a user-facing message describing why the replay was rejected.
        public async Task<string?> ImportReplayFile(string filePath) => await _replayWatcherService.ImportReplay(filePath);

        // Raised instead of deleting immediately, so the view can show a confirmation dialog and, if
        // accepted, call ConfirmDeleteGame.
        public event Action<GameDataRowViewModel>? DeleteGameConfirmationRequested;

        [RelayCommand]
        private void DeleteGame(GameDataRowViewModel row) => DeleteGameConfirmationRequested?.Invoke(row);

        public void ConfirmDeleteGame(GameDataRowViewModel row)
        {
            _gameDataRepository.DeleteGame(row.GameId);
            Games.Remove(row);
        }

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

        // Guards against a duplicate row if the same underlying game is reported twice — e.g. a manual
        // import (via ImportReplayFile) of a replay the folder watcher already picked up, or vice versa.
        // InsertGame itself already dedupes by ReplayPath, so this only ever skips the redundant UI add.
        private void OnGameParsed(GameData game) => Dispatcher.UIThread.Post(() =>
        {
            if (Games.Any(row => row.GameId == game.GameId))
                return;
            Games.Add(WrapGame(game));
        });

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
        // Reloading the tree data doesn't by itself refresh an already-selected build's attribute
        // editors though (that list was built once, when the build was first selected), so each row
        // is asked to re-derive its own editors from the just-reloaded tree afterward.
        private void RefreshBuildTreeCache()
        {
            foreach (((Race player, Matchups opponent), ObservableCollection<BuildNode> tree) in _buildTreeCache)
            {
                tree.Clear();
                foreach (BuildNode node in _buildRepository.GetBuildsForMatchup(player, opponent))
                    tree.Add(node);
            }

            foreach (GameDataRowViewModel row in Games)
                row.RefreshAttributeEditors();
        }

        private GameDataRowViewModel WrapGame(GameData game) =>
            new GameDataRowViewModel(game, _gameDataRepository, GetBuildTree);

        private ObservableCollection<BuildNode>? GetBuildTree(Race? player, Matchups matchups)
        {
            if (player == null)
                return null;
            if (!_buildTreeCache.TryGetValue((player.Value, matchups), out ObservableCollection<BuildNode>? tree))
            {
                tree = new ObservableCollection<BuildNode>(_buildRepository.GetBuildsForMatchup(player.Value, matchups));
                _buildTreeCache[(player.Value, matchups)] = tree;
            }

            return tree;
        }
    }
}
