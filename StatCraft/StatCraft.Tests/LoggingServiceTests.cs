using StatCraft.Models;
using StatCraft.Services;

namespace StatCraft.Tests;

public class LoggingServiceTests : IAsyncDisposable
{
    private readonly string _logDirectory;
    private readonly LoggingService _logger;

    public LoggingServiceTests()
    {
        _logDirectory = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid().ToString());
        _logger = new LoggingService(_logDirectory);
    }

    private string LogFilePath => Path.Combine(_logDirectory, $"log-{DateTimeOffset.Now:yyyyMMdd}.txt");

    [Fact]
    public void Flush_NoRecordsQueued_DoesNotCreateFile()
    {
        _logger.Flush();

        Assert.False(File.Exists(LogFilePath));
    }

    [Fact]
    public void Log_ThenFlush_WritesRecordToFile()
    {
        _logger.Log(LogLevel.Info, "hello world");
        _logger.Flush();

        string content = File.ReadAllText(LogFilePath);
        Assert.Contains("[Info]", content);
        Assert.Contains("hello world", content);
    }

    [Fact]
    public void Flush_MultipleQueuedRecords_WritesAllAndDrainsQueue()
    {
        _logger.LogInfo("first");
        _logger.LogWarning("second");
        _logger.LogError("third");
        _logger.Flush();

        string[] lines = File.ReadAllLines(LogFilePath);
        Assert.Equal(3, lines.Length);
        Assert.Contains(lines, l => l.Contains("[Info]") && l.Contains("first"));
        Assert.Contains(lines, l => l.Contains("[Warning]") && l.Contains("second"));
        Assert.Contains(lines, l => l.Contains("[Error]") && l.Contains("third"));

        _logger.Flush();
        string[] linesAfterSecondFlush = File.ReadAllLines(LogFilePath);
        Assert.Equal(3, linesAfterSecondFlush.Length);
    }

    [Fact]
    public void Log_AppendsAcrossMultipleFlushes()
    {
        _logger.LogInfo("first");
        _logger.Flush();

        _logger.LogInfo("second");
        _logger.Flush();

        string[] lines = File.ReadAllLines(LogFilePath);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task DisposeAsync_FlushesRemainingQueuedRecords()
    {
        _logger.LogInfo("pending at shutdown");
        await _logger.DisposeAsync();

        string content = File.ReadAllText(LogFilePath);
        Assert.Contains("pending at shutdown", content);
    }

    public async ValueTask DisposeAsync()
    {
        await _logger.DisposeAsync();
        try
        {
            if (Directory.Exists(_logDirectory))
                Directory.Delete(_logDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
