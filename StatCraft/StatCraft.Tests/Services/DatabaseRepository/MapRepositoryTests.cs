using Microsoft.Data.Sqlite;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Attributes.FixedAttribute;
using StatCraft.Models.GameData.Maps;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.Tests;

public class MapRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly MapRepository _repository;

    public MapRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");
        _repository = new MapRepository(_dbPath);
        _repository.Initialize();
    }

    [Fact]
    public void Initialize_CalledTwice_DoesNotThrow()
    {
        _repository.Initialize();
    }

    [Fact]
    public void GetOrCreateMap_FirstCall_CreatesMap()
    {
        Map? map = _repository.GetOrCreateMap("Altitude LE");

        Assert.NotNull(map);
        Assert.Equal("Altitude LE", map.Name);
        Assert.Single(_repository.GetAllMaps([]));
    }

    [Fact]
    public void GetOrCreateMap_SecondCallSameName_ReturnsExistingRow()
    {
        Map first = _repository.GetOrCreateMap("Altitude LE")!;
        Map second = _repository.GetOrCreateMap("Altitude LE")!;

        Assert.Equal(first.Id, second.Id);
        Assert.Single(_repository.GetAllMaps([]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetOrCreateMap_BlankName_ReturnsNullWithoutCreatingAMap(string name)
    {
        Assert.Null(_repository.GetOrCreateMap(name));
        Assert.Empty(_repository.GetAllMaps([]));
    }

    [Fact]
    public void InsertMap_DuplicateName_Throws()
    {
        _repository.InsertMap(new Map { Name = "Altitude LE" });

        Assert.Throws<SqliteException>(() => _repository.InsertMap(new Map { Name = "Altitude LE" }));
    }

    [Fact]
    public void UpdateMap_ThenReload_PersistsName()
    {
        Map map = new() { Name = "Old" };
        _repository.InsertMap(map);

        map.Name = "New";
        _repository.UpdateMap(map);

        Assert.Equal("New", Assert.Single(_repository.GetAllMaps([])).Name);
    }

    [Fact]
    public void DeleteMap_RemovesIt()
    {
        Map map = new() { Name = "Doomed" };
        _repository.InsertMap(map);

        _repository.DeleteMap(map.Id);

        Assert.Empty(_repository.GetAllMaps([]));
    }

    [Fact]
    public void InsertAttribute_AppliesToEveryMap()
    {
        _repository.InsertMap(new Map { Name = "A" });
        _repository.InsertMap(new Map { Name = "B" });
        _repository.InsertAttribute(new FixedAttribute { Name = "Rush Distance", Type = AttributeType.Numeric }, 0);

        List<FixedAttribute> attributes = _repository.GetAllAttributes();
        foreach (Map map in _repository.GetAllMaps(attributes))
            Assert.Equal("Rush Distance", Assert.Single(map.AttributeValues).Attribute.Name);
    }

    [Fact]
    public void UpdateAttribute_ThenReload_PersistsNameAndType()
    {
        FixedAttribute attribute = new() { Name = "Old", Type = AttributeType.Numeric };
        _repository.InsertAttribute(attribute, 0);

        attribute.Name = "New";
        attribute.Type = AttributeType.Percent;
        _repository.UpdateAttribute(attribute);

        FixedAttribute loaded = Assert.Single(_repository.GetAllAttributes());
        Assert.Equal("New", loaded.Name);
        Assert.Equal(AttributeType.Percent, loaded.Type);
    }

    [Fact]
    public void DeleteAttribute_RemovesItFromEveryMap()
    {
        _repository.InsertMap(new Map { Name = "A" });
        FixedAttribute attribute = new() { Name = "Doomed" };
        _repository.InsertAttribute(attribute, 0);

        _repository.DeleteAttribute(attribute.Id);

        Assert.Empty(_repository.GetAllAttributes());
        Assert.Empty(Assert.Single(_repository.GetAllMaps(_repository.GetAllAttributes())).AttributeValues);
    }

    [Fact]
    public void ValueOptions_RoundTripInSortOrder()
    {
        FixedAttribute attribute = new() { Name = "Style", Type = AttributeType.Values };
        _repository.InsertAttribute(attribute, 0);
        _repository.InsertValueOption(attribute.Id, "Macro", 0);
        _repository.InsertValueOption(attribute.Id, "Rush", 1);

        Assert.Equal(["Macro", "Rush"], Assert.Single(_repository.GetAllAttributes()).ValueOptions);

        _repository.DeleteValueOption(attribute.Id, "Macro");
        Assert.Equal(["Rush"], Assert.Single(_repository.GetAllAttributes()).ValueOptions);
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
        _repository.InsertMap(new Map { Name = "A" });
        _repository.InsertAttribute(new FixedAttribute { Name = "Attr", Type = type }, 0);

        FixedAttributeValue value = Assert.Single(LoadSingleMap().AttributeValues);

        Assert.False(value.HasValue);
        Assert.Null(value.NumericValue);
        Assert.Null(value.BoolValue);
        Assert.Null(value.PercentValue);
        Assert.Null(value.SelectedValue);
    }

    [Fact]
    public void SaveValue_NumericThenReload_RoundTrips()
    {
        (Map map, FixedAttribute attribute) = SeedMapAndAttribute(AttributeType.Numeric);
        _repository.SaveValue(map.Id, attribute.Id, SerializeVia(attribute, v => v.NumericValue = 12.5m));

        FixedAttributeValue value = Assert.Single(LoadSingleMap().AttributeValues);
        Assert.True(value.HasValue);
        Assert.Equal(12.5m, value.NumericValue);
    }

    [Fact]
    public void SaveValue_BoolFalseThenReload_IsSetRatherThanUnset()
    {
        (Map map, FixedAttribute attribute) = SeedMapAndAttribute(AttributeType.Bool);
        _repository.SaveValue(map.Id, attribute.Id, SerializeVia(attribute, v => v.BoolValue = false));

        FixedAttributeValue value = Assert.Single(LoadSingleMap().AttributeValues);
        Assert.True(value.HasValue);
        Assert.False(value.BoolValue);
    }

    [Fact]
    public void SaveValue_Null_DeletesTheRowSoItReadsBackAsUnset()
    {
        (Map map, FixedAttribute attribute) = SeedMapAndAttribute(AttributeType.Numeric);
        _repository.SaveValue(map.Id, attribute.Id, SerializeVia(attribute, v => v.NumericValue = 42m));

        _repository.SaveValue(map.Id, attribute.Id, null);

        FixedAttributeValue value = Assert.Single(LoadSingleMap().AttributeValues);
        Assert.False(value.HasValue);
        Assert.Null(value.NumericValue);
    }

    [Fact]
    public void SaveValue_CalledTwice_UpdatesRatherThanDuplicating()
    {
        (Map map, FixedAttribute attribute) = SeedMapAndAttribute(AttributeType.Numeric);
        _repository.SaveValue(map.Id, attribute.Id, SerializeVia(attribute, v => v.NumericValue = 1m));
        _repository.SaveValue(map.Id, attribute.Id, SerializeVia(attribute, v => v.NumericValue = 2m));

        FixedAttributeValue value = Assert.Single(LoadSingleMap().AttributeValues);
        Assert.Equal(2m, value.NumericValue);
    }

    [Fact]
    public void GetAllMaps_ValueForADeletedAttribute_IsIgnored()
    {
        (Map map, FixedAttribute attribute) = SeedMapAndAttribute(AttributeType.Numeric);
        _repository.SaveValue(map.Id, attribute.Id, SerializeVia(attribute, v => v.NumericValue = 7m));

        // Passing no definitions simulates the attribute having been deleted out from under the row.
        Assert.Empty(Assert.Single(_repository.GetAllMaps([])).AttributeValues);
    }

    private (Map Map, FixedAttribute Attribute) SeedMapAndAttribute(AttributeType type)
    {
        Map map = new() { Name = "A" };
        _repository.InsertMap(map);
        FixedAttribute attribute = new() { Name = "Attr", Type = type };
        _repository.InsertAttribute(attribute, 0);
        return (map, attribute);
    }

    private Map LoadSingleMap() => Assert.Single(_repository.GetAllMaps(_repository.GetAllAttributes()));

    // Goes through MapAttributeValue rather than hand-writing the stored string, so these tests pin the
    // round trip the app actually performs.
    private static string? SerializeVia(FixedAttribute attribute, Action<FixedAttributeValue> set)
    {
        FixedAttributeValue value = new(attribute);
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
