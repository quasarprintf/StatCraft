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
    public event Action? OtherFiltersChanged;

    public IReadOnlyList<FilterMenuItemViewModel<GameData>> ExtraFilterSlots { get; }
    public IEnumerable<IFilterSlotViewModel> VisibleExtraFilterSlots => ExtraFilterSlots.SelectMany(i => i.ContainedFilters).Where(s => s.IsApplied);

    public FilterMenuViewModel(IReadOnlyList<FilterMenuItemViewModel<GameData>> filters)
    {
        ExtraFilterSlots = filters;
        foreach (IFilterMenuItemViewModel slot in ExtraFilterSlots)
        {
            slot.IsAppliedChanged += (_,_) =>
            {
                OnPropertyChanged(nameof(VisibleExtraFilterSlots));
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
            OtherFiltersChanged?.Invoke();
        };
    }

    public AndFilter<GameData> GetFilter()
    {
        List<IFilter<GameData>> appliedFilters = new List<IFilter<GameData>>();
        foreach (var filterMenuItem in ExtraFilterSlots)
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
