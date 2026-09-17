using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.Services.DataFiltering.SequentialFilters;

public abstract class SequentialFilter<T,F> : IWrappedFilter<T,F>
{
    public bool? AcceptNull { get; set; }
    public IFilter<F> Filter { get; set; }
    protected Func<T,IEnumerable<F?>?> _filteredPropertyMap;
    public SequentialFilter(IFilter<F> filter, Func<T,IEnumerable<F?>?> filteredPropertyMap)
    {
        Filter = filter;
        _filteredPropertyMap = filteredPropertyMap;
    }

    public abstract bool MatchesFilter(T? candidate, bool? acceptNullOverride = null);
}
