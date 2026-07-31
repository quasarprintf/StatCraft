using s2protocol.NET;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StatCraft.Services.BackgroundService
{
    public class ReplayWatcherService(ILogger logger, ReplayDataExtractor replayDataExtractor, GameDataRepository gameDataRepository) : IAsyncDisposable
    {
        private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(5));
        private readonly HashSet<string> _knownFiles = new();
        private string? _folderPath;
        private Sc2Profile? _profile;
        private CancellationTokenSource? _cts;
        private Task? LoopTask = null;

        // Raised after a replay is parsed and persisted, so the UI can add a row for it.
        // Internal because GameData is internal; the class itself stays public.
        internal event Action<GameData>? GameParsed;

        // The folder currently being watched (null when no session is active). Exposed so a manual
        // "import a replay" UI can default its file picker here and validate the user's selection
        // against it.
        public string? WatchedFolderPath => _folderPath;

        public async Task Start(string folderPath, Sc2Profile profile)
        {
            await Stop();

            _folderPath = folderPath;
            _profile = profile;
            if (Directory.Exists(folderPath))
            {
                foreach (string file in Directory.EnumerateFiles(folderPath))
                    _knownFiles.Add(file);
            }

            _cts = new CancellationTokenSource();
            LoopTask = RunLoopAsync(_cts.Token);
        }

        public async Task Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _knownFiles.Clear();
            _folderPath = null;
            _profile = null;
            if (LoopTask != null)
                await LoopTask;
        }

        private async Task RunLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (await _timer.WaitForNextTickAsync(cancellationToken))
                    await CheckNow();
            }
            catch (OperationCanceledException)
            {
                // Expected when Stop() cancels the loop.
            }
        }

        // Manually import a single replay file (e.g. one from before watching started), reusing the same
        // decode/parse/insert/GameParsed pipeline as the folder watcher. InsertGame already dedupes by
        // ReplayPath, so importing an already-recorded replay is a harmless no-op rather than a
        // duplicate. Callers are expected to have already validated filePath is within WatchedFolderPath.
        // Returns null on success, or a user-facing message describing why the replay was rejected.
        public Task<string?> ImportReplay(string filePath) => ProcessReplay(filePath);

        public async Task CheckNow()
        {
            if (_folderPath == null || !Directory.Exists(_folderPath))
                return;

            foreach (string file in Directory.EnumerateFiles(_folderPath))
            {
                // Failures are already logged inside ProcessReplay; the folder watcher runs unattended in
                // the background, so there's no active user action to surface an error message to here.
                if (_knownFiles.Add(file))
                    await ProcessReplay(file);
            }
        }

        // Returns null on success, or a user-facing message describing why the replay was rejected.
        protected virtual async Task<string?> ProcessReplay(string filePath)
        {
            if (_profile == null)
                return "No active session.";
            logger.LogInfo($"Replay file found: {filePath}", _profile);

            using ReplayDecoder decoder = new();
            Sc2Replay? replay = await decoder.DecodeAsync(filePath);
            if (replay == null)
            {
                logger.LogWarning($"Failed to decode replay: {filePath}", _profile);
                return $"Could not read \"{Path.GetFileName(filePath)}\" — it doesn't look like a valid StarCraft II replay.";
            }

            DateTimeOffset replayTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath));
            RawReplayData rawReplayData = replayDataExtractor.Extract(replay, replayTimestamp);
            logger.LogInfo($"Replay parsed: {filePath}", _profile, rawReplayData);

            ParsedReplayData parsedReplayData;
            try
            {
                parsedReplayData = replayDataExtractor.Parse(rawReplayData, _profile);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning($"Could not match active profile in replay, skipping: {filePath} ({ex.Message})", _profile);
                return $"\"{Path.GetFileName(filePath)}\" doesn't contain a match for the active profile.";
            }

            GameData game = new() { ReplayData = parsedReplayData };
            gameDataRepository.InsertGame(game, _profile.Id);
            GameParsed?.Invoke(game);
            return null;
        }

        public async ValueTask DisposeAsync()
        {
            await Stop();
            _timer.Dispose();
        }
    }
}
