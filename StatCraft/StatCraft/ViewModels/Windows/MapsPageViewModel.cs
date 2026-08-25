using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Maps;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataFiltering;
using StatCraft.ViewModels.Windows.DataComponents;

namespace StatCraft.ViewModels.Windows
{
    // The Maps tab. Structurally simpler than Builds — maps never nest, so this is a flat list rather
    // than a tree — but with one twist Builds doesn't have: attributes are defined globally, from the
    // Attributes tab, so adding one adds it to every map at once (unset everywhere). This page can only
    // edit the per-map *value* — name/type/options are the attribute definition and aren't editable here.
    //
    // Persistence is write-through on PropertyChanged, the same way BuildsPageViewModel works; nothing
    // here needs an explicit save.
    public partial class MapsPageViewModel : ViewModelBase
    {
        private readonly MapRepository mapRepo;
        private readonly AttributeRepository attributeRepo;
        private readonly GameDataRepository _gameDataRepository;

        // Every map, unfiltered. Maps (the bound collection) is the subset currently passing the filters.
        private readonly List<Map> _allMaps = [];

        // The filter slot for each attribute definition. A dictionary rather than a Map-aware slot
        // subclass, so the existing filter-slot types can be reused unchanged.
        private readonly Dictionary<AttributeDefinition, FilterSlotViewModel> _slotByAttribute = [];

        public MapsPageViewModel(MapRepository mapRepository, AttributeRepository attributeRepository, GameDataRepository gameDataRepository)
        {
            mapRepo = mapRepository;
            attributeRepo = attributeRepository;
            _gameDataRepository = gameDataRepository;

            foreach (AttributeDefinition attribute in attributeRepo.GetAllAttributes(AttributeScope.Map))
                Attributes.Add(attribute);

            foreach (Map map in mapRepo.GetAllMaps(Attributes))
            {
                WireMap(map);
                _allMaps.Add(map);
            }

            foreach (AttributeDefinition attribute in Attributes)
                AddFilterSlot(attribute);
            ApplyFilters();
            SelectedMap = Maps.FirstOrDefault();

            // AttributeRepository is shared with the Attributes tab, but this page keeps its own
            // in-memory attribute list (loaded once, above) — without this, an attribute added or
            // removed from the Attributes tab would only show up here after restarting the app.
            attributeRepo.AttributesChanged += SyncAttributesFromRepository;
        }

        // The global attribute definitions, shared by every map — each Map holds one MapAttributeValue
        // per entry here, in the same order.
        public ObservableCollection<AttributeDefinition> Attributes { get; } = [];

        // The maps currently passing the name and attribute filters.
        public ObservableCollection<Map> Maps { get; } = [];

        [ObservableProperty] private Map? _selectedMap;

        [ObservableProperty] private string _nameFilter = "";


        // Split views of AttributeFilterSlots by visibility, kept in sync incrementally rather than as a
        // computed `Where(...)` property notified via OnPropertyChanged. The visible-side ItemsControl
        // tolerated that pattern fine, but the "+ " button's MenuFlyout (bound to HiddenFilterSlots) does
        // not reliably re-evaluate a property-changed-only binding once its popup content has been
        // realized once — reopening it kept showing whatever attributes existed the first time it was
        // shown, silently missing anything added or removed afterward. Real ObservableCollections raise
        // CollectionChanged, which the flyout's presenter does honor correctly.
        public ObservableCollection<FilterSlotViewModel> VisibleFilterSlots { get; } = [];
        public ObservableCollection<FilterSlotViewModel> HiddenFilterSlots { get; } = [];

        // Raised instead of deleting when the map still has games recorded on it. Unlike a build, a map
        // can't be detached from its games — Games.MapId has no cascade — so the view reports this rather
        // than offering to go ahead anyway.
        public event Action<Map>? DeleteBlocked;

        partial void OnNameFilterChanged(string value) => ApplyFilters();

        [RelayCommand]
        public void AddMap()
        {
            Map map = new() { Name = "New Map" };
            mapRepo.InsertMap(map);

            // Every existing attribute applies to it immediately, with no value.
            foreach (AttributeDefinition attribute in Attributes)
                map.AttributeValues.Add(new AttributeValue(attribute));

            WireMap(map);
            _allMaps.Add(map);
            ApplyFilters();
            SelectedMap = map;
        }

        [RelayCommand]
        public void DeleteMap(Map map)
        {
            if (_gameDataRepository.IsAnyMapReferenced(map.Id))
            {
                DeleteBlocked?.Invoke(map);
                return;
            }

            // Captured before the list changes: removing the item from Maps makes the ListBox null its
            // own selection, so SelectedMap can't be compared against afterwards.
            bool wasSelected = SelectedMap == map;
            int index = Maps.IndexOf(map);

            mapRepo.DeleteMap(map.Id);
            _allMaps.Remove(map);
            ApplyFilters();

            if (wasSelected)
                SelectedMap = Maps.ElementAtOrDefault(index) ?? Maps.ElementAtOrDefault(index - 1);
        }

        // Reconciles Attributes (and every map's AttributeValues, and the filter slots) against
        // AttributeDefinitions, so any change made on the Attributes tab ends up reflected here
        private void SyncAttributesFromRepository()
        {
            List<AttributeDefinition> current = attributeRepo.GetAllAttributes(AttributeScope.Map);
            Dictionary<int, AttributeDefinition> currentById = current.ToDictionary(a => a.Id);

            //sync deleted attributes
            foreach (AttributeDefinition attribute in Attributes.Where(a => !currentById.ContainsKey(a.Id)).ToList())
            {
                Attributes.Remove(attribute);

                foreach (Map map in _allMaps)
                {
                    AttributeValue? value = map.AttributeValues.FirstOrDefault(v => v.Definition == attribute);
                    if (value != null)
                        map.AttributeValues.Remove(value);
                }

                RemoveFilterSlot(attribute);
            }

            //sync edited attributes
            foreach (AttributeDefinition attribute in Attributes)
            {
                AttributeDefinition latest = currentById[attribute.Id];

                if (attribute.Name != latest.Name)
                {
                    attribute.Name = latest.Name;
                    if (_slotByAttribute.TryGetValue(attribute, out FilterSlotViewModel? slot))
                        slot.Title = latest.Name;
                }

                if (attribute.Type != latest.Type)
                {
                    attribute.Type = latest.Type;
                    // Numeric/Percent vs. Bool vs. Values are different FilterSlotViewModel subclasses,
                    // so the slot itself has to be replaced rather than patched — but only for this one
                    // attribute, and preserving whether it was actually showing.
                    bool wasVisible = _slotByAttribute.TryGetValue(attribute, out FilterSlotViewModel? old) && old.IsVisible;
                    RemoveFilterSlot(attribute);
                    AddFilterSlot(attribute, wasVisible);
                }

                SyncValueOptions(attribute, latest.ValueOptions);
            }

            //sync new attributes
            HashSet<int> knownIds = Attributes.Select(a => a.Id).ToHashSet();
            foreach (AttributeDefinition attribute in current.Where(a => !knownIds.Contains(a.Id)))
            {
                Attributes.Add(attribute);

                // Defined for every map at once, and unset on all of them until someone fills it in.
                foreach (Map map in _allMaps)
                    map.AttributeValues.Add(new AttributeValue(attribute));

                AddFilterSlot(attribute);
            }

            ApplyFilters();
        }

        // Patches attribute.ValueOptions to match latest in place (add/remove, not replace), so the
        // ComboBox bound to it updates, and — for a still-visible Values filter — the checkbox slot's
        // options are patched too, preserving whichever options are still checked.
        private void SyncValueOptions(AttributeDefinition attribute, ObservableCollection<string> latest)
        {
            bool changed = false;

            //remove deleted options
            foreach (string stale in attribute.ValueOptions.Where(o => !latest.Contains(o)).ToList())
            {
                attribute.ValueOptions.Remove(stale);
                changed = true;
            }

            //sync new options
            foreach (string value in latest.Where(o => !attribute.ValueOptions.Contains(o)))
            {
                attribute.ValueOptions.Add(value);
                changed = true;
            }

            if (!changed)
                return;

            if (_slotByAttribute.TryGetValue(attribute, out FilterSlotViewModel? slot) &&
                slot is CheckboxFilterSlotViewModel<string> stringSlot)
            {
                HashSet<string> previouslyChecked = stringSlot.Options.Where(o => o.IsChecked).Select(o => o.Value).ToHashSet();
                stringSlot.ReplaceOptions(attribute.ValueOptions
                    .Select(o => new CheckboxFilterOptionViewModel<string>(o, o) { IsChecked = previouslyChecked.Contains(o) }));
            }
        }

        private void WireMap(Map map)
        {
            map.PropertyChanged += (s, e) =>
            {
                if (s is Map m && e.PropertyName == nameof(Map.Name))
                {
                    mapRepo.UpdateMap(m);
                    ApplyFilters();
                }
            };

            foreach (AttributeValue value in map.AttributeValues)
                WireValue(map, value);

            // Values are appended when a new attribute is defined and removed when one is deleted, so
            // the per-value subscriptions have to follow the collection rather than being set up once.
            map.AttributeValues.CollectionChanged += (s, e) =>
            {
                if (e.NewItems == null) return;
                foreach (AttributeValue value in e.NewItems.OfType<AttributeValue>())
                    WireValue(map, value);
            };
        }

        private void WireValue(Map map, AttributeValue value)
        {
            value.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AttributeValue.HasValue))
                    return;

                // Serialize() returns null when unset, and SaveValue deletes the row for null — that
                // absence is what "no value" actually is in the database.
                mapRepo.SaveValue(map.Id, value.Definition.Id, value.Serialize());
                ApplyFilters();
            };
        }

        // Adds one new filter slot for this attribute
        private void AddFilterSlot(AttributeDefinition attribute, bool isVisible = false)
        {
            FilterSlotViewModel slot = CreateSlot(attribute);
            slot.IsVisible = isVisible;
            slot.VisibilityChanged += () => OnSlotVisibilityChanged(slot);
            slot.Changed += ApplyFilters;

            _slotByAttribute[attribute] = slot;
            (isVisible ? VisibleFilterSlots : HiddenFilterSlots).Add(slot);
        }

        private void RemoveFilterSlot(AttributeDefinition attribute)
        {
            if (!_slotByAttribute.Remove(attribute, out FilterSlotViewModel? slot))
                return;

            slot.Changed -= ApplyFilters;
            VisibleFilterSlots.Remove(slot);
            HiddenFilterSlots.Remove(slot);
        }

        // Moves a slot between the visible/hidden collections when its own IsVisible flips — via the
        // Add/Remove commands the "+" menu and the filter's own ✕ button invoke.
        private void OnSlotVisibilityChanged(FilterSlotViewModel slot)
        {
            if (slot.IsVisible)
            {
                HiddenFilterSlots.Remove(slot);
                if (!VisibleFilterSlots.Contains(slot))
                    VisibleFilterSlots.Add(slot);
            }
            else
            {
                VisibleFilterSlots.Remove(slot);
                if (!HiddenFilterSlots.Contains(slot))
                    HiddenFilterSlots.Add(slot);
            }
        }

        private static FilterSlotViewModel CreateSlot(AttributeDefinition attribute) => attribute.Type switch
        {
            AttributeType.Bool => new BoolFilterSlotViewModel(attribute.Name),
            AttributeType.Values => new CheckboxFilterSlotViewModel<string>(attribute.Name,
                attribute.ValueOptions.Select(o => new CheckboxFilterOptionViewModel<string>(o, o)), showSearch: true),
            // Numeric and Percent both filter by range.
            _ => new NumericRangeFilterSlotViewModel(attribute.Name),
        };

        // Rebuilds the visible map list from the name box and every active attribute filter, ANDed.
        // Kept as an in-place edit of the bound collection rather than a fresh one, so the ListBox's
        // selection survives a filter change that still includes the selected map.
        private void ApplyFilters()
        {
            List<Map> matching = _allMaps.Where(Matches).ToList();

            for (int i = Maps.Count - 1; i >= 0; i--)
                if (!matching.Contains(Maps[i]))
                    Maps.RemoveAt(i);

            for (int i = 0; i < matching.Count; i++)
                if (!Maps.Contains(matching[i]))
                    Maps.Insert(i, matching[i]);
        }

        private bool Matches(Map map)
        {
            if (!MatchesName(map, NameFilter))
                return false;

            foreach ((AttributeDefinition attribute, FilterSlotViewModel slot) in _slotByAttribute)
            {
                if (!slot.IsVisible)
                    continue;

                AttributeValue? value = map.AttributeValues.FirstOrDefault(v => v.Definition == attribute);
                if (value == null)
                    continue;

                if (!MatchesSlot(slot, value))
                    return false;
            }

            return true;
        }

        //temporarily public to facilitate testing. Should be indirectly tested via OnNameFilterChanged, then this can be made private again
        public static bool MatchesName(Map map, string? nameFilter)
        {
            return string.IsNullOrWhiteSpace(nameFilter) || map.Name.Contains(nameFilter.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        private static bool MatchesSlot(FilterSlotViewModel slot, AttributeValue value) => slot switch
        {
            NumericRangeFilterSlotViewModel range =>
                AttributeFilter.MatchesRange(value, range.Min, range.Max, range.IncludeUnset),
            BoolFilterSlotViewModel boolSlot =>
                AttributeFilter.MatchesBool(value, boolSlot.Value, boolSlot.IncludeUnset),
            CheckboxFilterSlotViewModel<string> strings =>
                AttributeFilter.MatchesSelection(Checked(strings), value.HasValue, value.SelectedValue ?? "", strings.IncludeUnset),
            _ => true,
        };

        private static IReadOnlySet<T> Checked<T>(CheckboxFilterSlotViewModel<T> slot) =>
            slot.Options.Where(o => o.IsChecked).Select(o => o.Value).ToHashSet();
    }
}
