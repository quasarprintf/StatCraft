using StatCraft.Services.DataFiltering;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace StatCraft.ViewModels.Windows.Filters.WrappedFilters;

public partial class TemplatedFilterSlotViewModel<T,F> : WrappedFilterSlotViewModel<T>, IFilterSlotViewModel<T>
{
    private Func<IFilter<F>, IFilter<T>> FilterTemplate { get; set; }
    private IFilterSlotViewModel<F> _wrappedFilter => (IFilterSlotViewModel<F>)WrappedFilter;

    [SetsRequiredMembers]
    public TemplatedFilterSlotViewModel(Func<IFilter<F>, IFilter<T>> filterTemplate, IFilterSlotViewModel<F> wrappedSlot)
    {
        FilterTemplate = filterTemplate;
        WrappedFilter = wrappedSlot;
    }

    public IFilter<T> GetFilter()
    {
        return FilterTemplate(_wrappedFilter.GetFilter());
    }
}
