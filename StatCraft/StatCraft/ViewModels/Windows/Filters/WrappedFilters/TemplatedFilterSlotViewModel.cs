using StatCraft.Services.DataFiltering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.ViewModels.Windows.Filters.WrappedFilters;

public partial class TemplatedFilterSlotViewModel<T,F> : WrappedFilterSlotViewModel<T>, IFilterSlotViewModel<T>
{
    private Func<IFilter<F>, IWrappedFilter<T,F>> FilterTemplate { get; set; }
    private IFilterSlotViewModel<F> _wrappedFilter => (IFilterSlotViewModel<F>)WrappedFilter;
    public TemplatedFilterSlotViewModel(Func<IFilter<F>, IWrappedFilter<T,F>> filterTemplate, IFilterSlotViewModel<F> wrappedSlot)
    {
        FilterTemplate = filterTemplate;
        WrappedFilter = wrappedSlot;
        base.BindWrapped();
    }

    public IFilter<T> GetFilter()
    {
        return FilterTemplate(_wrappedFilter.GetFilter());
    }
}
