using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.CollatedFilters;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace StatCraft.ViewModels.Windows.Filters;

public interface IFilterMenuViewModel : INotifyPropertyChanged
{
    event Action? FiltersChanged;
    ObservableCollection<IFilterMenuItemViewModel> MenuItems { get; }
    IEnumerable<IFilterSlotViewModel> AppliedFilters { get; }
}

public class FilterMenuViewModel<T> : ViewModelBase, IFilterMenuViewModel
{
    public event Action? FiltersChanged;

    public ObservableCollection<IFilterMenuItemViewModel> MenuItems { get; set; }
    public IEnumerable<FilterMenuItemViewModel<T>> FilterSlots => MenuItems.Cast<FilterMenuItemViewModel<T>>();
    public IEnumerable<IFilterSlotViewModel> AppliedFilters => FilterSlots.SelectMany(i => i.ContainedFilters).Where(s => s.IsApplied);

    public FilterMenuViewModel(IEnumerable<FilterMenuItemViewModel<T>> filters)
    {
        MenuItems = new ObservableCollection<IFilterMenuItemViewModel>(filters);
        foreach (IFilterMenuItemViewModel slot in FilterSlots)
        {
            WireSlotChanged(slot);
        }
    }
    private void WireSlotChanged(IFilterMenuItemViewModel? filter)
    {
        if (filter == null)
            return;

        filter.IsAppliedChanged += AppliedFiltersChanged;
        filter.Changed += InvokeFiltersChanged;
    }
    private void UnWireSlotChanged(IFilterMenuItemViewModel? filter)
    {
        if (filter == null)
            return;

        filter.IsAppliedChanged -= AppliedFiltersChanged;
        filter.Changed -= InvokeFiltersChanged;
    }
    private void InvokeFiltersChanged()
    {
        FiltersChanged?.Invoke();
    }
    private void AppliedFiltersChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AppliedFilters));
    }

    public void AddFilter(FilterMenuItemViewModel<T> filter)
    {
        WireSlotChanged(filter);
        MenuItems.Add(filter);
    }
    public void RemoveFilter(FilterMenuItemViewModel<T> filter)
    {
        UnWireSlotChanged(filter);
        MenuItems.Remove(filter);
        if (filter.IsApplied)
        {
            OnPropertyChanged(nameof(AppliedFilters));
            InvokeFiltersChanged();
        }
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
