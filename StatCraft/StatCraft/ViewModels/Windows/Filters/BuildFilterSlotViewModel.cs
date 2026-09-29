using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Attributes;
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
    bool IncludeDescendants { get; set; }
    BuildNode Build { get; }

    void Rebuild(BuildNode build);
}

public sealed partial class BuildFilterSlotViewModel : FilterSlotViewModel<GameData,GamePlayer?>, IBuildFilterSlotViewModel
{
    [ObservableProperty] public partial ObservableCollection<AttributeFilterSlotViewModel<BuildDetailValues>> BuildDetailSlots { get; private set; }
    [ObservableProperty] public partial bool IncludeDescendants { get; set; } = true;
    public BuildNode Build { get; private set; }

    internal BuildFilterSlotViewModel(BuildNode build, Func<GameData,GamePlayer?> filteredPropertyMap) : base(build.Name, filteredPropertyMap)
    {
        Build = build;
        BuildDetailSlots = new ObservableCollection<AttributeFilterSlotViewModel<BuildDetailValues>>();
        foreach (var detail in build.Details)
        {
            BuildDetailSlots.Add(CreateFilterSlot(detail));
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

        IFilter<int> buildIdFilter;
        if (IncludeDescendants)
        {
            buildIdFilter = new SetMemberFilter<int, int>(b=>b)
            {
                AcceptNull = false,
                FilterValue = Build.EnumerateDescendants().Select(b => b.Id).Append(Build.Id).ToHashSet()
            };
        }
        else
        {
            buildIdFilter = new ComparableFilter<int, int>(b=>b)
            {
                FilterValue = Build.Id,
                AcceptNull = false
            }.SetMatchExact();
        }
        SequentialAnyFilter<GameData, int> buildDefinedFilter = new SequentialAnyFilter<GameData, int>(buildIdFilter, g => FilteredPropertyMap(g)?.BuildIds);

        return new AndFilter<GameData>([detailFilters, buildDefinedFilter]);
    }

    public void Rebuild(BuildNode build)
    {
        Title = build.Name;
        Build = build;
        var newSlots = new ObservableCollection<AttributeFilterSlotViewModel<BuildDetailValues>>();
        foreach (var newDetail in build.Details)
        {
            var existing = BuildDetailSlots.FirstOrDefault(s => s.Attribute.Id == newDetail.Id);
            if (existing != null)
            {
                existing.RefreshDefinition(newDetail);
                newSlots.Add(existing);
            }
            else
            {
                newSlots.Add(CreateFilterSlot(newDetail));
            }
        }
        foreach (var oldDetail in BuildDetailSlots.Except(newSlots))
        {
            oldDetail.Changed -= RaiseChanged;
            oldDetail.PropertyChanged -= ForwardPropertyChanged;
        }
        BuildDetailSlots = newSlots;
    }

    partial void OnIncludeDescendantsChanged(bool value) => RaiseChanged();

    private AttributeFilterSlotViewModel<BuildDetailValues> CreateFilterSlot(AttributeDefinition definition)
    {
        var detailSlot = new AttributeFilterSlotViewModel<BuildDetailValues>(definition)
        {
            AllowIncludeUnset = true
        };
        detailSlot.Changed += RaiseChanged;
        detailSlot.PropertyChanged += ForwardPropertyChanged;
        return detailSlot;
    }

    public override void Clear()
    {
        IncludeDescendants = true;
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
