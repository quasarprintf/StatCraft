using StatCraft.Models.GameData.Attributes;
using StatCraft.Services.DatabaseRepository;
using System;
using System.Collections.Generic;
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
}
