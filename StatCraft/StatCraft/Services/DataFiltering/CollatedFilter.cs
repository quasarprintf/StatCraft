using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;

namespace StatCraft.Services.DataFiltering;

public abstract partial class CollatedFilter<T> : IFilter<T>
{
    public bool AcceptNull { get; set; }
    public IReadOnlyCollection<IFilter<T>> Filters { get; set; }
    public CollatedFilter(IReadOnlyCollection<IFilter<T>> filters)
    {
        Filters = filters;
    }
    public CollatedFilter()
    {
        Filters = new List<IFilter<T>>();
    }

    public abstract bool MatchesFilter(T candidate, bool? acceptNullOverride = null);
}
