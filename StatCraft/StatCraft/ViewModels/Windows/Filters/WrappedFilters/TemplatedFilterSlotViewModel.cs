using StatCraft.Models.GameData.Attributes;
using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.SequentialFilters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.ViewModels.Windows.Filters.WrappedFilters;

public partial class TemplatedFilterSlotViewModel<T,F> : WrappedFilterSlotViewModel<T>, IFilterSlotViewModel<T> where T : IAttributedObject
{
    public IWrappedFilter<T,F> TemplateFilter { get; set; }
    private IFilterSlotViewModel<F> _wrappedFilter => (IFilterSlotViewModel<F>)WrappedFilter;
    public TemplatedFilterSlotViewModel(IWrappedFilter<T,F> templateFilter, IFilterSlotViewModel<F> wrappedSlot)
    {
        TemplateFilter = templateFilter;
        WrappedFilter = wrappedSlot;
        base.BindWrapped();
    }

    public IFilter<T> GetFilter()
    {
        IFilter<F> filter = _wrappedFilter.GetFilter();
        TemplateFilter.Filter = filter;
        return TemplateFilter;
    }
}
