using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StatCraft.Services.BackgroundService
{
    public class ReplayWatcherService(ILogger logger) : IAsyncDisposable
    {
        private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(5));
        private readonly HashSet<string> _knownFiles = new();
        public string? WatchedFolderPath { get; private set; }
        private CancellationTokenSource? _cts;
        private Task? _loopTask;

        internal event Action<string>? NewReplayFileFound;

        public async Task Start(string folderPath)
        {
            await Stop();

            WatchedFolderPath = folderPath;
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
            WatchedFolderPath = null;
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
            if (WatchedFolderPath == null || !Directory.Exists(WatchedFolderPath))
                return;

            foreach (string file in Directory.EnumerateFiles(WatchedFolderPath))
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
