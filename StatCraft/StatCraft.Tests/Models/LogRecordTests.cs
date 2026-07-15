using StatCraft.Models;

namespace StatCraft.Tests;

public class LogRecordTests
{
    [Fact]
    public void AddContext_ReturnsSameInstance_ForChaining()
    {
        LogRecord record = new LogRecord()
        {
            Level = Microsoft.Extensions.Logging.LogLevel.Information,
            Message = ""
        };

        LogRecord result = record.AddContext("first");

        Assert.Same(record, result);
    }

    [Fact]
    public void AddContext_CalledMultipleTimes_AccumulatesInOrder()
    {
        LogRecord record = new LogRecord()
        {
            Level = Microsoft.Extensions.Logging.LogLevel.Information,
            Message = ""
        };

        record.AddContext("first").AddContext(42).AddContext("third");

        Assert.Equal(["first", 42, "third"], record.Context);
    }

    [Fact]
    public void Context_DefaultsToEmpty()
    {
        LogRecord record = new LogRecord()
        {
            Level = Microsoft.Extensions.Logging.LogLevel.Information,
            Message = ""
        };

        Assert.Empty(record.Context);
    }
}
