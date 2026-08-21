using StatCraft.Models.GameData.Attributes;
using StatCraft.Services.DatabaseRepository;
using StatCraft.ViewModels.Windows;

namespace StatCraft.Tests;

public class AttributesPageViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly AttributeRepository _attributeRepo;

    public AttributesPageViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "StatCraftTests", Guid.NewGuid() + ".db");
        _attributeRepo = new AttributeRepository(_dbPath);
        _attributeRepo.Initialize();
    }

    [Fact]
    public void Constructor_LoadsAttributesGroupedByScope()
    {
        _attributeRepo.InsertAttribute(new AttributeDefinition(AttributeScope.Game) { Name = "Game attr" }, 0);
        _attributeRepo.InsertAttribute(new AttributeDefinition(AttributeScope.Map) { Name = "Map attr" }, 0);

        AttributesPageViewModel vm = new(_attributeRepo);

        vm.SetScope(AttributeScope.Game);
        Assert.Equal(["Game attr"], vm.Attributes.Select(a => a.Name));

        vm.SetScope(AttributeScope.Map);
        Assert.Equal(["Map attr"], vm.Attributes.Select(a => a.Name));
    }

    [Fact]
    public void NameFilter_OnlyShowsMatchingAttributes()
    {
        AttributesPageViewModel vm = new(_attributeRepo);
        vm.SetScope(AttributeScope.Map);
        vm.AddAttributeCommand.Execute(null);
        vm.SelectedAttribute!.Name = "Rush Distance";
        vm.AddAttributeCommand.Execute(null);
        vm.SelectedAttribute!.Name = "Base Count";

        vm.NameFilter = "rush";

        Assert.Equal(["Rush Distance"], vm.FilteredAttributes.Select(a => a.Name));
    }

    [Fact]
    public void AddAttribute_PersistsANewRow()
    {
        AttributesPageViewModel vm = new(_attributeRepo);
        vm.SetScope(AttributeScope.Build);

        vm.AddAttributeCommand.Execute(null);

        AttributeDefinition loaded = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Build));
        Assert.Equal("New Attribute", loaded.Name);
        Assert.Equal(loaded.Id, vm.SelectedAttribute!.Id);
    }

    [Fact]
    public void DeleteAttribute_PersistsTheDeletion()
    {
        AttributesPageViewModel vm = new(_attributeRepo);
        vm.SetScope(AttributeScope.Map);
        vm.AddAttributeCommand.Execute(null);
        AttributeDefinition attribute = vm.SelectedAttribute!;

        vm.DeleteAttributeCommand.Execute(attribute);

        Assert.Empty(_attributeRepo.GetAllAttributes(AttributeScope.Map));
    }

    [Fact]
    public void DeleteAttribute_TheSelectedOne_SelectsAnotherRemainingAttribute()
    {
        AttributesPageViewModel vm = new(_attributeRepo);
        vm.SetScope(AttributeScope.Map);
        vm.AddAttributeCommand.Execute(null);
        AttributeDefinition first = vm.SelectedAttribute!;
        vm.AddAttributeCommand.Execute(null);
        AttributeDefinition second = vm.SelectedAttribute!;

        vm.DeleteAttributeCommand.Execute(second);

        Assert.Equal(first, vm.SelectedAttribute);
        Assert.DoesNotContain(second, vm.FilteredAttributes);
    }

    [Fact]
    public void EditingSelectedAttributeName_Persists()
    {
        AttributesPageViewModel vm = new(_attributeRepo);
        vm.SetScope(AttributeScope.Map);
        vm.AddAttributeCommand.Execute(null);

        vm.SelectedAttribute!.Name = "Renamed";

        AttributeDefinition loaded = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Map));
        Assert.Equal("Renamed", loaded.Name);
    }

    [Fact]
    public void EditingSelectedAttributeDefaultValue_Persists()
    {
        AttributesPageViewModel vm = new(_attributeRepo);
        vm.SetScope(AttributeScope.Map);
        vm.AddAttributeCommand.Execute(null);

        vm.SelectedAttributeValue!.NumericValue = 7.5m;

        AttributeDefinition loaded = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Map));
        Assert.Equal(7.5m, loaded.DefaultValue.NumericValue);
    }

    [Fact]
    public void AddingAValueOption_PersistsIt()
    {
        AttributesPageViewModel vm = new(_attributeRepo);
        vm.SetScope(AttributeScope.Map);
        vm.AddAttributeCommand.Execute(null);
        vm.SelectedAttribute!.Type = AttributeType.Values;

        vm.SelectedAttribute.NewOptionText = "Zealot";
        vm.SelectedAttribute.AddOptionCommand.Execute(null);

        AttributeDefinition loaded = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Map));
        Assert.Equal(["Zealot"], loaded.ValueOptions);
    }

    // Pins the fix for a real bug: typing into the "add option" textbox used to issue an UPDATE on every
    // keystroke, since it was wired to the same PropertyChanged event as real, persistable edits.
    [Fact]
    public void EditingNewOptionText_DoesNotPersist()
    {
        AttributesPageViewModel vm = new(_attributeRepo);
        vm.SetScope(AttributeScope.Map);
        vm.AddAttributeCommand.Execute(null);

        int changeCount = 0;
        _attributeRepo.AttributesChanged += () => changeCount++;

        vm.SelectedAttribute!.NewOptionText = "typing...";

        Assert.Equal(0, changeCount);
    }

    // Pins the fix for a real bug: AddAttribute used to wire the new attribute's events itself AND rely
    // on selecting it to wire them again, so every subsequent edit issued two UPDATEs instead of one.
    [Fact]
    public void AddAttribute_ThenRenameOnce_UpdatesExactlyOnce()
    {
        AttributesPageViewModel vm = new(_attributeRepo);
        vm.SetScope(AttributeScope.Map);

        int changeCount = 0;
        _attributeRepo.AttributesChanged += () => changeCount++;

        vm.AddAttributeCommand.Execute(null);
        Assert.Equal(1, changeCount); // the insert itself

        vm.SelectedAttribute!.Name = "Renamed";

        Assert.Equal(2, changeCount); // exactly one more, not two, for the rename
    }

    // Same double-subscription risk, for the ValueOptions collection specifically.
    [Fact]
    public void AddAttribute_ThenAddOneValueOption_InsertsExactlyOnce()
    {
        AttributesPageViewModel vm = new(_attributeRepo);
        vm.SetScope(AttributeScope.Map);
        vm.AddAttributeCommand.Execute(null);
        vm.SelectedAttribute!.Type = AttributeType.Values;
        vm.SelectedAttribute.NewOptionText = "Zealot";

        int changeCount = 0;
        _attributeRepo.AttributesChanged += () => changeCount++;

        vm.SelectedAttribute.AddOptionCommand.Execute(null);

        Assert.Equal(1, changeCount);
        AttributeDefinition loaded = Assert.Single(_attributeRepo.GetAllAttributes(AttributeScope.Map));
        Assert.Equal(["Zealot"], loaded.ValueOptions);
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
