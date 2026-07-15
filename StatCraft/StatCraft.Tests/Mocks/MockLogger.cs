using StatCraft.Models;
using StatCraft.Services;
using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Tests.Mocks
{
    internal class MockLogger : ILogger
    {
        public async ValueTask DisposeAsync()
        {
        }

        public void Flush()
        {
        }

        public void Log(LogRecord record)
        {
        }

        public void Log(Microsoft.Extensions.Logging.LogLevel level, string message, params object[] context)
        {
        }

        public void LogError(string message, params object[] context)
        {
        }

        public void LogInfo(string message, params object[] context)
        {
        }

        public void LogWarning(string message, params object[] context)
        {
        }
    }
}
