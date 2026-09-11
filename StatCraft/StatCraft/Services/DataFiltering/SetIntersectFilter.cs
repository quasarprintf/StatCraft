using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public class SetIntersectFilter<T,U> : IFilter<T>
{
    public bool? AcceptNull { get; set; }
    public HashSet<U>? FilterValue { get; set; }
    private Func<T,IEnumerable<U>?> _filteredPropertyMap;

    public SetIntersectFilter(Func<T,IEnumerable<U>?> filteredPropertyMap)
    {
        _filteredPropertyMap = filteredPropertyMap;
    }
    public bool MatchesFilter(T candidate, bool? acceptNullOverride = null)
    {
        if (candidate == null)
            return false;
        IEnumerable<U>? mapped = _filteredPropertyMap(candidate);
        if (mapped == null || !mapped.Any())
            return acceptNullOverride ?? AcceptNull ?? false;
        if (FilterValue == null)
            return true;
        foreach (var item in mapped)
        {
            if (FilterValue.Contains(item))
                return true;
        }
        return false;
    }
}
