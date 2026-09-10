using CommunityToolkit.Mvvm.ComponentModel;
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
    public bool AcceptNull { get; set; }
    public int? FilterValue { get; set; }
    private Func<T,int?> _filteredPropertyMap;

    public IntFilter(Func<T,int?> filteredPropertyMap)
    {
        _filteredPropertyMap = filteredPropertyMap;
    }
    public bool MatchesFilter(T filter, bool? acceptNullOverride)
    {
        if (acceptNullOverride == null)
            acceptNullOverride = AcceptNull;
        if (FilterValue == null)
            return true;
        int? property = _filteredPropertyMap(filter);
        if (property == null)
            return acceptNullOverride.Value;
        return FilterValue.Value.CompareTo(property) == _compareType;
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
