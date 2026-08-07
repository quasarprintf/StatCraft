using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Attributes.DynamicAttribute;
using StatCraft.Models.GameData.Builds;
using StatCraft.ViewModels;

namespace StatCraft.Tests;

public class GameAttributeEditorViewModelTests
{
    [Fact]
    public void Constructor_NumericAttributeWithConfiguredDefault_SeedsNumericValueFromTemplate()
    {
        DynamicAttribute template = new() { Type = AttributeType.Numeric, NumericValue = 14 };

        DynamicAttribute editor = template.Clone();

        Assert.Equal(14, editor.NumericValue);
    }

    [Fact]
    public void Constructor_BoolAttributeWithConfiguredDefault_SeedsBoolValueFromTemplate()
    {
        DynamicAttribute template = new() { Type = AttributeType.Bool, BoolValue = true };

        DynamicAttribute editor = template.Clone();

        Assert.True(editor.BoolValue);
    }

    [Fact]
    public void Constructor_PercentAttributeWithConfiguredDefault_SeedsPercentValueFromTemplate()
    {
        DynamicAttribute template = new() { Type = AttributeType.Percent, PercentValue = 75 };

        DynamicAttribute editor = template.Clone();

        Assert.Equal(75, editor.PercentValue);
    }

    [Fact]
    public void Constructor_ValuesAttributeWithConfiguredDefault_SeedsSelectedValueFromTemplate()
    {
        DynamicAttribute template = new() { Type = AttributeType.Values, SelectedValue = "Aggressive" };

        DynamicAttribute editor = template.Clone();

        Assert.Equal("Aggressive", editor.SelectedValue);
    }

    [Fact]
    public void ApplyValue_OverridesTemplateDefaultWithCachedValue()
    {
        DynamicAttribute template = new() { Type = AttributeType.Numeric, NumericValue = 14 };
        DynamicAttribute editor = template.Clone();

        editor.ApplyValue(editor.SerializeValue()); // sanity: round-trips at the default first
        Assert.Equal(14, editor.NumericValue);

        editor.ApplyValue(AttributeValueSerializer.Serialize(AttributeType.Numeric, 20, false, 0, null));

        Assert.Equal(20, editor.NumericValue);
    }
}
