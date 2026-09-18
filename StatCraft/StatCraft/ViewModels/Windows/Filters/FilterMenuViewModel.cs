using StatCraft.Models.GameData;
using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.CollatedFilters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StatCraft.ViewModels.Windows.Filters;

public class FilterMenuViewModel : ViewModelBase
{
    public event Action? FiltersChanged;

    public IReadOnlyList<FilterMenuItemViewModel<GameData>> FilterSlots { get; }
    public IEnumerable<IFilterSlotViewModel> AppliedFilterSlots => FilterSlots.SelectMany(i => i.ContainedFilters).Where(s => s.IsApplied);

    public FilterMenuViewModel(IReadOnlyList<FilterMenuItemViewModel<GameData>> filters)
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

    public AndFilter<GameData> GetFilter()
    {
        List<IFilter<GameData>> appliedFilters = new List<IFilter<GameData>>();
        foreach (var filterMenuItem in FilterSlots)
        {
            foreach (var filterSlot in filterMenuItem.ContainedFilters)
            {
                if (filterSlot.IsApplied)
                    appliedFilters.Add(filterSlot.GetFilter());
            }
        }

        return new AndFilter<GameData>(appliedFilters);
    }
}
