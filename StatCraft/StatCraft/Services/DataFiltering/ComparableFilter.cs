using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public class ComparableFilter<T, F> : IFilter<T> where F : struct, IComparable
{
    private int _compareType;
    public bool? AcceptNull { get; set; }
    public F? FilterValue { get; set; }
    private Func<T,F?> _filteredPropertyMap;

    public ComparableFilter(Func<T,F?> filteredPropertyMap)
    {
        _filteredPropertyMap = filteredPropertyMap;
    }
    public bool MatchesFilter(T? candidate, bool? acceptNullOverride = null)
    {
        if (candidate == null)
            return false;
        F? mapped = _filteredPropertyMap(candidate);
        if (mapped == null)
            return acceptNullOverride ?? AcceptNull ?? false;
        if (FilterValue == null)
            return true;
        int compareValue = FilterValue.Value.CompareTo(mapped.Value);
        return compareValue == 0 || compareValue == _compareType;
    }

    public ComparableFilter<T, F> SetMatchExact()
    {
        _compareType = 0;
        return this;
    }
    public ComparableFilter<T, F> SetMatchLowerBound()
    {
        _compareType = -1;
        return this;
    }
    public ComparableFilter<T, F> SetMatchUpperBound()
    {
        _compareType = 1;
        return this;
    }
}
