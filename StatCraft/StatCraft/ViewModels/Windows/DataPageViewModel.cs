using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
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
using StatCraft.Models.Analytics;

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
        private readonly SessionMmrTracker _mmrTracker;
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
            _mmrTracker = new SessionMmrTracker(ladderService);
            _replayWatcherService.NewReplayFileFound += OnNewReplayFileFound;
            _replayImportService.GameParsed += OnGameParsed;
            _replayImportService.GameMmrUpdated += OnGameMmrUpdated;
            _buildRepository.BuildsChanged += OnBuildsChanged;
            _settingsRepository.SettingsChanged += OnSettingsChanged;

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

        // The active session profile's current ladder rating, one entry per race it has placed in — see
        // SessionMmrTracker for how it's populated and kept current.
        public ObservableCollection<RaceMmrViewModel> CurrentMmrs => _mmrTracker.CurrentMmrs;

        // Win/loss over exactly the games the table is currently showing, aggregated across every ladder
        // race and game mode. Recomputed by ApplyFilters, so narrowing to a matchup or map reports the
        // rate for that matchup or map. Blank when nothing is in view.
        [ObservableProperty] private string _winRateLabel = "";

        public event Action? SessionRequested;

        [RelayCommand]
        private void BeginSession() => SessionRequested?.Invoke();

        // Clears the active session back to "no active session" — stops the replay watcher and empties
        // the header/table — without deleting anything already recorded. Begin Session can still be used
        // afterward to start a fresh one, for this profile or another.
        [RelayCommand]
        private async Task EndSession() => await SetActiveProfile(null);

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

        // Raised when the replay file can't be handed off to the OS (moved/deleted since import, or no
        // program associated with .SC2Replay), so the view can surface it instead of the app just
        // silently doing nothing.
        public event Action<string>? LaunchReplayFailed;

        [RelayCommand]
        private void LaunchReplay(GameDataRowViewModel row)
        {
            try
            {
                Process.Start(new ProcessStartInfo(row.ReplayPath) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
            {
                LaunchReplayFailed?.Invoke($"Could not open replay file:\n{row.ReplayPath}\n\n{ex.Message}");
            }
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
                WinRateLabel = "";
                _mmrTracker.Reset();
                await _replayWatcherService.Stop();
                return;
            }

            // Not awaited: a network round-trip shouldn't hold up the session starting or the games
            // table appearing. The header simply fills in whenever the lookup lands, which is also when
            // the session's MMR baseline gets captured.
            _mmrTracker.Reset();
            _ = LoadCurrentMmrs(profile);

            // Every session start collapses the profile filter back to just this profile and the date
            // range back to today, per spec, even if the user had broadened either beforehand.
            Filters.RefreshProfileOptions(_accountRepository.GetAllProfiles());
            Filters.SetSingleActiveProfile(profile);
            await ReloadGamesFromDatabase();

            await StartWatcherFor(profile);
        }

        // Reads the base folder fresh from disk (rather than caching it) so a change made on the Settings
        // tab takes effect on the very next call — both the normal one at session start and the one
        // OnSettingsChanged makes to redirect an already-running watcher mid-session.
        private async Task StartWatcherFor(Sc2Profile profile)
        {
            string baseReplayFolderPath = _settingsRepository.Load().BaseReplayFolderPath ?? "";
            string replayFolderPath = Path.Combine(baseReplayFolderPath, profile.ReplayFolderPathSuffix);
            await _replayWatcherService.Start(replayFolderPath);
        }

        // The base folder can be changed from the Settings tab at any time, including mid-session. A
        // session with no active profile has no watcher to redirect, so there's nothing to do until the
        // next Begin Session — which reads the setting fresh via StartWatcherFor anyway.
        private async void OnSettingsChanged()
        {
            if (ActiveProfile != null)
                await StartWatcherFor(ActiveProfile);
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
            SeedSessionBaselineFrom(game);
            ApplyFilters();
        });

        // The replay records the rating going *into* the game, which is precisely where that ladder stood
        // when the session's first game on it began — so it's a correct baseline for SessionMmrTracker to
        // seed if the session-start lookup didn't already cover this race (see SeedBaselineIfAbsent).
        private void SeedSessionBaselineFrom(GameData game)
        {
            ParsedReplayData replay = game.ReplayData;
            if (!replay.IsRatedOneVsOne)
                return;

            if (LadderRaceExtensions.FromPlayer(replay.Player.Race, replay.Player.Random) is { } race)
                _mmrTracker.SeedBaselineIfAbsent(race, replay.Player.Mmr);
        }

        // Arrives minutes after the game was imported, on a background polling task. The row's underlying
        // GamePlayer has already been mutated in place, so this only needs to prompt the row to
        // re-announce its derived MMR properties — no reload or re-filter.
        private void OnGameMmrUpdated(GameData game) => Dispatcher.UIThread.Post(() =>
        {
            Games.FirstOrDefault(row => row.GameId == game.GameId)?.RefreshMmrChange();

            // The freshly-observed rating is by definition this profile's current one for the ladder just
            // played, so the header can be updated straight from it rather than re-querying the API.
            GamePlayer self = game.ReplayData.Player;
            if (self.MmrAfter is { } mmrAfter && LadderRaceExtensions.FromPlayer(self.Race, self.Random) is { } race)
                _mmrTracker.UpdateCurrent(race, mmrAfter);
        });

        private async Task LoadCurrentMmrs(Sc2Profile profile)
        {
            IReadOnlyDictionary<LadderRace, long>? byRace = await _mmrTracker.FetchCurrentMmrs(profile, CancellationToken.None);
            if (byRace == null)
                return; // Best-effort: the header just stays empty if the rating can't be fetched.

            Dispatcher.UIThread.Post(() =>
            {
                // Guard against a slow lookup landing after the user already switched profiles.
                if (ActiveProfile?.Id != profile.Id)
                    return;

                _mmrTracker.SetBaseline(byRace);
            });
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
            Filters.RefreshMapOptions(_loadedGames.Where(g => g.Map != null).Select(g => g.Map!).Distinct());
            ApplyFilters();
            await Task.CompletedTask;
        }

        private void ApplyFilters()
        {
            GameFilterCriteria criteria = Filters.BuildCriteria();
            List<GameData> matching = _loadedGames
                .Where(g => GameDataFilter.Matches(g, criteria))
                .OrderBy(g => g.ReplayData.ReplayTimestamp)
                .ToList();

            Games.Clear();
            foreach (GameData game in matching)
                Games.Add(WrapGame(game));

            // Derived from the same set the table shows, so the two can never disagree.
            WinRateLabel = WinLossRecord.From(matching.Select(g => GameOutcomeExtensions.FromWin(g.ReplayData.Win))).Label;
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
