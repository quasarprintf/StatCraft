using System;
using System.Collections.Generic;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Maps;

namespace StatCraft.Services.DataFiltering
{
    // Predicates behind the Maps tab's filter bar. Takes plain values rather than filter-slot view models
    // so the rules stay independently testable; MapsPageViewModel is what unpacks a slot into these.
    //
    // Two conventions run through all of them. An empty constraint is inactive and matches everything —
    // adding a filter without filling it in must not hide anything, matching the Data tab. And a map with
    // no value for the attribute is excluded unless includeUnset says otherwise, since a freshly defined
    // attribute is unset on every map and would otherwise swallow the entire list.
    internal static class MapFilter
    {
        public static bool MatchesName(Map map, string? nameFilter) =>
            string.IsNullOrWhiteSpace(nameFilter) ||
            map.Name.Contains(nameFilter.Trim(), StringComparison.OrdinalIgnoreCase);

        // Numeric and Percent attributes. Bounds are inclusive, and either end may be left open.
        public static bool MatchesRange(AttributeValue value, decimal? min, decimal? max, bool includeUnset)
        {
            if (min == null && max == null)
                return true;
            if (!value.HasValue)
                return includeUnset;

            decimal actual = value.Attribute.Type == AttributeType.Percent
                ? value.PercentValue ?? 0m
                : value.NumericValue ?? 0m;
            return (min == null || actual >= min) && (max == null || actual <= max);
        }

        // Values attributes: does the map's selected option appear among the checked ones? `actual` is
        // only read once hasValue has ruled out the unset case, so callers may pass any placeholder for
        // an unset value.
        public static bool MatchesSelection<T>(IReadOnlySet<T> checkedValues, bool hasValue, T actual, bool includeUnset)
        {
            if (checkedValues.Count == 0)
                return true;
            if (!hasValue)
                return includeUnset;

            return checkedValues.Contains(actual);
        }

        // Bool attributes. A three-state checkbox rather than a checked-values set: null means no
        // constraint on this dimension (matches both true and false), otherwise the map's value must
        // equal it exactly.
        public static bool MatchesBool(AttributeValue value, bool? filterValue, bool includeUnset)
        {
            if (filterValue == null)
                return true;
            if (!value.HasValue)
                return includeUnset;

            return value.BoolValue == filterValue;
        }
    }
}
