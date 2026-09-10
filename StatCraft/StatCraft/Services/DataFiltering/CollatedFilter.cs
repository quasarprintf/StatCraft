using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public abstract class CollatedFilter<T,F> : IFilter<T>
{
    public bool? AcceptNull { get; set; }
    public IReadOnlyCollection<IFilter<F>> Filters { get; set; }
    protected Func<T,F> _filteredPropertyMap;
    public CollatedFilter(IReadOnlyCollection<IFilter<F>> filters, Func<T,F> filteredPropertyMap)
    {
        Filters = filters;
        _filteredPropertyMap = filteredPropertyMap;
    }
    public CollatedFilter(Func<T,F> filteredPropertyMap)
    {
        Filters = new List<IFilter<F>>();
        _filteredPropertyMap = filteredPropertyMap;
    }

    public abstract bool MatchesFilter(T candidate, bool? acceptNullOverride = null);
}

public abstract class CollatedFilter<T> : CollatedFilter<T,T>
{
    public CollatedFilter(IReadOnlyCollection<IFilter<T>> filters) : base(filters, x => x)
    {
    }
    public CollatedFilter() : base(x => x)
    {
    }
}
