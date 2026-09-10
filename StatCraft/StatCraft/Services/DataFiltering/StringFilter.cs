using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public interface IStringFilter : IFilter
{
    public string? FilterValue { get; set; }
}
public partial class StringFilter<T> : IFilter<T>
{
    public event Action? FilterChanged;

    public bool MatchExact { get; set; }
    public bool AcceptNull { get; set; }
    public string FilterValue { get; set; } = "";
    private Func<T,string?> _filteredPropertyMap;

    public StringFilter(Func<T,string?> filteredPropertyMap)
    {
        _filteredPropertyMap = filteredPropertyMap;
    }
    public bool MatchesFilter(T filter, bool? acceptNullOverride = null)
    {
        if (acceptNullOverride == null)
            acceptNullOverride = AcceptNull;
        if (string.IsNullOrWhiteSpace(FilterValue))
            return true;
        string? property = _filteredPropertyMap(filter);
        if (string.IsNullOrWhiteSpace(property))
            return acceptNullOverride.Value;
        if (MatchExact)
            return property.Equals(FilterValue, StringComparison.OrdinalIgnoreCase);
        else
            return property.Contains(FilterValue, StringComparison.OrdinalIgnoreCase);
    }
}
