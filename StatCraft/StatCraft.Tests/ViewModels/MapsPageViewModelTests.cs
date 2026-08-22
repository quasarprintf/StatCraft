using StatCraft.Models.GameData.Attributes;
using StatCraft.Services.DatabaseRepository;
using StatCraft.ViewModels.Windows;
using StatCraft.ViewModels.Windows.DataComponents;

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

    // Pins the fix for a real bug: this page loads its own AttributeDefinition instances, separate from
    // whatever instance the Attributes tab is editing, so a rename/retype made there never touched this
    // page's copy — the value editor kept showing the old type, and the label kept the old name. The fix
    // patches the same instance this page (and every map's AttributeValues, and the bound editors) already
    // holds, in place, rather than replacing it.
    [Fact]
    public void AttributeTypeChangedElsewhere_UpdatesTheSameInstanceThisPageAlreadyHolds()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Rush Distance", Type = AttributeType.Numeric };
        _attributeRepo.InsertAttribute(attribute, 0);
        _mapRepo.InsertMap(new() { Name = "Altitude LE" });
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        AttributeDefinition held = Assert.Single(vm.Attributes);
        Assert.IsType<NumericRangeFilterSlotViewModel>(Assert.Single(vm.HiddenFilterSlots));

        AttributeDefinition editedElsewhere = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Map));
        editedElsewhere.Type = AttributeType.Bool;
        _attributeRepo.UpdateAttribute(editedElsewhere);

        Assert.Equal(AttributeType.Bool, held.Type);
        Assert.Same(held, Assert.Single(vm.Attributes));
        Assert.Same(held, Assert.Single(vm.Maps.Single().AttributeValues).Definition);
        // Numeric/Bool/Values are different FilterSlotViewModel subclasses, so the slot itself must be
        // swapped, not just have a property change underneath it.
        Assert.IsType<BoolFilterSlotViewModel>(Assert.Single(vm.HiddenFilterSlots));
    }

    [Fact]
    public void AttributeNameChangedElsewhere_UpdatesTheSameInstanceThisPageAlreadyHolds()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Old Name" };
        _attributeRepo.InsertAttribute(attribute, 0);
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        AttributeDefinition held = Assert.Single(vm.Attributes);

        AttributeDefinition editedElsewhere = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Map));
        editedElsewhere.Name = "New Name";
        _attributeRepo.UpdateAttribute(editedElsewhere);

        Assert.Equal("New Name", held.Name);
        Assert.Equal("New Name", Assert.Single(vm.HiddenFilterSlots).Title);
    }

    [Fact]
    public void ValueOptionAddedElsewhere_AppearsOnTheSameInstance()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        AttributeDefinition held = Assert.Single(vm.Attributes);

        _attributeRepo.InsertValueOption(attribute.Id, "Rush");

        Assert.Equal(["Rush"], held.ValueOptions);
    }

    // The checkbox filter slot keeps its own separate option list (built once at slot-creation time), so
    // adding an option elsewhere has to patch it too — and preserve whatever the user already checked.
    [Fact]
    public void ValueOptionAddedElsewhere_PatchesTheFilterSlotPreservingWhatWasChecked()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { Name = "Style", Type = AttributeType.Values };
        _attributeRepo.InsertAttribute(attribute, 0);
        _attributeRepo.InsertValueOption(attribute.Id, "Rush");
        MapsPageViewModel vm = new(_mapRepo, _attributeRepo, _gameDataRepo);
        CheckboxFilterSlotViewModel<string> slot = Assert.IsType<CheckboxFilterSlotViewModel<string>>(Assert.Single(vm.HiddenFilterSlots));
        slot.Options.Single(o => o.Value == "Rush").IsChecked = true;

        _attributeRepo.InsertValueOption(attribute.Id, "Macro");

        Assert.Equal(["Rush", "Macro"], slot.Options.Select(o => o.Value));
        Assert.True(slot.Options.Single(o => o.Value == "Rush").IsChecked);
        Assert.False(slot.Options.Single(o => o.Value == "Macro").IsChecked);
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
