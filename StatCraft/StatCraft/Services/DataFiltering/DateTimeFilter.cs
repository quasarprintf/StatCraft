using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public class DateTimeFilter<T> : IFilter<T>
{
    private int _compareType;
    public bool? AcceptNull { get; set; }
    public DateTime? FilterValue { get; set; }
    private Func<T,DateTime?> _filteredPropertyMap;

    public DateTimeFilter(Func<T,DateTime?> filteredPropertyMap)
    {
        _filteredPropertyMap = filteredPropertyMap;
    }
    public bool MatchesFilter(T? candidate, bool? acceptNullOverride = null)
    {
        if (candidate == null)
            return false;
        DateTime? mapped = _filteredPropertyMap(candidate);
        if (mapped == null)
            return acceptNullOverride ?? AcceptNull ?? false;
        if (FilterValue == null)
            return true;
        int compareValue = FilterValue.Value.CompareTo(mapped.Value);
        return compareValue == 0 || compareValue == _compareType;
    }

    public DateTimeFilter<T> SetMatchExact()
    {
        _compareType = 0;
        return this;
    }
    public DateTimeFilter<T> SetMatchLowerBound()
    {
        _compareType = -1;
        return this;
    }
    public DateTimeFilter<T> SetMatchUpperBound()
    {
        _compareType = 1;
        return this;
    }
}
