using StatCraft.Models.GameData.Attributes;

namespace StatCraft.Tests;

public class AttributeValueTests
{
    [Fact]
    public void Definition_IsNullable_DefaultsToTrue()
    {
        AttributeDefinition definition = new(AttributeScope.Map);

        Assert.True(definition.IsNullable);
    }

    // Map/Attributes-tab attributes are nullable — Clear() must still put them back to genuinely unset,
    // not a concrete zero-value, or "no value entered" and "explicitly 0" become indistinguishable.
    [Fact]
    public void Clear_Nullable_ResetsEveryFieldToNull()
    {
        AttributeDefinition definition = new(AttributeScope.Map) { IsNullable = true };
        AttributeValue value = definition.DefaultValue;
        value.NumericValue = 5m;
        value.BoolValue = true;
        value.PercentValue = 50m;
        value.SelectedValue = "Zealot";

        value.ClearCommand.Execute(null);

        Assert.Null(value.NumericValue);
        Assert.Null(value.BoolValue);
        Assert.Null(value.PercentValue);
        Assert.Null(value.SelectedValue);
        Assert.False(value.HasValue);
    }

    // Pins the fix for a real bug: Clear()'s non-nullable branch originally assigned bare `default`,
    // which for a nullable-typed property (decimal?/bool?) is null — not the underlying type's zero
    // value — so it silently behaved exactly like the nullable branch instead of resetting to a concrete
    // 0/false, defeating the entire point of "a Build-detail attribute must never read as unset".
    [Fact]
    public void Clear_NotNullable_ResetsNumericAndPercentToZeroNotNull()
    {
        AttributeDefinition definition = new(AttributeScope.BuildDetail) { IsNullable = false };
        AttributeValue value = definition.DefaultValue;
        value.NumericValue = 5m;
        value.PercentValue = 50m;

        value.ClearCommand.Execute(null);

        Assert.Equal(0m, value.NumericValue);
        Assert.Equal(0m, value.PercentValue);
    }

    [Fact]
    public void Clear_NotNullable_ResetsBoolToFalseNotNull()
    {
        AttributeDefinition definition = new(AttributeScope.BuildDetail) { IsNullable = false };
        AttributeValue value = definition.DefaultValue;
        value.BoolValue = true;

        value.ClearCommand.Execute(null);

        Assert.Equal(false, value.BoolValue);
    }

    [Fact]
    public void Clear_NotNullable_SelectedValue_FallsBackToFirstOption()
    {
        AttributeDefinition definition = new(AttributeScope.BuildDetail) { IsNullable = false, Type = AttributeType.Values };
        definition.ValueOptions.Add("Zealot");
        definition.ValueOptions.Add("Stalker");
        AttributeValue value = definition.DefaultValue;
        value.SelectedValue = "Stalker";

        value.ClearCommand.Execute(null);

        Assert.Equal("Zealot", value.SelectedValue);
    }

    [Fact]
    public void Clear_NotNullable_SelectedValue_NullWhenNoOptionsYet()
    {
        AttributeDefinition definition = new(AttributeScope.BuildDetail) { IsNullable = false, Type = AttributeType.Values };
        AttributeValue value = definition.DefaultValue;

        value.ClearCommand.Execute(null);

        Assert.Null(value.SelectedValue);
    }

    // A non-nullable attribute is still meaningfully "concrete" after Clear() — HasValue reflects that,
    // matching how it's meant to never present as unset.
    [Theory]
    [InlineData(AttributeType.Numeric)]
    [InlineData(AttributeType.Bool)]
    [InlineData(AttributeType.Percent)]
    public void Clear_NotNullable_HasValueIsTrueAfterward(AttributeType type)
    {
        AttributeDefinition definition = new(AttributeScope.BuildDetail) { IsNullable = false, Type = type };
        AttributeValue value = definition.DefaultValue;

        value.ClearCommand.Execute(null);

        Assert.True(value.HasValue);
    }
}
