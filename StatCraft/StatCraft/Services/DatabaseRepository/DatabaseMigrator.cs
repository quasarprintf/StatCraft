using System;
using System.Reflection;
using DbUp;
using DbUp.Engine;
using DbUp.Engine.Output;
using DbUp.Support;
using StatCraft.Services.BackgroundService;

namespace StatCraft.Services.DatabaseRepository
{
    // Applies every schema script under DatabaseScripts/ to the given SQLite database, tracked in
    // DbUp's own journal table (SchemaVersions) so each RunOnce script only ever executes once, ever,
    // regardless of how many repositories call this. RunGroupOrder controls the two folders' relative
    // order (Table scripts, then RunAlways scripts) — see DatabaseScripts/RunOnce/Table and
    // DatabaseScripts/RunAlways.
    public static class DatabaseMigrator
    {
        public static void Migrate(string dbPath, ILogger? logger = null)
        {
            Assembly assembly = typeof(DatabaseMigrator).Assembly;

            UpgradeEngine upgrader = DeployChanges.To
                .SqliteDatabase($"Data Source={dbPath}")
                .WithScriptsEmbeddedInAssembly(
                    assembly,
                    s => s.Contains(".DatabaseScripts.RunOnce.Table."),
                    new SqlScriptOptions { RunGroupOrder = 1 })
                .WithScriptsEmbeddedInAssembly(
                    assembly,
                    s => s.Contains(".DatabaseScripts.RunAlways."),
                    new SqlScriptOptions { RunGroupOrder = 2, ScriptType = ScriptType.RunAlways })
                .LogTo(new AppLoggerUpgradeLog(logger))
                .Build();

            DatabaseUpgradeResult result = upgrader.PerformUpgrade();
            if (!result.Successful)
                throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        // dbup-core ships no built-in IUpgradeLog that forwards elsewhere (only ConsoleUpgradeLog/
        // TraceUpgradeLog), and .LogToConsole() has nowhere to write in this WinExe app (no attached
        // console) — so DbUp's own progress/error output is routed into the app's existing ILogger
        // instead. logger is null in tests (SqliteRepository's logger parameter is optional), in which
        // case every call below is a harmless no-op.
        private sealed class AppLoggerUpgradeLog(ILogger? logger) : IUpgradeLog
        {
            public void LogTrace(string format, params object[] args) => logger?.Log(Microsoft.Extensions.Logging.LogLevel.Trace, string.Format(format, args));
            public void LogDebug(string format, params object[] args) => logger?.Log(Microsoft.Extensions.Logging.LogLevel.Debug, string.Format(format, args));
            public void LogInformation(string format, params object[] args) => logger?.LogInfo(string.Format(format, args));
            public void LogWarning(string format, params object[] args) => logger?.LogWarning(string.Format(format, args));
            public void LogError(string format, params object[] args) => logger?.LogError(string.Format(format, args));
            public void LogError(Exception ex, string format, params object[] args) => logger?.LogError($"{string.Format(format, args)}: {ex}");
        }
    }
}
