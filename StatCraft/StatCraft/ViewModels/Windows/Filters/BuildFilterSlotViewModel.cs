using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
using StatCraft.Services.BackgroundService;
using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.CollatedFilters;
using StatCraft.Services.DataFiltering.SequentialFilters;
using StatCraft.Services.DataParsing;
using StatCraft.ViewModels.Windows.DataComponents.GameRow;
using StatCraft.ViewModels.Windows.Filters.WrappedFilters;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace StatCraft.ViewModels.Windows.Filters;

public interface IBuildFilterSlotViewModel : IFilterSlotViewModel
{
    BuildSelectionSlotViewModel BuildSelection { get; }
    ObservableCollection<AttributeFilterSlotViewModel<BuildDetailValues>> BuildDetailSlots { get; }
    bool HasDetails { get; }
    bool IncludeDescendants { get; set; }
    BuildNode? Build { get; }
}

// One matchup's worth of build filtering: the user picks a build out of that matchup's tree, and the
// build they pick is both what gets filtered on and what decides which details can be filtered further.
// Until they pick one the filter constrains nothing, same as any other filter with no criteria entered.
public sealed partial class BuildFilterSlotViewModel : FilterSlotViewModel<GameData,IEnumerable<GamePlayer>>, IBuildFilterSlotViewModel
{
    public BuildSelectionSlotViewModel BuildSelection { get; }

    [NotifyPropertyChangedFor(nameof(HasDetails))]
    [ObservableProperty] public partial ObservableCollection<AttributeFilterSlotViewModel<BuildDetailValues>> BuildDetailSlots { get; private set; }
    [ObservableProperty] public partial bool IncludeDescendants { get; set; } = true;

    public BuildNode? Build => BuildSelection.SelectedBuildNode;

    // A build with no details of its own has nothing to show behind the flyout button, so it isn't shown.
    public bool HasDetails => BuildDetailSlots.Count > 0;

    // Set while Refresh is re-resolving the selection against a reloaded tree, so that selection change
    // doesn't announce itself as the user having changed the criteria — the same reason
    // AttributeFilterSlotViewModel.Rebuild stays silent: whoever reloaded the tree re-runs the filters
    // once afterwards, rather than once per filter it touched.
    private bool _refreshing;

    internal BuildFilterSlotViewModel(string title, ObservableCollection<BuildNode>? buildTree, ILogger logger,
        Func<GameData,IEnumerable<GamePlayer>> filteredPropertyMap) : base(title, filteredPropertyMap)
    {
        BuildSelection = new BuildSelectionSlotViewModel(buildTree, logger);
        BuildDetailSlots = [];
        BuildSelection.SelectionChanged += OnSelectedBuildChanged;
    }

    public override IFilter<GameData> GetFilter()
    {
        BuildNode? build = Build;
        if (build == null)
        {
            // Nothing picked yet: in the bar, but not yet asking anything of a game.
            return new AndFilter<GameData>([]);
        }

        IFilter<BuildDetailValues>[] filters = new IFilter<BuildDetailValues>[BuildDetailSlots.Count];
        for (int i = 0; i < filters.Length; i++)
        {
            filters[i] = BuildDetailSlots[i].GetFilter();
        }
        var playerDetailFilter = new AndFilter<GamePlayer, BuildDetailValues>(filters, p => p.BuildDetailValues);

        IFilter<int> buildIdFilter;
        if (IncludeDescendants)
        {
            buildIdFilter = new SetMemberFilter<int, int>(b=>b)
            {
                AcceptNull = false,
                FilterValue = build.EnumerateDescendants().Select(b => b.Id).Append(build.Id).ToHashSet()
            };
        }
        else
        {
            buildIdFilter = new ComparableFilter<int, int>(b=>b)
            {
                FilterValue = build.Id,
                AcceptNull = false
            }.SetMatchExact();
        }
        SequentialAnyFilter<GamePlayer, int> playerBuildDefinedFilter = new SequentialAnyFilter<GamePlayer, int>(buildIdFilter, p => p.BuildIds);

        var playerFinalFilter = new AndFilter<GamePlayer>([playerDetailFilter, playerBuildDefinedFilter]);
        return new SequentialAnyFilter<GameData, GamePlayer>(playerFinalFilter, FilteredPropertyMap);
    }

    // Re-resolves the picked build against the tree this filter was built from, after that tree has been
    // reloaded from the database: every BuildNode in it is a new instance, so the one the picker is
    // holding is stale, and may be gone altogether (the build was deleted on the Builds tab), which
    // clears the selection the same way the user would.
    public void Refresh()
    {
        if (Build == null)
            return;

        _refreshing = true;
        try
        {
            BuildSelection.SelectedBuildNode = BuildPathHelper.FindPath(BuildSelection.BuildTree, Build.Id)?.LastOrDefault();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void OnSelectedBuildChanged(BuildSelectionSlotViewModel selection, BuildNode? previousBuild)
    {
        RebuildDetailSlots();
        if (!_refreshing)
            RaiseChanged();
    }

    // The detail filters belong to whichever build is picked: a slot already showing for a detail the
    // newly picked build also has is kept, criteria and all, and everything else is dropped.
    private void RebuildDetailSlots()
    {
        IEnumerable<AttributeDefinition> details = Build?.Details ?? Enumerable.Empty<AttributeDefinition>();

        var newSlots = new ObservableCollection<AttributeFilterSlotViewModel<BuildDetailValues>>();
        foreach (var newDetail in details)
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
        // Takes the detail filters with it, since they only exist for the build that was picked.
        BuildSelection.SelectedBuildNode = null;
    }

    private void ForwardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}
