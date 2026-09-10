using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public interface IDecimalFilter : IFilter
{
    public decimal? FilterValue { get; set; }
}
public partial class DecimalFilter<T> : IFilter<T>, IDecimalFilter
{
    private int _compareType;
    public bool AcceptNull { get; set; }
    public decimal? FilterValue { get; set; }
    private Func<T,decimal?> _filteredPropertyMap;

    public DecimalFilter(Func<T,decimal?> filteredPropertyMap)
    {
        _filteredPropertyMap = filteredPropertyMap;
    }
    public bool MatchesFilter(T filter, bool? acceptNullOverride)
    {
        if (acceptNullOverride == null)
            acceptNullOverride = AcceptNull;
        if (FilterValue == null)
            return true;
        decimal? property = _filteredPropertyMap(filter);
        if (property == null)
            return acceptNullOverride.Value;
        return FilterValue.Value.CompareTo(property) == _compareType;
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
