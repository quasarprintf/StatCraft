using StatCraft.Models.GameData.Attributes;
using StatCraft.Services.DatabaseRepository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.Tests.Services.DatabaseRepository;

public class AttributeRepositoryTests
{
    private readonly string _dbPath;
    private readonly AttributeRepository _attributeRepo;

    public AttributeRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");

        _attributeRepo = new AttributeRepository(_dbPath);
        _attributeRepo.Initialize();
    }

    [Fact]
    public void UpdateAttribute_ThenReload_PersistsNameAndType()
    {
        AttributeDefinition attribute = new AttributeDefinition(AttributeScope.Map) { Name = "Old", Type = AttributeType.Numeric };
        _attributeRepo.InsertAttribute(attribute, 0);

        attribute.Name = "New";
        attribute.Type = AttributeType.Percent;
        _attributeRepo.UpdateAttribute(attribute);

        AttributeDefinition loaded = Assert.Single(_attributeRepo.GetAllAttributes());
        Assert.Equal("New", loaded.Name);
        Assert.Equal(AttributeType.Percent, loaded.Type);
        Assert.Equal(AttributeScope.Map, loaded.Scope);
    }

    [Fact]
    public void ValueOptions_RoundTripInSortOrder()
    {
        AttributeDefinition attribute = new AttributeDefinition(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        _attributeRepo.InsertValueOption(attribute.Id, "Macro", 0);
        _attributeRepo.InsertValueOption(attribute.Id, "Rush", 1);

        Assert.Equal(["Macro", "Rush"], Assert.Single(_attributeRepo.GetAllAttributes()).ValueOptions);

        _attributeRepo.DeleteValueOption(attribute.Id, "Macro");
        Assert.Equal(["Rush"], Assert.Single(_attributeRepo.GetAllAttributes()).ValueOptions);
    }

    [Fact]
    public void InsertAttribute_ThenReload_PersistsNameTypeScopeAndDescription()
    {
        AttributeDefinition attribute = new(AttributeScope.Game) { Name = "Apm", Type = AttributeType.Numeric, Description = "Actions per minute" };
        _attributeRepo.InsertAttribute(attribute, 0);

        AttributeDefinition loaded = Assert.Single(_attributeRepo.GetAllAttributes());
        Assert.Equal("Apm", loaded.Name);
        Assert.Equal(AttributeType.Numeric, loaded.Type);
        Assert.Equal(AttributeScope.Game, loaded.Scope);
        Assert.Equal("Actions per minute", loaded.Description);
    }

    [Fact]
    public void DeleteAttribute_RemovesIt()
    {
        AttributeDefinition attribute = new(AttributeScope.Build) { Name = "Doomed" };
        _attributeRepo.InsertAttribute(attribute, 0);

        _attributeRepo.DeleteAttribute(attribute.Id);

        Assert.Empty(_attributeRepo.GetAllAttributes());
    }

    [Fact]
    public void GetAllAttributes_ScopedCall_OnlyReturnsThatScope()
    {
        _attributeRepo.InsertAttribute(new AttributeDefinition(AttributeScope.Game) { Name = "Game attr" }, 0);
        _attributeRepo.InsertAttribute(new AttributeDefinition(AttributeScope.Map) { Name = "Map attr" }, 0);

        AttributeDefinition loaded = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Game));
        Assert.Equal("Game attr", loaded.Name);
    }

    [Fact]
    public void GetAllAttributes_UnscopedCall_ReturnsEveryScope()
    {
        _attributeRepo.InsertAttribute(new AttributeDefinition(AttributeScope.Game) { Name = "Game attr" }, 0);
        _attributeRepo.InsertAttribute(new AttributeDefinition(AttributeScope.Map) { Name = "Map attr" }, 0);

        Assert.Equal(2, _attributeRepo.GetAllAttributes().Count);
    }

    // Pins the fix for a real bug: the unscoped query used to have no ORDER BY at all, only the scoped
    // one did, so an unscoped read (what AttributesPageViewModel's constructor uses) could come back in
    // whatever order SQLite felt like rather than SortOrder.
    [Fact]
    public void GetAllAttributes_UnscopedCall_OrdersBySortOrder()
    {
        AttributeDefinition first = new(AttributeScope.Game) { Name = "Second" };
        AttributeDefinition second = new(AttributeScope.Game) { Name = "First" };
        // Inserted out of SortOrder order, so a fallback to insertion/rowid order would fail this.
        _attributeRepo.InsertAttribute(first, 1);
        _attributeRepo.InsertAttribute(second, 0);

        Assert.Equal(["First", "Second"], _attributeRepo.GetAllAttributes().Select(a => a.Name));
    }

    [Fact]
    public void GetAllAttributes_ScopedCall_OrdersBySortOrder()
    {
        AttributeDefinition first = new(AttributeScope.Map) { Name = "Second" };
        AttributeDefinition second = new(AttributeScope.Map) { Name = "First" };
        _attributeRepo.InsertAttribute(first, 1);
        _attributeRepo.InsertAttribute(second, 0);

        Assert.Equal(["First", "Second"], _attributeRepo.GetAllAttributes(AttributeScope.Map).Select(a => a.Name));
    }

    // Pins the fix for a real bug: an attribute's default was stored as "" until explicitly set, and
    // reloading used to Parse("") into 0/false rather than leaving it unset.
    [Fact]
    public void InsertAttribute_NoDefaultValueEverSet_ReloadsAsUnset()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Rush Distance", Type = AttributeType.Numeric };
        _attributeRepo.InsertAttribute(attribute, 0);

        AttributeDefinition loaded = Assert.Single(_attributeRepo.GetAllAttributes());
        Assert.False(loaded.DefaultValue.HasValue);
        Assert.Null(loaded.DefaultValue.NumericValue);
    }

    [Fact]
    public void InsertAttribute_WithDefaultValueSet_ReloadsWithIt()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Rush Distance", Type = AttributeType.Numeric };
        attribute.DefaultValue.NumericValue = 4.5m;
        _attributeRepo.InsertAttribute(attribute, 0);

        AttributeDefinition loaded = Assert.Single(_attributeRepo.GetAllAttributes());
        Assert.True(loaded.DefaultValue.HasValue);
        Assert.Equal(4.5m, loaded.DefaultValue.NumericValue);
    }

    [Fact]
    public void UpdateAttribute_ThenReload_PersistsDefaultValueAndDescription()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Attr", Type = AttributeType.Numeric };
        _attributeRepo.InsertAttribute(attribute, 0);

        attribute.DefaultValue.NumericValue = 10m;
        attribute.Description = "New description";
        _attributeRepo.UpdateAttribute(attribute);

        AttributeDefinition loaded = Assert.Single(_attributeRepo.GetAllAttributes());
        Assert.Equal(10m, loaded.DefaultValue.NumericValue);
        Assert.Equal("New description", loaded.Description);
    }

    [Fact]
    public void InsertAttribute_RaisesAttributesChanged()
    {
        int raisedCount = 0;
        _attributeRepo.AttributesChanged += () => raisedCount++;

        _attributeRepo.InsertAttribute(new AttributeDefinition(AttributeScope.Map) { Name = "Attr" }, 0);

        Assert.Equal(1, raisedCount);
    }

    [Fact]
    public void DeleteAttribute_RaisesAttributesChanged()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Attr" };
        _attributeRepo.InsertAttribute(attribute, 0);

        int raisedCount = 0;
        _attributeRepo.AttributesChanged += () => raisedCount++;
        _attributeRepo.DeleteAttribute(attribute.Id);

        Assert.Equal(1, raisedCount);
    }
}
