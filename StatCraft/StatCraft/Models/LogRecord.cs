using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace StatCraft.Models
{

    public class LogRecord
    {
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
        public required LogLevel Level { get; set; }
        public required string Message { get; set; } = "";
        public List<object> Context = new List<object>();

        public LogRecord AddContext(object context)
        {
            Context.Add(context);
            return this;
        }
    }
}
