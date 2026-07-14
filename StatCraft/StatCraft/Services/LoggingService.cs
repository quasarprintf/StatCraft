using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StatCraft.Models;

namespace StatCraft.Services
{
    public class LoggingService : IAsyncDisposable
    {
        private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(5));
        private readonly ConcurrentQueue<LogRecord> _queue = new();
        private readonly string _logDirectory;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loopTask;
        private bool _disposed;

        public LoggingService(string logDirectory)
        {
            _logDirectory = logDirectory;
            _loopTask = RunLoopAsync(_cts.Token);
        }

        public void Log(LogLevel level, string message, params object[] context)
        {
            LogRecord record = new LogRecord { Timestamp = DateTimeOffset.Now, Level = level, Message = message };
            foreach (object item in context)
                record.AddContext(item);
            _queue.Enqueue(record);
        }

        public void LogInfo(string message, params object[] context) => Log(LogLevel.Information, message, context);
        public void LogWarning(string message, params object[] context) => Log(LogLevel.Warning, message, context);
        public void LogError(string message, params object[] context) => Log(LogLevel.Error, message, context);

        private async Task RunLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (await _timer.WaitForNextTickAsync(cancellationToken))
                    Flush();
            }
            catch (OperationCanceledException)
            {
                // Expected when DisposeAsync() cancels the loop.
            }
        }

        public void Flush()
        {
            if (_queue.IsEmpty)
                return;

            List<string> lines = new();
            while (_queue.TryDequeue(out LogRecord? record))
                lines.Add($"{record.Timestamp:O} - [{record.Level}] - {string.Join(" | ", record.Context)} || {record.Message}");

            Directory.CreateDirectory(_logDirectory);
            string filePath = Path.Combine(_logDirectory, $"log-{DateTimeOffset.Now:yyyyMMdd}.txt");
            File.AppendAllLines(filePath, lines);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;

            _cts.Cancel();
            _cts.Dispose();
            await _loopTask;
            Flush();
            _timer.Dispose();
        }
    }
}
