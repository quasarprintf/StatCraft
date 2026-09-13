using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Maps;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.CollatedFilters;
using StatCraft.Services.DataFiltering.SequentialFilters;
using StatCraft.Services.DataParsing;
using StatCraft.Services.Factories;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;

namespace StatCraft.ViewModels.Windows.Filters;

// Owns every filter dimension on the Data tab's filter bar. Player profile and date range are
// always visible; the other five are "extra filters" that can be added/removed via the bar's
// dropdown, each remembering its own on/off state independently of whether it currently constrains
// anything (an added-but-empty filter is inactive, same as a hidden one).
public partial class DataPageFiltersViewModel : ViewModelBase
{
    // Set while SetSingleActiveProfile is bulk-updating state on a session start, so that update
    // doesn't trigger its own reload — the caller (DataPageViewModel.SetActiveProfile) always issues
    // exactly one explicit reload right afterward.
    private bool _suppressChangeEvents;

    private FilterSlotFactory _filterSlotFactory;

    public CheckboxFilterSlotViewModel<Sc2Profile, Sc2Profile> ProfileSlot { get; }

    // DateTime (not DateTimeOffset) because Calendar.SelectedDate — which CompactDatePicker wraps —
    // is DateTime?.
    [ObservableProperty] private DateTime? _fromDate;
    [ObservableProperty] private DateTime? _toDate;

    public CheckboxFilterSlotViewModel<GameData, Map> MapSlot { get; }
    public CheckboxFilterSlotViewModel<GameData, (Race, Race)> MatchupSlot { get; }
    // Internal, not public, because GameOutcome itself is internal — this stays consistent with the
    // same-assembly-only visibility of the type it filters on.
    public CheckboxFilterSlotViewModel<GameData, GameOutcome> OutcomeSlot { get; }
    public NumericRangeFilterSlotViewModel<PlayerMmr> MmrSlot { get; }
    public CheckboxFilterSlotViewModel<GameData, BuildNode> BuildSlot { get; }
    public ObservableCollection<FilterMenuItemViewModel<GameData>> GameAttributeSlots { get; private set; }

    // Fixed display order for both the bar itself and the "+ Filters" add-dropdown.
    public IReadOnlyList<IFilterMenuItemViewModel> ExtraFilterSlots { get; }
    public IEnumerable<IFilterSlotViewModel> VisibleExtraFilterSlots => ExtraFilterSlots.SelectMany(i => i.ContainedFilters).Where(s => s.IsApplied);
    public IEnumerable<IFilterMenuItemViewModel> HiddenExtraFilterSlots => ExtraFilterSlots.Where(s => !s.IsApplied);

    // Checking/unchecking a profile changes which games need to be loaded from the database at all;
    // every other filter change only needs to re-filter the already-loaded set in memory.
    public event Action? ProfileSelectionChanged;
    public event Action? OtherFiltersChanged;

    internal DataPageFiltersViewModel(BuildRepository buildRepository, ObservableCollection<AttributeDefinition> gameAttributes, FilterSlotFactory filterSlotFactory)
    {
        _filterSlotFactory = filterSlotFactory;

        ProfileSlot = new CheckboxFilterSlotViewModel<Sc2Profile, Sc2Profile>("Profile", [], p => [p], showSearch: true);
        // Checking/unchecking a profile requires a database reload, unlike every other checkbox
        // filter, so it's wired to ProfileSelectionChanged instead of joining the ExtraFilterSlots
        // loop below (which is also how it stays permanently visible, with no Add/Remove).
        ProfileSlot.Changed += () =>
        {
            if (!_suppressChangeEvents)
                ProfileSelectionChanged?.Invoke();
        };

        MapSlot = new CheckboxFilterSlotViewModel<GameData, Map>("Map", [], g => [g.Map!], showSearch: true) { AllowIncludeUnset=false }; //TODO: why is map nullable?
        MatchupSlot = new CheckboxFilterSlotViewModel<GameData, (Race, Race)>("Matchup", BuildMatchupOptions(), GetGameMatchups, columns: 3) { AllowIncludeUnset=false };
        OutcomeSlot = new CheckboxFilterSlotViewModel<GameData, GameOutcome>("Outcome", BuildOutcomeOptions(), g => [g.ReplayData.Win.AsGameOutcome()]) { AllowIncludeUnset=false };
        MmrSlot = new NumericRangeFilterSlotViewModel<PlayerMmr>("Opponent MMR", m => m.Mmr) { AllowIncludeUnset=false };

        //TODO: builds filter needs to be completely redesigned
        List<BuildNode> allBuilds = buildRepository.GetAllBuilds();
        Dictionary<int, BuildNode> buildsMap = allBuilds.SelectMany(b => b.EnumerateDescendants().Append(b)).ToDictionary(b => b.Id);
        BuildSlot = new CheckboxFilterSlotViewModel<GameData, BuildNode>("Build", BuildBuildOptions(allBuilds), 
            g => g.ReplayData.Player.BuildIds.SelectMany(b => buildsMap.TryGetValue(b, out BuildNode? build) ? build.EnumerateAncestors().Append(build) : Enumerable.Empty<BuildNode>())) 
        { 
            AllowIncludeUnset=false 
        };

        GameAttributeSlots = new ObservableCollection<FilterMenuItemViewModel<GameData>>();
        foreach (var attribute in gameAttributes)
        {
            IFilterSlotViewModel<GameData> filterSlot = _filterSlotFactory.CreateFromDefinition<GameData>(attribute);
            GameAttributeSlots.Add(new FilterMenuItemViewModel<GameData>(filterSlot));
        }
        gameAttributes.CollectionChanged += GameAttributesChanged;

        ExtraFilterSlots = 
        [
            new FilterMenuItemViewModel<GameData>(MapSlot),
            new FilterMenuItemViewModel<GameData>(MatchupSlot), 
            new FilterMenuItemViewModel<GameData>(OutcomeSlot),
            new FilterMenuItemViewModel<PlayerMmr>(MmrSlot),
            new FilterMenuItemViewModel<GameData>(BuildSlot),
            new FilterMenuItemViewModel<GameData>(GameAttributeSlots, "Game Attributes")
        ];
        foreach (IFilterMenuItemViewModel slot in ExtraFilterSlots)
        {
            // Only a visibility toggle (Add/Remove) should rebuild the filter bar's own item list —
            // rebuilding on every criteria edit too would tear down and recreate the ItemsControl's
            // containers on every keystroke/checkbox click, stealing focus from whatever the user is
            // actively interacting with.
            slot.IsAppliedChanged += (_,_) =>
            {
                OnPropertyChanged(nameof(VisibleExtraFilterSlots));
                OnPropertyChanged(nameof(HiddenExtraFilterSlots));
            };
            foreach (var filter in slot.ContainedFilters)
                WireSlotChanged(filter);
        }
    }

    private void WireSlotChanged(IFilterSlotViewModel? filter)
    {
        if (filter == null)
            return;

        filter.Changed += () =>
        {
            if (!_suppressChangeEvents)
                OtherFiltersChanged?.Invoke();
        };
    }

    private FilterMenuItemViewModel<GameData>? MenuItemFor(AttributeDefinition attribute)
    {
        //TODO: look for better way to do this
        return GameAttributeSlots.FirstOrDefault(m => (m.Filter as AttributeFilterSlotViewModel<GameData>)?.Attribute.Id == attribute.Id);
    }
    internal void RenameAttributeSlot(AttributeDefinition attribute)
    {
        FilterMenuItemViewModel<GameData>? item = MenuItemFor(attribute);
        if (item?.Filter == null)
            return;

        item.Filter.Title = attribute.Name;
        item.DisplayText = attribute.Name;
    }

    //underlying slot type is tied to attribute type, needs to be rebuilt when type changes
    internal void ReplaceAttributeSlot(AttributeDefinition attribute)
    {
        FilterMenuItemViewModel<GameData>? item = MenuItemFor(attribute);
        if (item == null)
            return;

        int index = GameAttributeSlots.IndexOf(item);
        IFilterSlotViewModel<GameData> replacement = _filterSlotFactory.CreateFromDefinition<GameData>(attribute);
        replacement.IsApplied = item.Filter?.IsApplied ?? false;
        WireSlotChanged(replacement);
        GameAttributeSlots[index] = new FilterMenuItemViewModel<GameData>(replacement);
    }

    // A Values slot builds its checkbox list when it's created, so options added or removed afterwards
    // have to be patched in.
    internal void RefreshAttributeSlotOptions(AttributeDefinition attribute)
    {
        (MenuItemFor(attribute)?.Filter as AttributeFilterSlotViewModel<GameData>)?.Refresh();
    }

    partial void OnFromDateChanged(DateTime? value)
    {
        if (!_suppressChangeEvents)
            OtherFiltersChanged?.Invoke();
    }

    partial void OnToDateChanged(DateTime? value)
    {
        if (!_suppressChangeEvents)
            OtherFiltersChanged?.Invoke();
    }

    // Rebuilds the profile checkbox list (e.g. after linking a new account), preserving checked
    // state by profile id across the rebuild.
    internal void RefreshProfileOptions(IReadOnlyList<Sc2Profile> profiles)
    {
        HashSet<int> previouslyChecked = ProfileSlot.Options.Where(o => o.IsChecked).Select(o => o.Value.Id).ToHashSet();

        IEnumerable<CheckboxFilterOptionViewModel<Sc2Profile>> newOptions = profiles
            .Select(p => new CheckboxFilterOptionViewModel<Sc2Profile>(p, p.DisplayName) { IsChecked = previouslyChecked.Contains(p.Id) });
        ProfileSlot.ReplaceOptions(newOptions);
    }

    // Rebuilds the map filter's option list from the currently-loaded games' distinct map names,
    // preserving checked state by map name across the rebuild.
    internal void RefreshMapOptions(IEnumerable<Map> distinctMapNames)
    {
        HashSet<int> previouslyChecked = MapSlot.Options.Where(o => o.IsChecked).Select(o => o.Value.Id).ToHashSet();

        IEnumerable<CheckboxFilterOptionViewModel<Map>> newOptions = distinctMapNames
            .OrderBy(m => m.Name)
            .Select(m => new CheckboxFilterOptionViewModel<Map>(m, m.Name) { IsChecked = previouslyChecked.Contains(m.Id) });
        MapSlot.ReplaceOptions(newOptions);
    }

    private void GameAttributesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            for (int i = 0; i < e.NewItems.Count; ++i) 
            {
                AttributeDefinition attribute = (AttributeDefinition)e.NewItems[i]!;
                IFilterSlotViewModel<GameData> filterSlot = _filterSlotFactory.CreateFromDefinition<GameData>(attribute);
                WireSlotChanged(filterSlot);
                GameAttributeSlots.Insert(i + e.NewStartingIndex, new FilterMenuItemViewModel<GameData>(filterSlot));
            }
        }
        if (e.OldItems != null)
        {
            for (int i = 0; i < e.OldItems.Count; ++i) 
            {
                GameAttributeSlots.RemoveAt(e.OldStartingIndex);
            }
        }
    }

    // Collapses the profile filter to just the given profile and resets the date range to today —
    // called every time a session starts. Deliberately silent: the caller always follows this with
    // its own single explicit reload, so no intermediate event should fire here.
    internal void SetSingleActiveProfile(Sc2Profile profile)
    {
        _suppressChangeEvents = true;
        try
        {
            List<CheckboxFilterOptionViewModel<Sc2Profile>> options = ProfileSlot.Options.ToList();
            if (options.All(o => o.Value.Id != profile.Id))
            {
                options.Add(new CheckboxFilterOptionViewModel<Sc2Profile>(profile, profile.DisplayName));
                ProfileSlot.ReplaceOptions(options);
            }

            foreach (CheckboxFilterOptionViewModel<Sc2Profile> option in ProfileSlot.Options)
                option.IsChecked = option.Value.Id == profile.Id;

            DateTime today = DateTime.Today;
            FromDate = today;
            ToDate = today;
        }
        finally
        {
            _suppressChangeEvents = false;
        }
    }

    public AndFilter<GameData> GetFilter()
    {
        List<IFilter<GameData>> appliedFilters = new List<IFilter<GameData>>();
        DateTimeFilter<GameData> fromDateFilter = new DateTimeFilter<GameData>(g => g.ReplayData.ReplayTimestamp.ToLocalTime().DateTime.Date)
        {
            FilterValue = FromDate == null ? null : FromDate.Value.Date
        }.SetMatchLowerBound();
        DateTimeFilter<GameData> toDateFilter = new DateTimeFilter<GameData>(g => g.ReplayData.ReplayTimestamp.ToLocalTime().DateTime.Date)
        {
            FilterValue = ToDate == null ? null : ToDate.Value.Date
        }.SetMatchUpperBound();
        AndFilter<GameData> dateFilter = new AndFilter<GameData>([fromDateFilter, toDateFilter]);
        appliedFilters.Add(dateFilter);

        if (MapSlot.IsApplied)
            appliedFilters.Add(MapSlot.GetFilter());
        if (MatchupSlot.IsApplied)
            appliedFilters.Add(MatchupSlot.GetFilter());
        if (OutcomeSlot.IsApplied)
            appliedFilters.Add(OutcomeSlot.GetFilter());

        if (MmrSlot.IsApplied)
        {
            AndFilter<PlayerMmr> singleMmrFilter = MmrSlot.GetFilter();
            SequentialAnyFilter<GameData, PlayerMmr> mmrFilter = new SequentialAnyFilter<GameData, PlayerMmr>(singleMmrFilter, g => g.ReplayData.Opponents.Select(o => o.Mmr));
            appliedFilters.Add(mmrFilter);
        }

        if (BuildSlot.IsApplied)
        {
            Dictionary<int, BuildNode> allBuilds = BuildSlot.Options.ToDictionary(o => o.Value.Id, o => o.Value);
            SequentialAnyFilter<GameData, BuildNode> buildFilter = BuildSlot.GetFilter();
            appliedFilters.Add(buildFilter);
        }

        foreach (var attributeMenu in GameAttributeSlots)
        {
            foreach (var attributeFilterSlot in attributeMenu.ContainedFilters)
            {
                if (attributeFilterSlot.IsApplied)
                {
                    IFilter<GameData> attributeFilter = attributeFilterSlot.GetFilter();
                    //var wrappedFilter = new SequentialAllFilter<GameData, AttributeValue>(attributeFilter, g => [g.GetAttributeByDefinitionId(attribute.Id)]);
                    appliedFilters.Add(attributeFilter);
                }
            }
        }
        //TODO: build attribute filter

        AndFilter<GameData> collatedFilter = new AndFilter<GameData>(appliedFilters);

        return collatedFilter;
    }

    private (Race,Race)[] GetGameMatchups(GameData game)
    {
        return game.ReplayData.Opponents.Select(o => (game.ReplayData.Player.Race.AsRace()!.Value, o.Race.AsRace()!.Value)).Distinct().ToArray();
    }

    private static IReadOnlySet<int> ToBuildIdSet(CheckboxFilterSlotViewModel<GameData, BuildNode> slot) =>
        slot.Options
            .Where(o => o.IsChecked)
            .SelectMany(o => GameDataFilter.CollectSubtreeIds(o.Value))
            .ToHashSet();

    private static List<CheckboxFilterOptionViewModel<(Race, Race)>> BuildMatchupOptions()
    {
        List<CheckboxFilterOptionViewModel<(Race, Race)>> options = new();
        foreach (Race opponentRace in Enum.GetValues<Race>())
            foreach (Race playerRace in Enum.GetValues<Race>())
                options.Add(new CheckboxFilterOptionViewModel<(Race, Race)>((playerRace, opponentRace), $"{playerRace.Display()}v{opponentRace.Display()}"));
        return options;
    }

    private static List<CheckboxFilterOptionViewModel<GameOutcome>> BuildOutcomeOptions() =>
        Enum.GetValues<GameOutcome>()
            .Select(outcome => new CheckboxFilterOptionViewModel<GameOutcome>(outcome, outcome.ToString()))
            .ToList();

    // Every build across every race, grouped by race (Z, T, P) and flattened depth-first with an
    // indentation prefix so the tree structure is still legible in a flat checkbox list.
    private static List<CheckboxFilterOptionViewModel<BuildNode>> BuildBuildOptions(List<BuildNode> allNodes)
    {
        List<CheckboxFilterOptionViewModel<BuildNode>> options = new();
        foreach (Race race in Enum.GetValues<Race>())
            foreach (BuildNode root in allNodes.Where(n => n.PlayerRace == race))
                AddBuildOption(root, 0, options);
        return options;
    }

    private static void AddBuildOption(BuildNode node, int depth, List<CheckboxFilterOptionViewModel<BuildNode>> options)
    {
        string label = depth == 0 ? $"{node.PlayerRace.Display()} — {node.Name}" : new string(' ', depth * 2) + node.Name;
        options.Add(new CheckboxFilterOptionViewModel<BuildNode>(node, label));
        foreach (BuildNode child in node.Children)
            AddBuildOption(child, depth + 1, options);
    }
}
