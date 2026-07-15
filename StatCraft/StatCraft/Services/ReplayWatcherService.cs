using s2protocol.NET;
using StatCraft.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StatCraft.Services
{
    public class ReplayWatcherService(ILogger logger, ReplayDataExtractor replayDataExtractor) : IAsyncDisposable
    {
        private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(5));
        private readonly HashSet<string> _knownFiles = new();
        private string? _folderPath;
        private Sc2Profile? _profile;
        private CancellationTokenSource? _cts;
        private Task? LoopTask = null;

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

        public async Task CheckNow()
        {
            if (_folderPath == null || !Directory.Exists(_folderPath))
                return;

            foreach (string file in Directory.EnumerateFiles(_folderPath))
            {
                if (_knownFiles.Add(file))
                    await ProcessReplay(file);
            }
        }

        protected virtual async Task ProcessReplay(string filePath)
        {
            if (_profile == null)
                return;
            logger.LogInfo($"Replay file found: {filePath}", _profile);

            using ReplayDecoder decoder = new();
            Sc2Replay? replay = await decoder.DecodeAsync(filePath);
            if (replay == null)
            {
                logger.LogWarning($"Failed to decode replay: {filePath}", _profile);
                return;
            }

            ReplayData replayData = replayDataExtractor.Extract(replay);
            logger.LogInfo($"Replay parsed: {replayData.MapName}", _profile, replayData);
        }

        public async ValueTask DisposeAsync()
        {
            await Stop();
            _timer.Dispose();
        }
    }
}
