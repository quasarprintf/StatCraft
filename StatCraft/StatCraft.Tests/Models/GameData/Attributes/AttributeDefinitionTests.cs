using System.ComponentModel;
using StatCraft.Models.GameData.Attributes;

namespace StatCraft.Tests;

public class AttributeDefinitionTests
{
    // Pins the fix for a real bug: every freshly-created attribute is stored with DefaultValue = "" until
    // someone sets one, and that empty string must read back as "no default", not as 0/false — see
    // AttributeValueSerializer's own Parse tests for the lower-level version of this.
    [Theory]
    [InlineData(AttributeType.Numeric)]
    [InlineData(AttributeType.Bool)]
    [InlineData(AttributeType.Percent)]
    public void Constructor_EmptyRawDefaultValue_DefaultValueIsUnset(AttributeType type)
    {
        AttributeDefinition attribute = new(AttributeScope.Map, type, "");

        Assert.False(attribute.DefaultValue.HasValue);
        Assert.Null(attribute.DefaultValue.NumericValue);
        Assert.Null(attribute.DefaultValue.BoolValue);
        Assert.Null(attribute.DefaultValue.PercentValue);
    }

    [Fact]
    public void Constructor_NonEmptyRawDefaultValue_DefaultValueIsSet()
    {
        AttributeDefinition attribute = new(AttributeScope.Map, AttributeType.Numeric, "5");

        Assert.True(attribute.DefaultValue.HasValue);
        Assert.Equal(5m, attribute.DefaultValue.NumericValue);
    }

    // DefinitionChanged drives AttributesPageViewModel's persistence — NewOptionText is a UI-only staging
    // field for the "add option" textbox and was never meant to trigger a save (that was a real bug: every
    // keystroke there issued an UPDATE). Confirming both directions here pins that behavior.
    [Fact]
    public void DefinitionChanged_NewOptionTextEdited_DoesNotFire()
    {
        AttributeDefinition attribute = new(AttributeScope.Map);
        int fireCount = 0;
        attribute.DefinitionChanged += (_, _) => fireCount++;

        attribute.NewOptionText = "Zealot";

        Assert.Equal(0, fireCount);
    }

    [Theory]
    [InlineData(nameof(AttributeDefinition.Name))]
    [InlineData(nameof(AttributeDefinition.Type))]
    [InlineData(nameof(AttributeDefinition.Description))]
    public void DefinitionChanged_OtherPropertyEdited_Fires(string propertyName)
    {
        AttributeDefinition attribute = new(AttributeScope.Map);
        List<string?> fired = [];
        attribute.DefinitionChanged += (_, e) => fired.Add(e.PropertyName);

        switch (propertyName)
        {
            case nameof(AttributeDefinition.Name): attribute.Name = "New"; break;
            case nameof(AttributeDefinition.Type): attribute.Type = AttributeType.Bool; break;
            case nameof(AttributeDefinition.Description): attribute.Description = "New description"; break;
        }

        Assert.Contains(propertyName, fired);
    }

    [Fact]
    public void AddOption_RaisesValueOptionsChangedWithTheAddedValue()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { NewOptionText = "Zealot" };
        CollectionChangeEventArgs? received = null;
        attribute.ValueOptionsChanged += (_, e) => received = e;

        attribute.AddOptionCommand.Execute(null);

        Assert.NotNull(received);
        Assert.Equal(CollectionChangeAction.Add, received!.Action);
        Assert.Equal("Zealot", received.Element);
        Assert.Equal(["Zealot"], attribute.ValueOptions);
    }

    [Fact]
    public void AddOption_BlankNewOptionText_DoesNothing()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { NewOptionText = "   " };
        bool raised = false;
        attribute.ValueOptionsChanged += (_, _) => raised = true;

        attribute.AddOptionCommand.Execute(null);

        Assert.False(raised);
        Assert.Empty(attribute.ValueOptions);
    }

    // Pins the point of the IsNullable/AddOption pairing: a non-nullable Values attribute has nothing
    // concrete to fall back to until it has at least one option, so the very first one added becomes the
    // default automatically rather than leaving DefaultValue.SelectedValue unset.
    [Fact]
    public void AddOption_NotNullable_FirstOption_BecomesTheDefaultValue()
    {
        AttributeDefinition attribute = new(AttributeScope.BuildDetail) { IsNullable = false, NewOptionText = "Zealot" };

        attribute.AddOptionCommand.Execute(null);

        Assert.Equal("Zealot", attribute.DefaultValue.SelectedValue);
    }

    // Only the first option should be auto-picked — once a default exists (even implicitly, from the
    // first option), adding more options must not silently override whatever the user has selected.
    [Fact]
    public void AddOption_NotNullable_SecondOption_DoesNotOverrideTheDefaultValue()
    {
        AttributeDefinition attribute = new(AttributeScope.BuildDetail) { IsNullable = false, NewOptionText = "Zealot" };
        attribute.AddOptionCommand.Execute(null);
        attribute.DefaultValue.SelectedValue = "Zealot"; // simulates whatever the user picked

        attribute.NewOptionText = "Stalker";
        attribute.AddOptionCommand.Execute(null);

        Assert.Equal("Zealot", attribute.DefaultValue.SelectedValue);
    }

    [Fact]
    public void AddOption_Nullable_FirstOption_DoesNotSetTheDefaultValue()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { IsNullable = true, NewOptionText = "Zealot" };

        attribute.AddOptionCommand.Execute(null);

        Assert.Null(attribute.DefaultValue.SelectedValue);
    }

    // RemoveOption's fallback checks "SelectedValue == null || SelectedValue == option" — but in the
    // live app, a bound ComboBox's SelectedItem clears itself the instant its selected item disappears
    // from ItemsSource, and that clear round-trips back through the two-way binding to
    // DefaultValue.SelectedValue synchronously — before RemoveOption's own check ever runs. So the
    // "== null" branch is what actually fires when a user removes the option they'd selected; this test
    // simulates that precondition directly, rather than relying on a bound ComboBox to produce it.
    [Fact]
    public void RemoveOption_NotNullable_SelectedValueAlreadyNulledByTheBoundComboBox_FallsBackToFirstRemainingOption()
    {
        AttributeDefinition attribute = new(AttributeScope.BuildDetail) { IsNullable = false };
        attribute.ValueOptions.Add("A");
        attribute.ValueOptions.Add("B");
        attribute.DefaultValue.SelectedValue = null; // what a bound ComboBox would already have done

        attribute.RemoveOptionCommand.Execute("A");

        Assert.Equal("B", attribute.DefaultValue.SelectedValue);
    }

    // Only reachable with nothing bound to DefaultValue.SelectedValue to clear it first (e.g. calling
    // RemoveOption directly, as this test does) — kept as a defensive fallback for that case, not because
    // it's the path the live UI actually takes.
    [Fact]
    public void RemoveOption_NotNullable_NoBoundComboBox_StillFallsBackViaTheEqualityCheck()
    {
        AttributeDefinition attribute = new(AttributeScope.BuildDetail) { IsNullable = false };
        attribute.ValueOptions.Add("A");
        attribute.ValueOptions.Add("B");
        attribute.DefaultValue.SelectedValue = "A";

        attribute.RemoveOptionCommand.Execute("A");

        Assert.Equal("B", attribute.DefaultValue.SelectedValue);
    }

    // Mirrors what a bound ComboBox does: removing the last remaining option, matching the precondition
    // above (SelectedValue already nulled by the binding before RemoveOption's own check runs). With
    // nothing left to fall back to, it correctly stays null rather than dangling on the removed option.
    [Fact]
    public void RemoveOption_NotNullable_RemovingTheLastOption_LeavesSelectedValueNull()
    {
        AttributeDefinition attribute = new(AttributeScope.BuildDetail) { IsNullable = false };
        attribute.ValueOptions.Add("A");
        attribute.DefaultValue.SelectedValue = null; // what a bound ComboBox would already have done

        attribute.RemoveOptionCommand.Execute("A");

        Assert.Null(attribute.DefaultValue.SelectedValue);
    }

    [Fact]
    public void RemoveOption_RaisesValueOptionsChangedWithTheRemovedValue()
    {
        AttributeDefinition attribute = new(AttributeScope.Map) { NewOptionText = "Zealot" };
        attribute.AddOptionCommand.Execute(null);
        CollectionChangeEventArgs? received = null;
        attribute.ValueOptionsChanged += (_, e) => received = e;

        attribute.RemoveOptionCommand.Execute("Zealot");

        Assert.NotNull(received);
        Assert.Equal(CollectionChangeAction.Remove, received!.Action);
        Assert.Equal("Zealot", received.Element);
        Assert.Empty(attribute.ValueOptions);
    }
}
