using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.Services.DataFiltering.SequentialFilters;

public class SequentialAnyFilter<T,F> : SequentialFilter<T,F>
{
    public SequentialAnyFilter(IFilter<F> filter, Func<T,IEnumerable<F?>?> filteredPropertyMap) : base(filter, filteredPropertyMap)
    {
    }

    public override bool MatchesFilter(T? candidate, bool? acceptNullOverride = null)
    {
        if (candidate == null)
            return false;
        IEnumerable<F?>? mapped = _filteredPropertyMap(candidate);
        if (mapped == null || !mapped.Any())
            return acceptNullOverride ?? AcceptNull ?? false;
        return mapped.Any(m => Filter.MatchesFilter(m, acceptNullOverride ?? AcceptNull));
    }
}
