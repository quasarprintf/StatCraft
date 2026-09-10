using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public class OrFilter<T> : CollatedFilter<T>
{
    public OrFilter(IReadOnlyCollection<IFilter<T>> filters) : base(filters)
    {
    }
    public override bool MatchesFilter(T candidate, bool? acceptNullOverride = null)
    {
        if (Filters.Count == 0)
            return true;
        if (acceptNullOverride == null)
            acceptNullOverride = AcceptNull;
        return Filters.Select(f => f.MatchesFilter(candidate, acceptNullOverride)).Any(m => m);
    }
}
