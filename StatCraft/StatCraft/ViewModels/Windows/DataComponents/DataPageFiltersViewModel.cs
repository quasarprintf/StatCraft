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

    // Profile and date range are mandatory as well as unique: they are added once here and can never be
    // taken out of the bar, so unlike every other filter they have a slot for the page's whole lifetime.
    public CheckboxFilterSlotViewModel<GameData, int> ProfileSlot { get; }
    public DateRangeFilterSlotViewModel<GameData> DateSlot { get; }

    // The map filter's options come from the loaded games rather than being fixed, and the filter itself
    // only exists while it is in the bar — so the maps currently worth offering are kept here, and each
    // filter the menu entry builds gets its own options made from them (see RefreshMapOptions).
    private List<Map> _mapOptions = [];
    private readonly FilterMenuItemViewModel<GameData> _mapMenuItem;

    // One menu entry per game attribute, by definition, so a rename or a retype can find the entry it
    // belongs to (and whatever has already been added from it).
    private readonly Dictionary<AttributeDefinition, FilterMenuItemViewModel<GameData>> _gameAttributeItems = new();
    // The same build tree offered once per set of players, so "I played this build" and "my opponent
    // played this build" are separate criteria rather than one shared setting.
    private readonly BuildFilterScope[] _buildScopes;
    public ObservableCollection<FilterMenuItemViewModel<GameData>> GameAttributeSlots { get; private set; }

    public FilterMenuViewModel<GameData> FilterMenu { get; private set; }


    // Checking/unchecking a profile changes which games need to be loaded from the database at all;
    // every other filter change only needs to re-filter the already-loaded set in memory.
    public event Action? ProfileSelectionChanged;
    public event Action? OtherFiltersChanged;

    internal DataPageFiltersViewModel(BuildRepository buildRepository, ObservableCollection<AttributeDefinition> gameAttributes)
    {
        FilterMenuItemViewModel<GameData> profileMenuItem = new FilterMenuItemViewModel<GameData>(() => 
            new CheckboxFilterSlotViewModel<GameData, int>("Profile", [], g => [g.Sc2ProfileId], showSearch: true)
            {
                Mandatory = true
            }, "Profile"
        )
        {
            IsUnique = true
        };
        profileMenuItem.Add();
        // Checking/unchecking a profile requires a database reload
        ProfileSlot = (CheckboxFilterSlotViewModel<GameData, int>)profileMenuItem.AppliedFilters[0];
        ProfileSlot.Changed += () =>
        {
            if (!_suppressChangeEvents)
                ProfileSelectionChanged?.Invoke();
        };

        FilterMenuItemViewModel<GameData> dateMenuItem = new FilterMenuItemViewModel<GameData>(() =>
            new DateRangeFilterSlotViewModel<GameData>("Date", g => g.ReplayData.ReplayTimestamp.ToLocalTime().Date)
            {
                FromDate = DateTime.Today,
                ToDate = DateTime.Today,
                Mandatory = true
            }, "Date"
        )
        {
            IsUnique = true
        };
        dateMenuItem.Add();
        DateSlot = (DateRangeFilterSlotViewModel<GameData>)dateMenuItem.AppliedFilters[0];

        // A game has one map, one profile and one date, so asking about any of them twice over says
        // nothing new — those three are unique. Everything else can be asked of more than once.
        _mapMenuItem = new FilterMenuItemViewModel<GameData>(() =>
            new CheckboxFilterSlotViewModel<GameData, int>("Map", MapOptions(), g => [g.ReplayData.Map.Id], showSearch: true) { AllowIncludeUnset = false },
            "Map"
        )
        {
            IsUnique = true
        };

        FilterMenuItemViewModel<GameData> matchupMenuItem = new FilterMenuItemViewModel<GameData>(() =>
            new CheckboxFilterSlotViewModel<GameData, (Race, Race)>("Matchup", BuildMatchupOptions(), GetGameMatchups, columns: 3) { AllowIncludeUnset = false },
            "Matchup");

        FilterMenuItemViewModel<GameData> outcomeMenuItem = new FilterMenuItemViewModel<GameData>(() =>
            new CheckboxFilterSlotViewModel<GameData, GameOutcome>("Outcome", BuildOutcomeOptions(), g => [g.ReplayData.Win.AsGameOutcome()]) { AllowIncludeUnset = false },
            "Outcome");

        FilterMenuItemViewModel<GameData> mmrMenuItem = new FilterMenuItemViewModel<GameData>(() =>
            new TemplatedFilterSlotViewModel<GameData, PlayerMmr>(
                f => new SequentialAnyFilter<GameData, PlayerMmr>(f, g => g.ReplayData.Opponents.Select(o => o.Mmr)),
                new NumericRangeFilterSlotViewModel<PlayerMmr>("Opponent MMR", m => m.Mmr) { AllowIncludeUnset = false })
            {
                AllowIncludeUnset = false
            }, "Opponent MMR");

        _buildScopes =
        [
            new BuildFilterScope("My Build", g => [g.PlayerDetails[g.ReplayData.Player]]),
            new BuildFilterScope("Allies Build", g => g.ReplayData.Allies.Select(a => g.PlayerDetails[a])),
            new BuildFilterScope("Opponents Build", g => g.ReplayData.Opponents.Select(o => g.PlayerDetails[o])),
        ];
        RefreshBuildMenu(buildRepository.GetAllBuilds());

        GameAttributeSlots = new ObservableCollection<FilterMenuItemViewModel<GameData>>();
        foreach (AttributeDefinition attribute in gameAttributes)
            GameAttributeSlots.Add(GameAttributeMenuItem(attribute));
        gameAttributes.CollectionChanged += GameAttributesChanged;

        FilterMenuItemViewModel<GameData>[] filterSlots = 
        [
            profileMenuItem,
            dateMenuItem,
            _mapMenuItem,
            matchupMenuItem,
            outcomeMenuItem,
            mmrMenuItem,
            .. _buildScopes.Select(scope => scope.MenuItem),
            new FilterMenuItemViewModel<GameData>(GameAttributeSlots, "Game Attributes")
        ];
        FilterMenu = new FilterMenuViewModel<GameData>(filterSlots);
        FilterMenu.FiltersChanged += () =>
        {
            if (!_suppressChangeEvents)
                OtherFiltersChanged?.Invoke();
        };
    }

    // The factory closes over the definition, so an entry's future filters always read the current name
    // and type; these three only have to catch up whatever has already been added from it.
    private FilterMenuItemViewModel<GameData> GameAttributeMenuItem(AttributeDefinition attribute)
    {
        FilterMenuItemViewModel<GameData> item = new FilterMenuItemViewModel<GameData>(
            () => new AttributeFilterSlotViewModel<GameData>(attribute), attribute.Name);
        _gameAttributeItems[attribute] = item;
        return item;
    }

    private IEnumerable<AttributeFilterSlotViewModel<GameData>> AppliedAttributeSlots(AttributeDefinition attribute) =>
        _gameAttributeItems.TryGetValue(attribute, out FilterMenuItemViewModel<GameData>? item)
            ? item.AppliedFilters.OfType<AttributeFilterSlotViewModel<GameData>>().ToList()
            : [];

    internal void RenameAttributeSlot(AttributeDefinition attribute)
    {
        if (_gameAttributeItems.TryGetValue(attribute, out FilterMenuItemViewModel<GameData>? item))
            item.Rename(attribute.Name);
    }

    //underlying slot type is tied to attribute type, needs to be rebuilt when type changes
    internal void ReplaceAttributeSlot(AttributeDefinition attribute)
    {
        foreach (AttributeFilterSlotViewModel<GameData> applied in AppliedAttributeSlots(attribute))
            applied.Rebuild();
    }

    // A Values slot builds its checkbox list when it's created, so options added or removed afterwards
    // have to be patched in.
    internal void RefreshAttributeSlotOptions(AttributeDefinition attribute)
    {
        foreach (AttributeFilterSlotViewModel<GameData> applied in AppliedAttributeSlots(attribute))
            applied.Refresh();
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

    // Rebuilds the map filter's option list from the currently-loaded games' maps, preserving checked
    // state by map id across the rebuild — both for the filter currently in the bar, if there is one, and
    // for whichever one the menu entry builds next.
    internal void RefreshMapOptions(IEnumerable<Map> distinctMaps)
    {
        _mapOptions = distinctMaps.OrderBy(m => m.Name).ToList();

        CheckboxFilterSlotViewModel<GameData, int>? applied = AppliedMapSlot;
        if (applied == null)
            return;

        HashSet<int> previouslyChecked = applied.Options.Where(o => o.IsChecked).Select(o => o.Value).ToHashSet();
        applied.ReplaceOptions(MapOptions().Select(o => { o.IsChecked = previouslyChecked.Contains(o.Value); return o; }));
    }

    private List<CheckboxFilterOptionViewModel<int>> MapOptions() =>
        _mapOptions.Select(m => new CheckboxFilterOptionViewModel<int>(m.Id, m.Name)).ToList();

    private CheckboxFilterSlotViewModel<GameData, int>? AppliedMapSlot =>
        _mapMenuItem.AppliedFilters.OfType<CheckboxFilterSlotViewModel<GameData, int>>().FirstOrDefault();

    private void GameAttributesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            for (int i = 0; i < e.NewItems.Count; ++i) 
            {
                AttributeDefinition attribute = (AttributeDefinition)e.NewItems[i]!;
                GameAttributeSlots.Insert(i + e.NewStartingIndex, GameAttributeMenuItem(attribute));
            }
        }
        if (e.OldItems != null)
        {
            foreach (object oldItem in e.OldItems)
            {
                FilterMenuItemViewModel<GameData> item = GameAttributeSlots[e.OldStartingIndex];
                GameAttributeSlots.RemoveAt(e.OldStartingIndex);
                _gameAttributeItems.Remove((AttributeDefinition)oldItem);
                item.Destroy();
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
        foreach (BuildFilterScope scope in _buildScopes)
            RefreshBuildMenu(scope, roots);
    }

    private void RefreshBuildMenu(BuildFilterScope scope, List<BuildNode> roots)
    {
        Dictionary<int, FilterMenuItemViewModel<GameData>> previousItems = scope.ItemsByBuildId;
        scope.ItemsByBuildId = new Dictionary<int, FilterMenuItemViewModel<GameData>>();

        // Built before the old entries are torn down, because each new entry takes over the filters its
        // predecessor had added — an edit on the Builds tab mustn't discard criteria entered here. A build
        // that no longer exists simply isn't adopted, so its filters leave the bar with it.
        List<FilterMenuItemViewModel<GameData>> raceItems = [];
        foreach (Race race in Enum.GetValues<Race>())
        {
            ObservableCollection<FilterMenuItemViewModel<GameData>> raceBuilds =
                new(roots.Where(b => b.PlayerRace == race).Select(build => BuildMenuItem(scope, build, previousItems)));
            raceItems.Add(new FilterMenuItemViewModel<GameData>(raceBuilds, race.Display()));
        }

        for (int index = scope.RaceItems.Count - 1; index >= 0; index--)
        {
            scope.RaceItems[index].DestroyRecursively();
            scope.RaceItems.RemoveAt(index); //do not replace this with an outside-of-loop .Clear() call. .Clear doesn't include OldItems in its notify event
        }
        foreach (FilterMenuItemViewModel<GameData> raceItem in raceItems)
            scope.RaceItems.Add(raceItem);
    }

    private FilterMenuItemViewModel<GameData> BuildMenuItem(BuildFilterScope scope, BuildNode build,
        Dictionary<int, FilterMenuItemViewModel<GameData>> previousItems)
    {
        FilterMenuItemViewModel<GameData> item = build.Children.Count == 0
            ? new FilterMenuItemViewModel<GameData>(() => new BuildFilterSlotViewModel(build, scope.Players), build.Name)
            : new FilterMenuItemViewModel<GameData>(() => new BuildFilterSlotViewModel(build, scope.Players), build.Name,
                new ObservableCollection<FilterMenuItemViewModel<GameData>>(build.Children.Select(child => BuildMenuItem(scope, child, previousItems))));

        if (previousItems.TryGetValue(build.Id, out FilterMenuItemViewModel<GameData>? previousItem))
        {
            item.AdoptAppliedFilters(previousItem);
            // The adopted filters were built against the previous tree's node, which may have been
            // renamed, retyped or given new details since.
            foreach (BuildFilterSlotViewModel adopted in item.AppliedFilters.OfType<BuildFilterSlotViewModel>())
                adopted.Rebuild(build);
        }

        scope.ItemsByBuildId[build.Id] = item;
        return item;
    }

    // One set of players' builds: its own menu entries, and its own filters once the user adds any, all
    // built from the same build tree.
    private sealed class BuildFilterScope
    {
        public Func<GameData, IEnumerable<GamePlayer>> Players { get; }
        public ObservableCollection<FilterMenuItemViewModel<GameData>> RaceItems { get; } = [];

        // The entries from the last refresh, by build id, so the next one can hand each build's filters to
        // its replacement entry.
        public Dictionary<int, FilterMenuItemViewModel<GameData>> ItemsByBuildId { get; set; } = new();
        public FilterMenuItemViewModel<GameData> MenuItem { get; }

        public BuildFilterScope(string title, Func<GameData, IEnumerable<GamePlayer>> players)
        {
            Players = players;
            MenuItem = new FilterMenuItemViewModel<GameData>(RaceItems, title);
        }
    }
}
