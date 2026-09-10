using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public interface IStringFilter : IFilter
{
    public string? FilterValue { get; set; }
}
public class StringFilter<T> : IFilter<T>, IStringFilter
{
    public bool MatchExact { get; set; }
    public bool? AcceptNull { get; set; }
    public string? FilterValue { get; set; }
    private Func<T,string?> _filteredPropertyMap;

    public StringFilter(Func<T,string?> filteredPropertyMap)
    {
        _filteredPropertyMap = filteredPropertyMap;
    }
    public bool MatchesFilter(T candidate, bool? acceptNullOverride = null)
    {
        if (candidate == null)
            return false;
        string? mapped = _filteredPropertyMap(candidate);
        if (string.IsNullOrWhiteSpace(mapped))
            return acceptNullOverride ?? AcceptNull ?? false;
        if (string.IsNullOrWhiteSpace(FilterValue))
            return true;
        if (MatchExact)
            return mapped.Equals(FilterValue, StringComparison.OrdinalIgnoreCase);
        else
            return mapped.Contains(FilterValue, StringComparison.OrdinalIgnoreCase);
    }
}
