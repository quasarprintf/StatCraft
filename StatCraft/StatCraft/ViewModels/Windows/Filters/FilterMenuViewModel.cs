using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.CollatedFilters;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace StatCraft.ViewModels.Windows.Filters;

public interface IFilterMenuViewModel : INotifyPropertyChanged
{
    event Action? FiltersChanged;
    IReadOnlyList<IFilterMenuItemViewModel> MenuItems { get; }
    IEnumerable<IFilterSlotViewModel> AppliedFilters { get; }
}

public class FilterMenuViewModel<T> : ViewModelBase, IFilterMenuViewModel
{
    public event Action? FiltersChanged;

    IReadOnlyList<IFilterMenuItemViewModel> IFilterMenuViewModel.MenuItems => FilterSlots;
    public IReadOnlyList<FilterMenuItemViewModel<T>> FilterSlots { get; }
    public IEnumerable<IFilterSlotViewModel> AppliedFilters => FilterSlots.SelectMany(i => i.ContainedFilters).Where(s => s.IsApplied);

    public FilterMenuViewModel(IReadOnlyList<FilterMenuItemViewModel<T>> filters)
    {
        FilterSlots = filters;
        foreach (IFilterMenuItemViewModel slot in FilterSlots)
        {
            slot.IsAppliedChanged += (_,_) =>
            {
                OnPropertyChanged(nameof(AppliedFilters));
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
