using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.CollatedFilter;

namespace StatCraft.Tests;

// Covers the DataFiltering primitives introduced to replace the old static AttributeFilter helpers.
// Everything here exercises the filters directly rather than through a page ViewModel, so a change in
// composition semantics surfaces here rather than as a mysterious filtering change three layers up.
public class FilterTests
{
    private sealed class Subject
    {
        public string? Text { get; init; }
        public bool? Flag { get; init; }
        public int? Count { get; init; }
        public decimal? Amount { get; init; }
        public Subject? Inner { get; init; }
    }

    #region StringFilter

    [Fact]
    public void StringFilter_NoFilterValue_MatchesAnySetValue()
    {
        StringFilter<Subject> filter = new(s => s.Text);

        Assert.True(filter.MatchesFilter(new Subject { Text = "Altitude LE" }));
    }

    [Theory]
    [InlineData("alt", true)]
    [InlineData("ALT", true)]
    [InlineData("tude L", true)]
    [InlineData("Rorschach", false)]
    public void StringFilter_IsCaseInsensitiveSubstringMatchByDefault(string filterValue, bool expected)
    {
        StringFilter<Subject> filter = new(s => s.Text) { FilterValue = filterValue };

        Assert.Equal(expected, filter.MatchesFilter(new Subject { Text = "Altitude LE" }));
    }

    [Theory]
    [InlineData("altitude le", true)]
    [InlineData("Altitude LE", true)]
    [InlineData("Altitude", false)]
    public void StringFilter_MatchExact_RequiresTheWholeValue(string filterValue, bool expected)
    {
        StringFilter<Subject> filter = new(s => s.Text) { FilterValue = filterValue, MatchExact = true };

        Assert.Equal(expected, filter.MatchesFilter(new Subject { Text = "Altitude LE" }));
    }

    // A blank candidate is treated as "no value at all" rather than as an empty string that trivially
    // matches, so it takes the AcceptNull path even when no filter text has been typed. Anything
    // filtering on a user-editable name has to opt in via AcceptNull or blank-named rows disappear.
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    [InlineData("   ", null, false)]
    [InlineData("", false, false)]
    [InlineData("", true, true)]
    public void StringFilter_BlankCandidate_FollowsAcceptNull(string? text, bool? acceptNull, bool expected)
    {
        StringFilter<Subject> filter = new(s => s.Text) { AcceptNull = acceptNull };

        Assert.Equal(expected, filter.MatchesFilter(new Subject { Text = text }));
    }

    [Fact]
    public void StringFilter_NullCandidate_NeverMatches()
    {
        StringFilter<Subject> filter = new(s => s.Text) { AcceptNull = true };

        Assert.False(filter.MatchesFilter(null!));
    }

    #endregion

    #region BoolFilter

    [Fact]
    public void BoolFilter_NoFilterValue_MatchesAnySetValue()
    {
        BoolFilter<Subject> filter = new(s => s.Flag);

        Assert.True(filter.MatchesFilter(new Subject { Flag = true }));
        Assert.True(filter.MatchesFilter(new Subject { Flag = false }));
    }

    // "false" is a real value, not an absence: a subject explicitly set false must match a filter
    // looking for false without needing AcceptNull, and must not match one looking for true.
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public void BoolFilter_SetValue_MustEqualTheFilter(bool filterValue, bool actual, bool expected)
    {
        BoolFilter<Subject> filter = new(s => s.Flag) { FilterValue = filterValue };

        Assert.Equal(expected, filter.MatchesFilter(new Subject { Flag = actual }));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void BoolFilter_UnsetValue_FollowsAcceptNull(bool? acceptNull, bool expected)
    {
        BoolFilter<Subject> filter = new(s => s.Flag) { FilterValue = true, AcceptNull = acceptNull };

        Assert.Equal(expected, filter.MatchesFilter(new Subject { Flag = null }));
    }

    [Fact]
    public void BoolFilter_AcceptNullOverride_WinsOverTheFiltersOwnAcceptNull()
    {
        BoolFilter<Subject> filter = new(s => s.Flag) { FilterValue = true, AcceptNull = false };

        Assert.True(filter.MatchesFilter(new Subject { Flag = null }, acceptNullOverride: true));
    }

    #endregion

    #region IntFilter / DecimalFilter

    [Fact]
    public void IntFilter_NoFilterValue_MatchesAnySetValue()
    {
        IntFilter<Subject> filter = new(s => s.Count);

        Assert.True(filter.MatchesFilter(new Subject { Count = 42 }));
    }

    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(16, false)]
    public void IntFilter_DefaultsToExactMatch(int actual, bool expected)
    {
        IntFilter<Subject> filter = new(s => s.Count) { FilterValue = 15 };

        Assert.Equal(expected, filter.MatchesFilter(new Subject { Count = actual }));
    }

    // Bounds are inclusive at the boundary: a lower bound of 15 accepts 15 and anything above it.
    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    public void IntFilter_LowerBound_IsInclusive(int actual, bool expected)
    {
        IntFilter<Subject> filter = new IntFilter<Subject>(s => s.Count) { FilterValue = 15 }.SetMatchLowerBound();

        Assert.Equal(expected, filter.MatchesFilter(new Subject { Count = actual }));
    }

    [Theory]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, false)]
    public void IntFilter_UpperBound_IsInclusive(int actual, bool expected)
    {
        IntFilter<Subject> filter = new IntFilter<Subject>(s => s.Count) { FilterValue = 15 }.SetMatchUpperBound();

        Assert.Equal(expected, filter.MatchesFilter(new Subject { Count = actual }));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void IntFilter_UnsetValue_FollowsAcceptNull(bool? acceptNull, bool expected)
    {
        IntFilter<Subject> filter = new(s => s.Count) { FilterValue = 15, AcceptNull = acceptNull };

        Assert.Equal(expected, filter.MatchesFilter(new Subject { Count = null }));
    }

    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(16, true)]
    public void DecimalFilter_LowerBound_IsInclusive(int actual, bool expected)
    {
        DecimalFilter<Subject> filter = new DecimalFilter<Subject>(s => s.Amount) { FilterValue = 15m }.SetMatchLowerBound();

        Assert.Equal(expected, filter.MatchesFilter(new Subject { Amount = actual }));
    }

    [Theory]
    [InlineData(14, true)]
    [InlineData(15, true)]
    [InlineData(16, false)]
    public void DecimalFilter_UpperBound_IsInclusive(int actual, bool expected)
    {
        DecimalFilter<Subject> filter = new DecimalFilter<Subject>(s => s.Amount) { FilterValue = 15m }.SetMatchUpperBound();

        Assert.Equal(expected, filter.MatchesFilter(new Subject { Amount = actual }));
    }

    // A lower bound ANDed with an upper bound is how NumericRangeFilterSlotViewModel builds a range,
    // so the composed form is what actually ships — bounds open at either end included.
    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(15, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void DecimalFilter_LowerAndUpperBoundAnded_FormAnInclusiveRange(int actual, bool expected)
    {
        DecimalFilter<Subject> lower = new DecimalFilter<Subject>(s => s.Amount) { FilterValue = 10m }.SetMatchLowerBound();
        DecimalFilter<Subject> upper = new DecimalFilter<Subject>(s => s.Amount) { FilterValue = 20m }.SetMatchUpperBound();
        AndFilter<Subject> range = new([lower, upper]);

        Assert.Equal(expected, range.MatchesFilter(new Subject { Amount = actual }));
    }

    [Fact]
    public void DecimalFilter_OpenEndedRange_OnlyConstrainsTheEndThatIsSet()
    {
        DecimalFilter<Subject> lower = new DecimalFilter<Subject>(s => s.Amount) { FilterValue = 10m }.SetMatchLowerBound();
        DecimalFilter<Subject> noUpper = new DecimalFilter<Subject>(s => s.Amount).SetMatchUpperBound();
        AndFilter<Subject> range = new([lower, noUpper]);

        Assert.True(range.MatchesFilter(new Subject { Amount = 9999m }));
        Assert.False(range.MatchesFilter(new Subject { Amount = 9m }));
    }

    #endregion

    #region AndFilter / OrFilter

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void AndFilter_RequiresEveryChildToMatch(bool first, bool second, bool expected)
    {
        AndFilter<Subject> filter = new([Constant(first), Constant(second)]);

        Assert.Equal(expected, filter.MatchesFilter(new Subject { Flag = true }));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    public void OrFilter_RequiresAnyChildToMatch(bool first, bool second, bool expected)
    {
        OrFilter<Subject> filter = new([Constant(first), Constant(second)]);

        Assert.Equal(expected, filter.MatchesFilter(new Subject { Flag = true }));
    }

    // An unconfigured filter constrains nothing — this is what makes a checkbox slot with no options
    // checked, or a range with neither bound typed in, leave the list alone.
    [Fact]
    public void CollatedFilters_WithNoChildren_MatchEverything()
    {
        Assert.True(new AndFilter<Subject>([]).MatchesFilter(new Subject { Flag = true }));
        Assert.True(new OrFilter<Subject>([]).MatchesFilter(new Subject { Flag = true }));
    }

    [Fact]
    public void CollatedFilters_NullCandidate_NeverMatches()
    {
        Assert.False(new AndFilter<Subject>([]).MatchesFilter(null!));
        Assert.False(new OrFilter<Subject>([]).MatchesFilter(null!));
    }

    // The projecting overload maps the candidate before delegating, so children filter the mapped
    // value — this is how the page ViewModels reach an AttributeValue hanging off a Map or BuildNode.
    [Fact]
    public void AndFilter_ProjectsThroughTheMapBeforeDelegatingToChildren()
    {
        BoolFilter<Subject> inner = new(s => s.Flag) { FilterValue = true };
        AndFilter<Subject, Subject> filter = new([inner], s => s.Inner!);

        Assert.True(filter.MatchesFilter(new Subject { Inner = new Subject { Flag = true } }));
        Assert.False(filter.MatchesFilter(new Subject { Inner = new Subject { Flag = false } }));
    }

    // The mapped-null check runs ahead of the empty-children check, so the wrapper decides on its own
    // AcceptNull and the child's is never consulted once the projection yields null. Anything wrapping
    // a child filter in a projecting AndFilter has to set AcceptNull on the wrapper, not just the child.
    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AndFilter_MappedNull_FollowsTheWrapperAcceptNullNotTheChildOne(bool? wrapperAcceptNull, bool expected)
    {
        BoolFilter<Subject> inner = new(s => s.Flag) { FilterValue = true, AcceptNull = true };
        AndFilter<Subject, Subject> filter = new([inner], s => s.Inner!) { AcceptNull = wrapperAcceptNull };

        Assert.Equal(expected, filter.MatchesFilter(new Subject { Inner = null }));
    }

    // A collated filter hands its own AcceptNull down as the override, so it wins over whatever its
    // children were configured with.
    [Fact]
    public void CollatedFilter_PassesItsAcceptNullDownToChildrenAsAnOverride()
    {
        BoolFilter<Subject> inner = new(s => s.Flag) { FilterValue = true, AcceptNull = false };
        AndFilter<Subject> filter = new([inner]) { AcceptNull = true };

        Assert.True(filter.MatchesFilter(new Subject { Flag = null }));
    }

    private static BoolFilter<Subject> Constant(bool matches) => new(_ => matches) { FilterValue = true };

    #endregion
}
