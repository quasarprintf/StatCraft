using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.CollatedFilters;
using StatCraft.Services.DataFiltering.SequentialFilters;
using StatCraft.ViewModels.Windows.Filters.WrappedFilters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace StatCraft.ViewModels.Windows.Filters;

public interface IBuildFilterSlotViewModel : IFilterSlotViewModel
{
    ObservableCollection<AttributeFilterSlotViewModel<BuildDetailValues>> BuildDetailSlots { get; }
}

public sealed partial class BuildFilterSlotViewModel : FilterSlotViewModel<GameData,GamePlayer?>, IBuildFilterSlotViewModel
{
    public ObservableCollection<AttributeFilterSlotViewModel<BuildDetailValues>> BuildDetailSlots { get; private set; }
    private int buildId { get; set; }

    internal BuildFilterSlotViewModel(string title, BuildNode build, Func<GameData,GamePlayer?> filteredPropertyMap) : base(title, filteredPropertyMap)
    {
        buildId = build.Id;
        BuildDetailSlots = new ObservableCollection<AttributeFilterSlotViewModel<BuildDetailValues>>();
        foreach (var detail in build.Details)
        {
            var detailSlot = new AttributeFilterSlotViewModel<BuildDetailValues>(detail);
            BuildDetailSlots.Add(detailSlot);
            detailSlot.Changed += RaiseChanged;
            detailSlot.PropertyChanged += ForwardPropertyChanged;
        }
    }

    public override IFilter<GameData> GetFilter()
    {
        IFilter<BuildDetailValues>[] filters = new IFilter<BuildDetailValues>[BuildDetailSlots.Count];
        for (int i = 0; i < filters.Length; i++)
        {
            filters[i] = BuildDetailSlots[i].GetFilter();
        }
        var detailFilters = new AndFilter<GameData, BuildDetailValues>(filters, g => FilteredPropertyMap(g)?.BuildDetailValues);

        ComparableFilter<int,int> buildIdFilter = new ComparableFilter<int, int>(b=>b)
        {
            FilterValue = buildId
        }.SetMatchExact();
        SequentialAnyFilter<GameData, int> buildDefinedFilter = new SequentialAnyFilter<GameData, int>(buildIdFilter, g => FilteredPropertyMap(g)?.BuildIds);

        return new AndFilter<GameData>([detailFilters, buildDefinedFilter]);
    }

    public override void Clear()
    {
        foreach (var slot in BuildDetailSlots)
        {
            slot.Clear();
        }
    }

    private void ForwardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}
