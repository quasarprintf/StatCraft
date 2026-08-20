using System.Reflection;
using Dapper;
using Microsoft.Data.Sqlite;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.Tests;

// Pins the correctness of DatabaseScripts/RunOnce/Table/Maps/001.sql — the one-time migration that
// moves MapAttributes/MapAttributeValueOptions data into the new shared AttributeDefinitions/
// AttributeValueOptions tables and rebuilds MapAttributeValues to reference AttributeDefinitions.
// Executes the actual embedded script (not a hand-duplicated copy) against a hand-seeded database
// shaped like the schema immediately before this migration ran, so there's no DbUp journal to fight —
// DatabaseMigrator.Migrate() itself always runs every script in order, dropping MapAttributes before
// any test could seed data into it.
public class MapAttributesMigrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _conn;

    public MapAttributesMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        _conn = new SqliteConnection($"Data Source={_dbPath}");
        _conn.Open();

        // Pre-migration schema shape — only what Maps/001.sql itself touches or depends on.
        _conn.Execute(@"
            CREATE TABLE AttributeDefinitions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL DEFAULT '', Type INTEGER NOT NULL DEFAULT 0,
                Scope INTEGER NOT NULL DEFAULT 0, DefaultValue TEXT NOT NULL DEFAULT '', SortOrder INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE AttributeValueOptions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, AttributeId INTEGER NOT NULL REFERENCES AttributeDefinitions(Id) ON DELETE CASCADE,
                Value TEXT NOT NULL, SortOrder INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE Maps (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL UNIQUE
            );
            CREATE TABLE MapAttributes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL DEFAULT '', Type INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0, Scope INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE MapAttributeValueOptions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, MapAttributeId INTEGER NOT NULL REFERENCES MapAttributes(Id) ON DELETE CASCADE,
                Value TEXT NOT NULL, SortOrder INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE MapAttributeValues (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, MapId INTEGER NOT NULL REFERENCES Maps(Id) ON DELETE CASCADE,
                MapAttributeId INTEGER NOT NULL REFERENCES MapAttributes(Id) ON DELETE CASCADE, Value TEXT NOT NULL DEFAULT '',
                UNIQUE(MapId, MapAttributeId)
            );");
    }

    [Fact]
    public void RunningTheMigration_MovesDataAndFixesForeignKeys_WithoutRemapping()
    {
        // Hand-picked non-1 IDs, so this can't accidentally pass just because everything already
        // happened to be ID 1.
        _conn.Execute("INSERT INTO MapAttributes (Id, Name, Type, SortOrder, Scope) VALUES (57, 'Rush Distance', 0, 3, 4)");
        _conn.Execute("INSERT INTO MapAttributeValueOptions (Id, MapAttributeId, Value, SortOrder) VALUES (91, 57, 'Close', 0)");
        _conn.Execute("INSERT INTO Maps (Id, Name) VALUES (12, 'Altitude LE')");
        _conn.Execute("INSERT INTO MapAttributeValues (Id, MapId, MapAttributeId, Value) VALUES (200, 12, 57, '4.5')");

        string migrationSql = ReadEmbeddedMigrationScript();
        _conn.Execute(migrationSql);

        // MapAttributes -> AttributeDefinitions, Id preserved.
        dynamic definition = _conn.QuerySingle("SELECT Id, Name, Type, Scope, DefaultValue, SortOrder FROM AttributeDefinitions WHERE Id = 57");
        Assert.Equal("Rush Distance", (string)definition.Name);
        Assert.Equal(0, (long)definition.Type);
        Assert.Equal(4, (long)definition.Scope);
        Assert.Equal(3, (long)definition.SortOrder);

        // MapAttributeValueOptions -> AttributeValueOptions, AttributeId = the preserved definition Id.
        dynamic option = _conn.QuerySingle("SELECT AttributeId, Value FROM AttributeValueOptions WHERE Id = 91");
        Assert.Equal(57L, (long)option.AttributeId);
        Assert.Equal("Close", (string)option.Value);

        // MapAttributeValues rebuilt: same row, same Id, AttributeId column (renamed from
        // MapAttributeId) still numerically equal to the preserved definition Id.
        dynamic value = _conn.QuerySingle("SELECT MapId, AttributeId, Value FROM MapAttributeValues WHERE Id = 200");
        Assert.Equal(12L, (long)value.MapId);
        Assert.Equal(57L, (long)value.AttributeId);
        Assert.Equal("4.5", (string)value.Value);

        // Old tables gone.
        Assert.Empty(_conn.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'MapAttributes'"));
        Assert.Empty(_conn.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'MapAttributeValueOptions'"));
    }

    private static string ReadEmbeddedMigrationScript()
    {
        Assembly assembly = typeof(DatabaseMigrator).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream("StatCraft.DatabaseScripts.RunOnce.Table.Maps.001.sql");
        Assert.NotNull(stream);
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        _conn.Dispose();
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}
