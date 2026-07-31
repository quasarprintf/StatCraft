using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StatCraft.Services.BackgroundService
{
    // Solely responsible for noticing when a new file appears in a folder — decoding, parsing, and
    // persisting a replay is ReplayImportService's job, not this class's.
    public class ReplayWatcherService(ILogger logger) : IAsyncDisposable
    {
        private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(5));
        private readonly HashSet<string> _knownFiles = new();
        private string? _folderPath;
        private CancellationTokenSource? _cts;
        private Task? _loopTask;

        // Raised for each new file found in the watched folder. Callers decide what "new" means for
        // their own purposes (e.g. whether it's actually a valid replay) — this event just reports files.
        internal event Action<string>? NewReplayFileFound;

        // The folder currently being watched (null when not watching). Exposed so a manual "import a
        // replay" UI can default its file picker here and validate the user's selection against it.
        public string? WatchedFolderPath => _folderPath;

        public async Task Start(string folderPath)
        {
            await Stop();

            _folderPath = folderPath;
            if (Directory.Exists(folderPath))
            {
                foreach (string file in Directory.EnumerateFiles(folderPath))
                    _knownFiles.Add(file);
            }

            _cts = new CancellationTokenSource();
            _loopTask = RunLoopAsync(_cts.Token);
        }

        public async Task Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _knownFiles.Clear();
            _folderPath = null;
            if (_loopTask != null)
                await _loopTask;
        }

        private async Task RunLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (await _timer.WaitForNextTickAsync(cancellationToken))
                    CheckNow();
            }
            catch (OperationCanceledException)
            {
                // Expected when Stop() cancels the loop.
            }
        }

        public void CheckNow()
        {
            if (_folderPath == null || !Directory.Exists(_folderPath))
                return;

            foreach (string file in Directory.EnumerateFiles(_folderPath))
            {
                if (_knownFiles.Add(file))
                {
                    logger.LogInfo($"Replay file found: {file}");
                    NewReplayFileFound?.Invoke(file);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Stop();
            _timer.Dispose();
        }
    }
}
