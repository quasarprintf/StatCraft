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
using System.Threading;
using StatCraft.Services.BackgroundService;
using StatCraft.Services.BattlenetApi;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataParsing;

namespace StatCraft.ViewModels
{
    public partial class DataPageViewModel : ViewModelBase
    {
        private readonly SettingsRepository _settingsRepository;
        private readonly ReplayWatcherService _replayWatcherService;
        private readonly ReplayImportService _replayImportService;
        private readonly AccountRepository _accountRepository;
        private readonly BuildRepository _buildRepository;
        private readonly GameDataRepository _gameDataRepository;
        private readonly Sc2LadderService _ladderService;
        private readonly Dictionary<(Race Player, Matchups Opponent), ObservableCollection<BuildNode>> _buildTreeCache = new();
        private bool _buildTreeCacheDirty;

        // The profile-scoped superset before the other (in-memory) filter dimensions are applied —
        // Games is always a filtered projection of this, never populated directly.
        private List<GameData> _loadedGames = [];

        public DataPageFiltersViewModel Filters { get; }

        public DataPageViewModel(SettingsRepository settingsRepository, ReplayWatcherService replayWatcherService,
            ReplayImportService replayImportService, AccountRepository accountRepository, BuildRepository buildRepository,
            GameDataRepository gameDataRepository, Sc2LadderService ladderService)
        {
            _settingsRepository = settingsRepository;
            _replayWatcherService = replayWatcherService;
            _replayImportService = replayImportService;
            _accountRepository = accountRepository;
            _buildRepository = buildRepository;
            _gameDataRepository = gameDataRepository;
            _ladderService = ladderService;
            _replayWatcherService.NewReplayFileFound += OnNewReplayFileFound;
            _replayImportService.GameParsed += OnGameParsed;
            _replayImportService.GameMmrUpdated += OnGameMmrUpdated;
            _buildRepository.BuildsChanged += OnBuildsChanged;

            Filters = new DataPageFiltersViewModel(buildRepository);
            Filters.ProfileSelectionChanged += async () => await ReloadGamesFromDatabase();
            Filters.OtherFiltersChanged += ApplyFilters;

            // Give the two always-visible filters sensible defaults as soon as the page exists, rather
            // than leaving them blank until a session actually starts.
            Filters.RefreshProfileOptions(_accountRepository.GetAllProfiles());
            DateTime today = DateTime.Today;
            Filters.FromDate = today;
            Filters.ToDate = today;
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ActiveProfileLabel), nameof(HasActiveSession))]
        private Sc2Profile? _activeProfile;

        public string ActiveProfileLabel => ActiveProfile == null ? "No active session" : ActiveProfile.DisplayName;

        // Drives visibility of session-only actions (e.g. importing a replay file).
        public bool HasActiveSession => ActiveProfile != null;

        public ObservableCollection<GameDataRowViewModel> Games { get; } = [];

        // The active session profile's current ladder rating, one entry per race it has placed in. Empty
        // until the lookup completes, and stays empty when there's nothing to show (no saved API
        // credentials, or a profile with no current-season ladder).
        public ObservableCollection<RaceMmrViewModel> CurrentMmrs { get; } = [];

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
        public async Task<string?> ImportReplayFile(string filePath) => await _replayImportService.ImportReplay(filePath, ActiveProfile!);

        // Raised instead of deleting immediately, so the view can show a confirmation dialog and, if
        // accepted, call ConfirmDeleteGame.
        public event Action<GameDataRowViewModel>? DeleteGameConfirmationRequested;

        [RelayCommand]
        private void DeleteGame(GameDataRowViewModel row) => DeleteGameConfirmationRequested?.Invoke(row);

        public void ConfirmDeleteGame(GameDataRowViewModel row)
        {
            _gameDataRepository.DeleteGame(row.GameId);
            _loadedGames.RemoveAll(g => g.GameId == row.GameId);
            Games.Remove(row);
        }

        // The filter bar is a display/query layer on top of session state, not a replacement for it —
        // ActiveProfile/the replay watcher/ImportReplayFile all keep referring to exactly this one
        // profile regardless of which profiles are checked in the filter bar.
        public async Task SetActiveProfile(Sc2Profile? profile)
        {
            ActiveProfile = profile;

            if (profile == null)
            {
                _loadedGames = [];
                Games.Clear();
                CurrentMmrs.Clear();
                await _replayWatcherService.Stop();
                return;
            }

            // Not awaited: a network round-trip shouldn't hold up the session starting or the games
            // table appearing. The header simply fills in whenever the lookup lands.
            CurrentMmrs.Clear();
            _ = LoadCurrentMmrs(profile);

            // Every session start collapses the profile filter back to just this profile and the date
            // range back to today, per spec, even if the user had broadened either beforehand.
            Filters.RefreshProfileOptions(_accountRepository.GetAllProfiles());
            Filters.SetSingleActiveProfile(profile);
            await ReloadGamesFromDatabase();

            string baseReplayFolderPath = _settingsRepository.Load().BaseReplayFolderPath ?? "";
            string replayFolderPath = Path.Combine(baseReplayFolderPath, profile.ReplayFolderPathSuffix);
            await _replayWatcherService.Start(replayFolderPath);
        }

        // The watcher only reports that a file appeared — importing it (and any failure handling) is
        // ReplayImportService's job. Failures here run unattended in the background, so they're only
        // logged (inside ImportReplay itself), not surfaced as a dialog like a manual import's would be.
        private async void OnNewReplayFileFound(string filePath)
        {
            if (ActiveProfile == null)
                return;
            await _replayImportService.ImportReplay(filePath, ActiveProfile);
        }

        // Guards against a duplicate entry if the same underlying game is reported twice — e.g. a manual
        // import (via ImportReplayFile) of a replay the folder watcher already picked up, or vice versa.
        // InsertGame itself already dedupes by ReplayPath, so this only ever skips the redundant add.
        // A freshly-imported replay may not immediately show up in Games if the current filters exclude
        // it (e.g. the date range no longer covers today) — that's the correct result of real filtering.
        private void OnGameParsed(GameData game) => Dispatcher.UIThread.Post(() =>
        {
            if (_loadedGames.Any(g => g.GameId == game.GameId))
                return;
            _loadedGames.Add(game);
            ApplyFilters();
        });

        // Arrives minutes after the game was imported, on a background polling task. The row's underlying
        // GamePlayer has already been mutated in place, so this only needs to prompt the row to
        // re-announce its derived MMR properties — no reload or re-filter.
        private void OnGameMmrUpdated(GameData game) => Dispatcher.UIThread.Post(() =>
        {
            Games.FirstOrDefault(row => row.GameId == game.GameId)?.RefreshMmrChange();

            // The freshly-observed rating is by definition this profile's current one for the race just
            // played, so the header can be updated straight from it rather than re-querying the API.
            GamePlayer self = game.ReplayData.Player;
            if (self.MmrAfter is { } mmrAfter && self.Race.AsRace() is { } race)
                UpsertCurrentMmr(race, mmrAfter);
        });

        // Keeps CurrentMmrs sorted by race so entries don't jump around as they're replaced.
        private void UpsertCurrentMmr(Race race, long mmr)
        {
            RaceMmrViewModel? existing = CurrentMmrs.FirstOrDefault(m => m.Race == race);
            if (existing != null)
                CurrentMmrs.Remove(existing);

            RaceMmrViewModel updated = new(race, mmr);
            int index = 0;
            while (index < CurrentMmrs.Count && CurrentMmrs[index].Race < race)
                index++;
            CurrentMmrs.Insert(index, updated);
        }

        private async Task LoadCurrentMmrs(Sc2Profile profile)
        {
            try
            {
                IReadOnlyDictionary<Race, long> byRace = await _ladderService.GetCurrentMmrByRaceAsync(profile, CancellationToken.None);
                Dispatcher.UIThread.Post(() =>
                {
                    // Guard against a slow lookup landing after the user already switched profiles.
                    if (ActiveProfile?.Id != profile.Id)
                        return;

                    CurrentMmrs.Clear();
                    foreach ((Race race, long mmr) in byRace.OrderBy(kv => kv.Key))
                        CurrentMmrs.Add(new RaceMmrViewModel(race, mmr));
                });
            }
            catch (Exception)
            {
                // Best-effort: the header just stays empty if the rating can't be fetched.
            }
        }

        // Don't reload immediately — builds can change many times in a row while editing on the Builds
        // tab. Just remember a reload is owed, and pay for it once when the user actually comes back
        // to the Data tab (see NotifyActivated).
        private void OnBuildsChanged() => _buildTreeCacheDirty = true;

        // Called by DataPage's code-behind when the Data tab becomes visible.
        public void NotifyActivated()
        {
            if (ActiveProfile != null)
                Filters.RefreshProfileOptions(_accountRepository.GetAllProfiles());

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

        // Re-queries the database for every currently-checked profile — the only filter dimension that
        // changes which rows exist in the candidate set at all. Every other filter dimension only needs
        // ApplyFilters (see OtherFiltersChanged), not a fresh database round trip.
        private async Task ReloadGamesFromDatabase()
        {
            List<int> profileIds = Filters.ProfileSlot.Options.Where(o => o.IsChecked).Select(o => o.Value.Id).ToList();
            _loadedGames = profileIds.Count == 0 ? [] : _gameDataRepository.GetGamesForProfiles(profileIds);
            Filters.RefreshMapOptions(_loadedGames.Select(g => g.ReplayData.MapName).Distinct());
            ApplyFilters();
            await Task.CompletedTask;
        }

        private void ApplyFilters()
        {
            GameFilterCriteria criteria = Filters.BuildCriteria();
            Games.Clear();
            foreach (GameData game in _loadedGames.Where(g => GameDataFilter.Matches(g, criteria)).OrderBy(g => g.ReplayData.ReplayTimestamp))
                Games.Add(WrapGame(game));
        }

        private GameDataRowViewModel WrapGame(GameData game) =>
            new GameDataRowViewModel(game, _gameDataRepository, ResolveProfileLabel(game.Sc2ProfileId), GetBuildTree);

        private string ResolveProfileLabel(int sc2ProfileId) =>
            Filters.ProfileSlot.Options.FirstOrDefault(o => o.Value.Id == sc2ProfileId)?.Value.DisplayName ?? sc2ProfileId.ToString();

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
