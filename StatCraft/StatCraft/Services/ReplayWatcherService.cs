using System;
using System.Collections.Generic;
using System.IO;
using System.Timers;

namespace StatCraft.Services
{
    public class ReplayWatcherService : IDisposable
    {
        private readonly Timer _timer;
        private readonly HashSet<string> _knownFiles = new();
        private string? _folderPath;

        public ReplayWatcherService()
        {
            _timer = new Timer(TimeSpan.FromSeconds(5));
            _timer.Elapsed += (_, _) => CheckNow();
            _timer.AutoReset = true;
        }

        public void Start(string folderPath)
        {
            Stop();

            _folderPath = folderPath;
            if (Directory.Exists(folderPath))
            {
                foreach (string file in Directory.EnumerateFiles(folderPath))
                    _knownFiles.Add(file);
            }

            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            _knownFiles.Clear();
            _folderPath = null;
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

        public void Dispose() => _timer.Dispose();
    }
}
