using StatCraft.Models.GameData.Attributes;

namespace StatCraft.Tests;

public class AttributeValueSerializerTests
{
    [Theory]
    [InlineData(AttributeType.Numeric)]
    [InlineData(AttributeType.Bool)]
    [InlineData(AttributeType.Percent)]
    public void Parse_EmptyString_LeavesTheMatchingSlotNull(AttributeType type)
    {
        AttributeValueSerializer.ParsedValue parsed = AttributeValueSerializer.Parse(type, "");

        Assert.Null(parsed.NumericValue);
        Assert.Null(parsed.BoolValue);
        Assert.Null(parsed.PercentValue);
    }

    [Theory]
    [InlineData(AttributeType.Numeric, "not a number")]
    [InlineData(AttributeType.Bool, "not a bool")]
    [InlineData(AttributeType.Percent, "not a number")]
    public void Parse_UnparseableString_LeavesTheMatchingSlotNullRatherThanDefaulting(AttributeType type, string garbage)
    {
        AttributeValueSerializer.ParsedValue parsed = AttributeValueSerializer.Parse(type, garbage);

        Assert.Null(parsed.NumericValue);
        Assert.Null(parsed.BoolValue);
        Assert.Null(parsed.PercentValue);
    }

    [Fact]
    public void Parse_ValidNumeric_RoundTrips()
    {
        AttributeValueSerializer.ParsedValue parsed = AttributeValueSerializer.Parse(AttributeType.Numeric, "12.5");
        Assert.Equal(12.5m, parsed.NumericValue);
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("False", false)]
    public void Parse_ValidBool_RoundTrips(string stored, bool expected)
    {
        AttributeValueSerializer.ParsedValue parsed = AttributeValueSerializer.Parse(AttributeType.Bool, stored);
        Assert.Equal(expected, parsed.BoolValue);
    }

    [Fact]
    public void Parse_ValidPercent_RoundTrips()
    {
        AttributeValueSerializer.ParsedValue parsed = AttributeValueSerializer.Parse(AttributeType.Percent, "55.5");
        Assert.Equal(55.5m, parsed.PercentValue);
    }

    [Fact]
    public void Parse_ValuesEmptyString_IsNullSelectedValue()
    {
        AttributeValueSerializer.ParsedValue parsed = AttributeValueSerializer.Parse(AttributeType.Values, "");
        Assert.Null(parsed.SelectedValue);
    }

    [Fact]
    public void Parse_ValuesNonEmptyString_IsItself()
    {
        AttributeValueSerializer.ParsedValue parsed = AttributeValueSerializer.Parse(AttributeType.Values, "Zealot");
        Assert.Equal("Zealot", parsed.SelectedValue);
    }

    [Theory]
    [InlineData(AttributeType.Numeric)]
    [InlineData(AttributeType.Bool)]
    [InlineData(AttributeType.Percent)]
    [InlineData(AttributeType.Values)]
    public void Serialize_ThenParse_RoundTrips(AttributeType type)
    {
        string serialized = type switch
        {
            AttributeType.Numeric => AttributeValueSerializer.Serialize(type, 12.5m, false, 0m, null),
            AttributeType.Bool => AttributeValueSerializer.Serialize(type, 0m, true, 0m, null),
            AttributeType.Percent => AttributeValueSerializer.Serialize(type, 0m, false, 55.5m, null),
            AttributeType.Values => AttributeValueSerializer.Serialize(type, 0m, false, 0m, "Zealot"),
            _ => "",
        };

        AttributeValueSerializer.ParsedValue parsed = AttributeValueSerializer.Parse(type, serialized);

        switch (type)
        {
            case AttributeType.Numeric: Assert.Equal(12.5m, parsed.NumericValue); break;
            case AttributeType.Bool: Assert.True(parsed.BoolValue); break;
            case AttributeType.Percent: Assert.Equal(55.5m, parsed.PercentValue); break;
            case AttributeType.Values: Assert.Equal("Zealot", parsed.SelectedValue); break;
        }
    }
}
