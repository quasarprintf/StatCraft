using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public class AndFilter<T> : CollatedFilter<T>
{
    public AndFilter(IReadOnlyCollection<IFilter<T>> filters) : base(filters)
    {
    }
    public override bool MatchesFilter(T candidate, bool? acceptNullOverride = null)
    {
        if (acceptNullOverride == null)
            acceptNullOverride = AcceptNull;
        return Filters.Select(f => f.MatchesFilter(candidate, acceptNullOverride)).All(m => m);
    }
}
