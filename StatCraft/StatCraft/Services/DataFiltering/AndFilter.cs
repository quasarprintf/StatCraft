using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public class AndFilter<T,F> : CollatedFilter<T,F>
{
    public AndFilter(IReadOnlyCollection<IFilter<F>> filters, Func<T,F> filteredPropertyMap) : base(filters, filteredPropertyMap)
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
        return Filters.Select(f => f.MatchesFilter(mapped, acceptNullOverride ?? AcceptNull)).All(m => m);
    }
}

public class AndFilter<T> : AndFilter<T,T>
{
    public AndFilter(IReadOnlyCollection<IFilter<T>> filters) : base(filters, x => x)
    {
    }
}
