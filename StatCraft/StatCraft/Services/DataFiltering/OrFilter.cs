using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public class OrFilter<T,F> : CollatedFilter<T,F>
{
    public OrFilter(IReadOnlyCollection<IFilter<F>> filters, Func<T,F> filteredPropertyMap) : base(filters, filteredPropertyMap)
    {
    }
    public override bool MatchesFilter(T candidate, bool? acceptNullOverride = null)
    {
        if (candidate == null)
            return false;
        F mapped = _filteredPropertyMap(candidate);
        if (mapped == null)
            return acceptNullOverride ?? AcceptNull ?? false;
        if (Filters.Count == 0)
            return true;
        return Filters.Select(f => f.MatchesFilter(mapped, acceptNullOverride ?? AcceptNull)).Any(m => m);
    }
}

public class OrFilter<T> : OrFilter<T,T>
{
    public OrFilter(IReadOnlyCollection<IFilter<T>> filters) : base(filters, x => x)
    {
    }
}
