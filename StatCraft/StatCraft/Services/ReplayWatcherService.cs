using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StatCraft.Services
{
    public class ReplayWatcherService : IAsyncDisposable
    {
        private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(5));
        private readonly HashSet<string> _knownFiles = new();
        private string? _folderPath;
        private CancellationTokenSource? _cts;
        private Task? LoopTask = null;

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
            LoopTask = RunLoopAsync(_cts.Token);
        }

        public async Task Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _knownFiles.Clear();
            _folderPath = null;
            if (LoopTask != null)
                await LoopTask;
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
                    ProcessReplay(file);
            }
        }

        protected virtual void ProcessReplay(string filePath)
        {
            // TODO: parse and record the new replay.
        }

        public async ValueTask DisposeAsync()
        {
            await Stop();
            _timer.Dispose();
        }
    }
}
