using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Maps;
using StatCraft.Services.DataFiltering;
using StatCraft.ViewModels;

namespace StatCraft.Tests;

public class MapFilterTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("alt", true)]
    [InlineData("ALT", true)]
    [InlineData(" LE ", true)]
    [InlineData("Rorschach", false)]
    public void MatchesName_IsCaseInsensitiveSubstringMatch(string filter, bool expected)
    {
        Map map = new() { Name = "Altitude LE" };
        Assert.Equal(expected, MapsPageViewModel.MatchesName(map, filter));
    }

    [Fact]
    public void MatchesRange_NoBounds_MatchesEvenAnUnsetValue()
    {
        AttributeValue value = Value(AttributeType.Numeric);
        Assert.True(AttributeFilter.MatchesRange(value, null, null, includeUnset: false));
    }

    [Theory]
    [InlineData(10, 20, true)]
    [InlineData(16, 20, false)]
    [InlineData(null, 15, true)]
    [InlineData(null, 14, false)]
    [InlineData(15, null, true)]
    [InlineData(16, null, false)]
    public void MatchesRange_BoundsAreInclusiveAndEitherEndMayBeOpen(int? min, int? max, bool expected)
    {
        AttributeValue value = Value(AttributeType.Numeric);
        value.NumericValue = 15m;

        Assert.Equal(expected, AttributeFilter.MatchesRange(value, min, max, includeUnset: false));
    }

    [Fact]
    public void MatchesRange_PercentAttribute_ReadsThePercentSlot()
    {
        AttributeValue value = Value(AttributeType.Percent);
        value.PercentValue = 60m;

        Assert.True(AttributeFilter.MatchesRange(value, 50, 70, includeUnset: false));
        Assert.False(AttributeFilter.MatchesRange(value, 10, 50, includeUnset: false));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void MatchesRange_UnsetValueWithActiveBounds_FollowsIncludeUnset(bool includeUnset, bool expected)
    {
        AttributeValue value = Value(AttributeType.Numeric);
        Assert.Equal(expected, AttributeFilter.MatchesRange(value, 10, 20, includeUnset));
    }

    [Fact]
    public void MatchesSelection_NothingChecked_MatchesEvenAnUnsetValue()
    {
        Assert.True(AttributeFilter.MatchesSelection(new HashSet<string>(), hasValue: false, "", includeUnset: false));
    }

    [Theory]
    [InlineData("Macro", true)]
    [InlineData("Rush", false)]
    public void MatchesSelection_SetValue_MustBeAmongTheCheckedOptions(string actual, bool expected)
    {
        Assert.Equal(expected, AttributeFilter.MatchesSelection(new HashSet<string> { "Macro" }, hasValue: true, actual, includeUnset: false));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void MatchesSelection_UnsetValueWithCheckedOptions_FollowsIncludeUnset(bool includeUnset, bool expected)
    {
        Assert.Equal(expected, AttributeFilter.MatchesSelection(new HashSet<string> { "Macro" }, hasValue: false, "", includeUnset));
    }

    [Fact]
    public void MatchesBool_NoFilterValue_MatchesEvenAnUnsetValue()
    {
        AttributeValue value = Value(AttributeType.Bool);
        Assert.True(AttributeFilter.MatchesBool(value, null, includeUnset: false));
    }

    // "false" is a real value, not an absence — a map explicitly set to No must be matched by a filter
    // checking No, without needing includeUnset, and excluded by a filter checking Yes.
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    public void MatchesBool_SetValue_MustEqualTheFilter(bool filterValue, bool actual, bool expected)
    {
        AttributeValue value = Value(AttributeType.Bool);
        value.BoolValue = actual;
        Assert.Equal(expected, AttributeFilter.MatchesBool(value, filterValue, includeUnset: false));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void MatchesBool_UnsetValueWithFilterSet_FollowsIncludeUnset(bool includeUnset, bool expected)
    {
        AttributeValue value = Value(AttributeType.Bool);
        Assert.Equal(expected, AttributeFilter.MatchesBool(value, true, includeUnset));
    }

    private static AttributeValue Value(AttributeType type) => new(new AttributeDefinition { Name = "Attr", Type = type });
}
