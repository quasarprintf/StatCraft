using CommunityToolkit.Mvvm.ComponentModel;
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

    public bool AcceptNull { get; set; }
    public bool? FilterValue { get; set; }
    private Func<T,bool?> _filteredPropertyMap;

    public BoolFilter(Func<T,bool?> filteredPropertyMap)
    {
        _filteredPropertyMap = filteredPropertyMap;
    }
    public bool MatchesFilter(T filter, bool? acceptNullOverride = null)
    {
        if (acceptNullOverride == null)
            acceptNullOverride = AcceptNull;
        if (FilterValue == null)
            return true;
        bool? property = _filteredPropertyMap(filter);
        if (property == null)
            return acceptNullOverride.Value;
        return property == FilterValue;
    }
}
