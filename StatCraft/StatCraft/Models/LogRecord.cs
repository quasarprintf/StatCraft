using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace StatCraft.Models
{

    public class LogRecord
    {
        public DateTimeOffset Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = "";
        public List<object> Context = new List<object>();

        public LogRecord AddContext(object context)
        {
            Context.Add(context);
            return this;
        }
    }
}
