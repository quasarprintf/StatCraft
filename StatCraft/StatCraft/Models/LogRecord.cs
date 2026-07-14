using System;

namespace StatCraft.Models
{
    public enum LogLevel { Debug, Info, Warning, Error }

    public class LogRecord
    {
        public DateTimeOffset Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = "";
    }
}
