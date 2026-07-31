using StatCraft.Models.GameData.Builds;
using StatCraft.ViewModels;

namespace StatCraft.Tests;

public class GameAttributeEditorViewModelTests
{
    [Fact]
    public void Constructor_NumericAttributeWithConfiguredDefault_SeedsNumericValueFromTemplate()
    {
        BuildAttribute template = new() { Type = AttributeType.Numeric, NumericValue = 14 };

        GameAttributeEditorViewModel editor = new(template);

        Assert.Equal(14, editor.NumericValue);
    }

    [Fact]
    public void Constructor_BoolAttributeWithConfiguredDefault_SeedsBoolValueFromTemplate()
    {
        BuildAttribute template = new() { Type = AttributeType.Bool, BoolValue = true };

        GameAttributeEditorViewModel editor = new(template);

        Assert.True(editor.BoolValue);
    }

    [Fact]
    public void Constructor_PercentAttributeWithConfiguredDefault_SeedsPercentValueFromTemplate()
    {
        BuildAttribute template = new() { Type = AttributeType.Percent, PercentValue = 75 };

        GameAttributeEditorViewModel editor = new(template);

        Assert.Equal(75, editor.PercentValue);
    }

    [Fact]
    public void Constructor_ValuesAttributeWithConfiguredDefault_SeedsSelectedValueFromTemplate()
    {
        BuildAttribute template = new() { Type = AttributeType.Values, SelectedValue = "Aggressive" };

        GameAttributeEditorViewModel editor = new(template);

        Assert.Equal("Aggressive", editor.SelectedValue);
    }

    [Fact]
    public void ApplyValue_OverridesTemplateDefaultWithCachedValue()
    {
        BuildAttribute template = new() { Type = AttributeType.Numeric, NumericValue = 14 };
        GameAttributeEditorViewModel editor = new(template);

        editor.ApplyValue(editor.SerializeValue()); // sanity: round-trips at the default first
        Assert.Equal(14, editor.NumericValue);

        editor.ApplyValue(BuildAttributeValueSerializer.Serialize(AttributeType.Numeric, 20, false, 0, null));

        Assert.Equal(20, editor.NumericValue);
    }
}
