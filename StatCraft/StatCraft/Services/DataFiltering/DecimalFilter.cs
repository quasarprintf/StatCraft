using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public class DecimalFilter<T> : IFilter<T>
{
    private int _compareType;
    public bool? AcceptNull { get; set; }
    public decimal? FilterValue { get; set; }
    private Func<T,decimal?> _filteredPropertyMap;

    public DecimalFilter(Func<T,decimal?> filteredPropertyMap)
    {
        _filteredPropertyMap = filteredPropertyMap;
    }
    public bool MatchesFilter(T candidate, bool? acceptNullOverride = null)
    {
        if (candidate == null)
            return false;
        decimal? mapped = _filteredPropertyMap(candidate);
        if (mapped == null)
            return acceptNullOverride ?? AcceptNull ?? false;
        if (FilterValue == null)
            return true;
        int compareValue = FilterValue.Value.CompareTo(mapped.Value);
        return compareValue == 0 || compareValue == _compareType;
    }

    public DecimalFilter<T> SetMatchExact()
    {
        _compareType = 0;
        return this;
    }
    public DecimalFilter<T> SetMatchLowerBound()
    {
        _compareType = -1;
        return this;
    }
    public DecimalFilter<T> SetMatchUpperBound()
    {
        _compareType = 1;
        return this;
    }
}
