using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public interface IBoolFilter : IFilter
{
    bool? FilterValue { get; set; }
}
public class BoolFilter<T> : IFilter<T>, IBoolFilter
{
    public event Action? FilterChanged;

    public bool? AcceptNull { get; set; }
    public bool? FilterValue { get; set; }
    private Func<T,bool?> _filteredPropertyMap;

    public BoolFilter(Func<T,bool?> filteredPropertyMap)
    {
        _filteredPropertyMap = filteredPropertyMap;
    }
    public bool MatchesFilter(T candidate, bool? acceptNullOverride = null)
    {
        if (candidate == null)
            return false;
        bool? mapped = _filteredPropertyMap(candidate);
        if (mapped == null)
            return acceptNullOverride ?? AcceptNull ?? false;
        if (FilterValue == null)
            return true;
        return mapped == FilterValue;
    }
}
