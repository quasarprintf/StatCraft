using Microsoft.Extensions.Logging;
using System;

namespace StatCraft.Models
{

    public class LogRecord
    {
        public DateTimeOffset Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = "";
    }
}
