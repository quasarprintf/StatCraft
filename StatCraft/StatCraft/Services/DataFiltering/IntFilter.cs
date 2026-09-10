using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public interface IIntFilter : IFilter
{
    int? FilterValue { get; set; }
}
public partial class IntFilter<T> : IFilter<T>, IIntFilter
{
    public event Action? FilterChanged;

    private int _compareType;
    public bool? AcceptNull { get; set; }
    public int? FilterValue { get; set; }
    private Func<T,int?> _filteredPropertyMap;

    public IntFilter(Func<T,int?> filteredPropertyMap)
    {
        _filteredPropertyMap = filteredPropertyMap;
    }
    public bool MatchesFilter(T candidate, bool? acceptNullOverride = null)
    {
        if (candidate == null)
            return false;
        int? mapped = _filteredPropertyMap(candidate);
        if (mapped == null)
            return acceptNullOverride ?? AcceptNull ?? false;
        if (FilterValue == null)
            return true;
        int compareValue = FilterValue.Value.CompareTo(mapped);
        return compareValue == 0 || compareValue == _compareType;
    }

    public IntFilter<T> SetMatchExact()
    {
        _compareType = 0;
        return this;
    }
    public IntFilter<T> SetMatchLowerBound()
    {
        _compareType = -1;
        return this;
    }
    public IntFilter<T> SetMatchUpperBound()
    {
        _compareType = 1;
        return this;
    }
}
