using StatCraft.Models.GameData.Attributes;
using StatCraft.Services.DatabaseRepository;
using StatCraft.ViewModels.Windows;

namespace StatCraft.Tests;

public class MapsPageViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly MapRepository _mapRepo;
    private readonly AttributeRepository _attributeRepo;
    private readonly GameDataRepository _gameDataRepo;

    public MapsPageViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");
        _mapRepo = new MapRepository(_dbPath);
        _mapRepo.Initialize();
        _attributeRepo = new AttributeRepository(_dbPath);
        _attributeRepo.Initialize();
        _gameDataRepo = new GameDataRepository(_dbPath);
        _gameDataRepo.Initialize();
    }

    // Pins the fix for a real bug: MapsPageViewModel loaded its attribute list once at construction and
    // never refreshed it, so an attribute added elsewhere (the Attributes tab, sharing the same
    // AttributeRepository) never showed up here without restarting the app.
    [Fact]
    public void AttributeAddedElsewhere_AppearsHereAndOnEveryExistingMap()
    {
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);

        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Rush Distance" };
        _attributeRepo.InsertAttribute(attribute, 0);

        Assert.Contains(vm.Attributes, a => a.Id == attribute.Id);
        Assert.Contains(vm.Maps.Single().AttributeValues, v => v.Definition.Id == attribute.Id);
    }

    [Fact]
    public void AttributeDeletedElsewhere_DisappearsHereAndFromEveryMap()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Doomed" };
        _attributeRepo.InsertAttribute(attribute, 0);
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);

        _attributeRepo.DeleteAttribute(attribute.Id);

        Assert.DoesNotContain(vm.Attributes, a => a.Id == attribute.Id);
        Assert.DoesNotContain(vm.Maps.Single().AttributeValues, v => v.Definition.Id == attribute.Id);
    }

    // A Game- or Build-scoped attribute change is irrelevant to this page; reconciling shouldn't pull it
    // in just because AttributesChanged fired.
    [Fact]
    public void AttributeAddedElsewhereForADifferentScope_IsIgnored()
    {
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);

        _attributeRepo.InsertAttribute(new(AttributeScope.Game) { Name = "Apm" }, 0);

        Assert.Empty(vm.Attributes);
    }

    [Fact]
    public void AddAttribute_StillAppliesToEveryExistingMap()
    {
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);

        vm.AddAttributeCommand.Execute(null);

        AttributeDefinition attribute = Assert.Single(vm.Attributes);
        Assert.Contains(vm.Maps.Single().AttributeValues, v => v.Definition == attribute);
    }

    [Fact]
    public void RemoveAttribute_StillRemovesFromEveryMap()
    {
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        vm.AddAttributeCommand.Execute(null);
        AttributeDefinition attribute = Assert.Single(vm.Attributes);

        vm.RemoveAttributeCommand.Execute(attribute);

        Assert.Empty(vm.Attributes);
        Assert.Empty(vm.Maps.Single().AttributeValues);
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
