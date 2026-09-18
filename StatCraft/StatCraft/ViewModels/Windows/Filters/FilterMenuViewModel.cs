using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.CollatedFilters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.ViewModels.Windows.Filters;

public interface IFilterMenuViewModel
{
    event Action? FiltersChanged;
    IReadOnlyList<IFilterMenuItemViewModel> FilterSlots { get; }
    IEnumerable<IFilterSlotViewModel> AppliedFilterSlots { get; }
}

public class FilterMenuViewModel<T> : ViewModelBase, IFilterMenuViewModel
{
    public event Action? FiltersChanged;

    IReadOnlyList<IFilterMenuItemViewModel> IFilterMenuViewModel.FilterSlots => FilterSlots;
    public IReadOnlyList<FilterMenuItemViewModel<T>> FilterSlots { get; }
    public IEnumerable<IFilterSlotViewModel> AppliedFilterSlots => FilterSlots.SelectMany(i => i.ContainedFilters).Where(s => s.IsApplied);

    public FilterMenuViewModel(IReadOnlyList<FilterMenuItemViewModel<T>> filters)
    {
        FilterSlots = filters;
        foreach (IFilterMenuItemViewModel slot in FilterSlots)
        {
            slot.IsAppliedChanged += (_,_) =>
            {
                OnPropertyChanged(nameof(AppliedFilterSlots));
            };
            WireSlotChanged(slot);
        }
    }
    private void WireSlotChanged(IFilterMenuItemViewModel? filter)
    {
        if (filter == null)
            return;

        filter.Changed += () =>
        {
            FiltersChanged?.Invoke();
        };
    }

    public AndFilter<T> GetFilter()
    {
        List<IFilter<T>> appliedFilters = new List<IFilter<T>>();
        foreach (var filterMenuItem in FilterSlots)
        {
            foreach (var filterSlot in filterMenuItem.ContainedFilters)
            {
                if (filterSlot.IsApplied)
                    appliedFilters.Add(filterSlot.GetFilter());
            }
        }

        return new AndFilter<T>(appliedFilters);
    }
}
