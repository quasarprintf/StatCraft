using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public class SetMemberFilter<T,U> : IFilter<T>
{
    public bool? AcceptNull { get; set; }
    public HashSet<U>? FilterValue { get; set; }
    private Func<T,U?> _filteredPropertyMap;

    public SetMemberFilter(Func<T,U?> filteredPropertyMap)
    {
        _filteredPropertyMap = filteredPropertyMap;
    }
    public bool MatchesFilter(T? candidate, bool? acceptNullOverride = null)
    {
        if (candidate == null)
            return false;
        U? mapped = _filteredPropertyMap(candidate);
        if (mapped == null)
            return acceptNullOverride ?? AcceptNull ?? false;
        if (FilterValue == null || FilterValue.Count == 0)
            return true;
        return FilterValue.Contains(mapped);
    }
}
