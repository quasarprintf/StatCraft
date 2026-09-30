using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Maps;
using StatCraft.Models.GameData.Race;
using StatCraft.Models.GameData.Replays;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataFiltering.CollatedFilters;
using StatCraft.Services.DataFiltering.SequentialFilters;
using StatCraft.Services.DataParsing;
using StatCraft.ViewModels.Windows.Filters;
using StatCraft.ViewModels.Windows.Filters.WrappedFilters;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace StatCraft.ViewModels.Windows.DataComponents;

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

    public CheckboxFilterSlotViewModel<GameData, int> ProfileSlot { get; }

    public DateRangeFilterSlotViewModel<GameData> DateSlot { get; }
    public CheckboxFilterSlotViewModel<GameData, Map> MapSlot { get; }
    public CheckboxFilterSlotViewModel<GameData, (Race, Race)> MatchupSlot { get; }
    public CheckboxFilterSlotViewModel<GameData, GameOutcome> OutcomeSlot { get; }
    public TemplatedFilterSlotViewModel<GameData,PlayerMmr> MmrSlot { get; }
    public ObservableCollection<FilterMenuItemViewModel<GameData>> BuildMatchupSlots { get; }
    private Dictionary<int, BuildFilterSlotViewModel> _buildFilters;
    public ObservableCollection<FilterMenuItemViewModel<GameData>> GameAttributeSlots { get; private set; }

    public FilterMenuViewModel<GameData> FilterMenu { get; private set; }


    // Checking/unchecking a profile changes which games need to be loaded from the database at all;
    // every other filter change only needs to re-filter the already-loaded set in memory.
    public event Action? ProfileSelectionChanged;
    public event Action? OtherFiltersChanged;

    internal DataPageFiltersViewModel(BuildRepository buildRepository, ObservableCollection<AttributeDefinition> gameAttributes)
    {
        ProfileSlot = new CheckboxFilterSlotViewModel<GameData, int>("Profile", [], g => [g.Sc2ProfileId], showSearch: true)
        {
            Mandatory = true
        };
        // Checking/unchecking a profile requires a database reload
        ProfileSlot.Changed += () =>
        {
            if (!_suppressChangeEvents)
                ProfileSelectionChanged?.Invoke();
        };

        DateSlot = new DateRangeFilterSlotViewModel<GameData>("Date", g => g.ReplayData.ReplayTimestamp.ToLocalTime().Date)
        {
            FromDate = DateTime.Today,
            ToDate = DateTime.Today,
            Mandatory = true
        };

        MapSlot = new CheckboxFilterSlotViewModel<GameData, Map>("Map", [], g => [g.ReplayData.Map], showSearch: true) { AllowIncludeUnset=false };
        MatchupSlot = new CheckboxFilterSlotViewModel<GameData, (Race, Race)>("Matchup", BuildMatchupOptions(), GetGameMatchups, columns: 3) { AllowIncludeUnset=false };
        OutcomeSlot = new CheckboxFilterSlotViewModel<GameData, GameOutcome>("Outcome", BuildOutcomeOptions(), g => [g.ReplayData.Win.AsGameOutcome()]) { AllowIncludeUnset=false };

        var innerMmrSlot = new NumericRangeFilterSlotViewModel<PlayerMmr>("Opponent MMR", m => m.Mmr) { AllowIncludeUnset=false };
        MmrSlot = new TemplatedFilterSlotViewModel<GameData, PlayerMmr>(
            f => new SequentialAnyFilter<GameData, PlayerMmr>(f, g => g.ReplayData.Opponents.Select(o => o.Mmr)),
            innerMmrSlot)
        {
            AllowIncludeUnset=false 
        };

        _buildFilters = new Dictionary<int, BuildFilterSlotViewModel>();
        BuildMatchupSlots = new ObservableCollection<FilterMenuItemViewModel<GameData>>();
        FilterMenuItemViewModel<GameData> buildSlot = new FilterMenuItemViewModel<GameData>(BuildMatchupSlots, "Builds");
        RefreshBuildMenu(buildRepository.GetAllBuilds());

        GameAttributeSlots = new ObservableCollection<FilterMenuItemViewModel<GameData>>();
        foreach (var attribute in gameAttributes)
        {
            IFilterSlotViewModel<GameData> filterSlot = new AttributeFilterSlotViewModel<GameData>(attribute);
            GameAttributeSlots.Add(new FilterMenuItemViewModel<GameData>(filterSlot));
        }
        gameAttributes.CollectionChanged += GameAttributesChanged;

        FilterMenuItemViewModel<GameData>[] filterSlots = 
        [
            new FilterMenuItemViewModel<GameData>(ProfileSlot),
            new FilterMenuItemViewModel<GameData>(DateSlot),
            new FilterMenuItemViewModel<GameData>(MapSlot),
            new FilterMenuItemViewModel<GameData>(MatchupSlot), 
            new FilterMenuItemViewModel<GameData>(OutcomeSlot),
            new FilterMenuItemViewModel<GameData>(MmrSlot),
            buildSlot,
            new FilterMenuItemViewModel<GameData>(GameAttributeSlots, "Game Attributes")
        ];
        FilterMenu = new FilterMenuViewModel<GameData>(filterSlots);
        FilterMenu.FiltersChanged += () =>
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
    }

    //underlying slot type is tied to attribute type, needs to be rebuilt when type changes
    internal void ReplaceAttributeSlot(AttributeDefinition attribute)
    {
        FilterMenuItemViewModel<GameData>? item = MenuItemFor(attribute);
        if (item == null)
            return;

        ((AttributeFilterSlotViewModel<GameData>)item.Filter!).Rebuild();
    }

    // A Values slot builds its checkbox list when it's created, so options added or removed afterwards
    // have to be patched in.
    internal void RefreshAttributeSlotOptions(AttributeDefinition attribute)
    {
        (MenuItemFor(attribute)?.Filter as AttributeFilterSlotViewModel<GameData>)?.Refresh();
    }

    // Rebuilds the profile checkbox list (e.g. after linking a new account), preserving checked
    // state by profile id across the rebuild.
    internal void RefreshProfileOptions(IReadOnlyList<Sc2Profile> profiles)
    {
        HashSet<int> previouslyChecked = ProfileSlot.Options.Where(o => o.IsChecked).Select(o => o.Value).ToHashSet();

        IEnumerable<CheckboxFilterOptionViewModel<int>> newOptions = profiles
            .Select(p => new CheckboxFilterOptionViewModel<int>(p.Id, p.DisplayName) { IsChecked = previouslyChecked.Contains(p.Id) });
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
                IFilterSlotViewModel<GameData> filterSlot = new AttributeFilterSlotViewModel<GameData>(attribute);
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
            List<CheckboxFilterOptionViewModel<int>> options = ProfileSlot.Options.ToList();
            if (options.All(o => o.Value != profile.Id))
            {
                options.Add(new CheckboxFilterOptionViewModel<int>(profile.Id, profile.DisplayName));
                ProfileSlot.ReplaceOptions(options);
            }

            foreach (CheckboxFilterOptionViewModel<int> option in ProfileSlot.Options)
                option.IsChecked = option.Value == profile.Id;

            DateSlot.FromDate = DateTime.Today;
            DateSlot.ToDate = DateTime.Today;
        }
        finally
        {
            _suppressChangeEvents = false;
        }
    }

    public AndFilter<GameData> GetFilter()
    {
        return FilterMenu.GetFilter();
    }

    private (Race,Race)[] GetGameMatchups(GameData game)
    {
        return game.ReplayData.Opponents.Select(o => (game.ReplayData.Player.Race.AsRace()!.Value, o.Race.AsRace()!.Value)).Distinct().ToArray();
    }

    private List<CheckboxFilterOptionViewModel<(Race, Race)>> BuildMatchupOptions()
    {
        List<CheckboxFilterOptionViewModel<(Race, Race)>> options = new();
        foreach (Race opponentRace in Enum.GetValues<Race>())
            foreach (Race playerRace in Enum.GetValues<Race>())
                options.Add(new CheckboxFilterOptionViewModel<(Race, Race)>((playerRace, opponentRace), $"{playerRace.Display()}v{opponentRace.Display()}"));
        return options;
    }

    private List<CheckboxFilterOptionViewModel<GameOutcome>> BuildOutcomeOptions() =>
        Enum.GetValues<GameOutcome>()
            .Select(outcome => new CheckboxFilterOptionViewModel<GameOutcome>(outcome, outcome.ToString()))
            .ToList();

    public void RefreshBuildMenu(List<BuildNode> roots)
    {
        for (int index = BuildMatchupSlots.Count - 1; index >= 0; index--)
        {
            BuildMatchupSlots[index].DestroyRecursively();
            BuildMatchupSlots.RemoveAt(index); //do not replace this with an outside-of-loop .Clear() call. .Clear doesn't include OldItems in its notify event
        }
        foreach (Race race in Enum.GetValues<Race>())
        {
            ObservableCollection<FilterMenuItemViewModel<GameData>> raceBuilds =
                new(roots.Where(b => b.PlayerRace == race).Select(BuildMenuItem));
            BuildMatchupSlots.Add(new FilterMenuItemViewModel<GameData>(raceBuilds, race.Display()));
        }
    }

    private FilterMenuItemViewModel<GameData> BuildMenuItem(BuildNode build)
    {
        if (_buildFilters.TryGetValue(build.Id, out BuildFilterSlotViewModel? slot))
        {
            slot.Rebuild(build);
        }
        else
        {
            slot = new(build, g => g.PlayerDetails[g.ReplayData.Player]);
            _buildFilters[build.Id] = slot;
        }
        if (build.Children.Count == 0)
            return new FilterMenuItemViewModel<GameData>(slot);

        ObservableCollection<FilterMenuItemViewModel<GameData>> children = new(build.Children.Select(BuildMenuItem));
        return new FilterMenuItemViewModel<GameData>(slot, children);
    }
}
