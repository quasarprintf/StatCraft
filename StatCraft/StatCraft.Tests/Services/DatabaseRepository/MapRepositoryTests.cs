using Microsoft.Data.Sqlite;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Maps;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.Tests;

public class MapRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly MapRepository _mapRepo;
    private readonly AttributeRepository _attributeRepo;

    public MapRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");
        _mapRepo = new MapRepository(_dbPath);
        _mapRepo.Initialize();
        _attributeRepo = new AttributeRepository(_dbPath);
        _attributeRepo.Initialize();
    }

    [Fact]
    public void Initialize_CalledTwice_DoesNotThrow()
    {
        _mapRepo.Initialize();
    }

    [Fact]
    public void GetOrCreateMap_FirstCall_CreatesMap()
    {
        Map? map = _mapRepo.GetOrCreateMap("Altitude LE");

        Assert.NotNull(map);
        Assert.Equal("Altitude LE", map.Name);
        Assert.Single(_mapRepo.GetAllMaps([]));
    }

    [Fact]
    public void GetOrCreateMap_SecondCallSameName_ReturnsExistingRow()
    {
        Map first = _mapRepo.GetOrCreateMap("Altitude LE")!;
        Map second = _mapRepo.GetOrCreateMap("Altitude LE")!;

        Assert.Equal(first.Id, second.Id);
        Assert.Single(_mapRepo.GetAllMaps([]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetOrCreateMap_BlankName_ReturnsNullWithoutCreatingAMap(string name)
    {
        Assert.Null(_mapRepo.GetOrCreateMap(name));
        Assert.Empty(_mapRepo.GetAllMaps([]));
    }

    [Fact]
    public void InsertMap_DuplicateName_Throws()
    {
        _mapRepo.InsertMap(new Map { Name = "Altitude LE" });

        Assert.Throws<SqliteException>(() => _mapRepo.InsertMap(new Map { Name = "Altitude LE" }));
    }

    [Fact]
    public void UpdateMap_ThenReload_PersistsName()
    {
        Map map = new() { Name = "Old" };
        _mapRepo.InsertMap(map);

        map.Name = "New";
        _mapRepo.UpdateMap(map);

        Assert.Equal("New", Assert.Single(_mapRepo.GetAllMaps([])).Name);
    }

    [Fact]
    public void DeleteMap_RemovesIt()
    {
        Map map = new() { Name = "Doomed" };
        _mapRepo.InsertMap(map);

        _mapRepo.DeleteMap(map.Id);

        Assert.Empty(_mapRepo.GetAllMaps([]));
    }

    // GetAllMaps takes its attribute definitions as a parameter rather than querying them itself
    // (AttributeRepository owns that), so "an attribute applies to every map" now means every map in the
    // result carries one AttributeValue slot per definition passed in.
    [Fact]
    public void GetAllMaps_GivenAnAttribute_AppliesItToEveryMap()
    {
        _mapRepo.InsertMap(new Map { Name = "A" });
        _mapRepo.InsertMap(new Map { Name = "B" });
        _attributeRepo.InsertAttribute(new AttributeDefinition(AttributeScope.Map) { Name = "Rush Distance", Type = AttributeType.Numeric }, 0);

        List<AttributeDefinition> attributes = _attributeRepo.GetAllAttributes(AttributeScope.Map);
        foreach (Map map in _mapRepo.GetAllMaps(attributes))
            Assert.Equal("Rush Distance", Assert.Single(map.AttributeValues).Definition.Name);
    }

    // Mirrors GetAllMaps_ValueForADeletedAttribute_IsIgnored below, but from the definitions side: once
    // an attribute is deleted, AttributeRepository.GetAllAttributes no longer returns it, so passing that
    // fresh (now-shorter) list means no map carries a slot for it anymore.
    [Fact]
    public void GetAllMaps_AfterAttributeDeleted_NoLongerAppliesToAnyMap()
    {
        _mapRepo.InsertMap(new Map { Name = "A" });
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Doomed" };
        _attributeRepo.InsertAttribute(attribute, 0);

        _attributeRepo.DeleteAttribute(attribute.Id);

        List<AttributeDefinition> attributes = _attributeRepo.GetAllAttributes(AttributeScope.Map);
        Assert.Empty(attributes);
        Assert.Empty(Assert.Single(_mapRepo.GetAllMaps(attributes)).AttributeValues);
    }

    // The reason MapAttributeValue's slots are nullable at all: a value that was never entered must not
    // read back as 0/false, which is what AttributeValueSerializer.Parse would produce for an empty
    // string. Absence of a row is the null, so nothing should be parsed for it.
    [Theory]
    [InlineData(AttributeType.Numeric)]
    [InlineData(AttributeType.Bool)]
    [InlineData(AttributeType.Percent)]
    [InlineData(AttributeType.Values)]
    public void GetAllMaps_AttributeWithNoStoredValue_ReadsBackAsUnset(AttributeType type)
    {
        _mapRepo.InsertMap(new Map { Name = "A" });
        _attributeRepo.InsertAttribute(new AttributeDefinition(AttributeScope.Map) { Name = "Attr", Type = type }, 0);

        AttributeValue value = Assert.Single(LoadSingleMap().AttributeValues);

        Assert.False(value.HasValue);
        Assert.Null(value.NumericValue);
        Assert.Null(value.BoolValue);
        Assert.Null(value.PercentValue);
        Assert.Null(value.SelectedValue);
    }

    [Fact]
    public void SaveValue_NumericThenReload_RoundTrips()
    {
        (Map map, AttributeDefinition attribute) = SeedMapAndAttribute(AttributeType.Numeric);
        _mapRepo.SaveValue(map.Id, attribute.Id, SerializeVia(attribute, v => v.NumericValue = 12.5m));

        AttributeValue value = Assert.Single(LoadSingleMap().AttributeValues);
        Assert.True(value.HasValue);
        Assert.Equal(12.5m, value.NumericValue);
    }

    [Fact]
    public void SaveValue_BoolFalseThenReload_IsSetRatherThanUnset()
    {
        (Map map, AttributeDefinition attribute) = SeedMapAndAttribute(AttributeType.Bool);
        _mapRepo.SaveValue(map.Id, attribute.Id, SerializeVia(attribute, v => v.BoolValue = false));

        AttributeValue value = Assert.Single(LoadSingleMap().AttributeValues);
        Assert.True(value.HasValue);
        Assert.False(value.BoolValue);
    }

    [Fact]
    public void SaveValue_Null_DeletesTheRowSoItReadsBackAsUnset()
    {
        (Map map, AttributeDefinition attribute) = SeedMapAndAttribute(AttributeType.Numeric);
        _mapRepo.SaveValue(map.Id, attribute.Id, SerializeVia(attribute, v => v.NumericValue = 42m));

        _mapRepo.SaveValue(map.Id, attribute.Id, null);

        AttributeValue value = Assert.Single(LoadSingleMap().AttributeValues);
        Assert.False(value.HasValue);
        Assert.Null(value.NumericValue);
    }

    [Fact]
    public void SaveValue_CalledTwice_UpdatesRatherThanDuplicating()
    {
        (Map map, AttributeDefinition attribute) = SeedMapAndAttribute(AttributeType.Numeric);
        _mapRepo.SaveValue(map.Id, attribute.Id, SerializeVia(attribute, v => v.NumericValue = 1m));
        _mapRepo.SaveValue(map.Id, attribute.Id, SerializeVia(attribute, v => v.NumericValue = 2m));

        AttributeValue value = Assert.Single(LoadSingleMap().AttributeValues);
        Assert.Equal(2m, value.NumericValue);
    }

    [Fact]
    public void GetAllMaps_ValueForADeletedAttribute_IsIgnored()
    {
        (Map map, AttributeDefinition attribute) = SeedMapAndAttribute(AttributeType.Numeric);
        _mapRepo.SaveValue(map.Id, attribute.Id, SerializeVia(attribute, v => v.NumericValue = 7m));

        // Passing no definitions simulates the attribute having been deleted out from under the row.
        Assert.Empty(Assert.Single(_mapRepo.GetAllMaps([])).AttributeValues);
    }

    private (Map Map, AttributeDefinition Attribute) SeedMapAndAttribute(AttributeType type)
    {
        Map map = new() { Name = "A" };
        _mapRepo.InsertMap(map);
        AttributeDefinition attribute = new AttributeDefinition(AttributeScope.Map) { Name = "Attr", Type = type };
        _attributeRepo.InsertAttribute(attribute, 0);
        return (map, attribute);
    }

    private Map LoadSingleMap() => Assert.Single(_mapRepo.GetAllMaps(_attributeRepo.GetAllAttributes(AttributeScope.Map)));

    // Goes through MapAttributeValue rather than hand-writing the stored string, so these tests pin the
    // round trip the app actually performs.
    private static string? SerializeVia(AttributeDefinition attribute, Action<AttributeValue> set)
    {
        AttributeValue value = new(attribute);
        set(value);
        return value.Serialize();
    }

    public void Dispose()
    {
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
