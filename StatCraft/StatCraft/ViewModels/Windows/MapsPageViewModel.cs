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

namespace StatCraft.ViewModels
{
    // The Maps tab. Structurally simpler than Builds — maps never nest, so this is a flat list rather
    // than a tree — but with one twist Builds doesn't have: attributes are defined globally, so adding
    // one adds it to every map at once (unset everywhere) and editing its name/type/options changes it
    // everywhere. Only the *values* are per map.
    //
    // Persistence is write-through on PropertyChanged, the same way BuildsPageViewModel works; nothing
    // here needs an explicit save.
    public partial class MapsPageViewModel : ViewModelBase
    {
        private readonly MapRepository _repository;
        private readonly GameDataRepository _gameDataRepository;

        // Every map, unfiltered. Maps (the bound collection) is the subset currently passing the filters.
        private readonly List<Map> _allMaps = [];

        // Which attribute each dynamically-created filter slot constrains. A dictionary rather than a
        // Map-aware slot subclass, so the existing filter-slot types can be reused unchanged.
        private readonly Dictionary<FilterSlotViewModel, MapAttribute> _attributeBySlot = [];

        public MapsPageViewModel(MapRepository repository, GameDataRepository gameDataRepository)
        {
            _repository = repository;
            _gameDataRepository = gameDataRepository;

            foreach (MapAttribute attribute in _repository.GetAllAttributes())
            {
                WireAttribute(attribute);
                Attributes.Add(attribute);
            }

            foreach (Map map in _repository.GetAllMaps(Attributes))
            {
                WireMap(map);
                _allMaps.Add(map);
            }

            RebuildFilterSlots();
            ApplyFilters();
            SelectedMap = Maps.FirstOrDefault();
        }

        // The global attribute definitions, shared by every map — each Map holds one MapAttributeValue
        // per entry here, in the same order.
        public ObservableCollection<MapAttribute> Attributes { get; } = [];

        // The maps currently passing the name and attribute filters.
        public ObservableCollection<Map> Maps { get; } = [];

        [ObservableProperty] private Map? _selectedMap;

        [ObservableProperty] private string _nameFilter = "";

        // One slot per attribute definition, rebuilt whenever the definitions change. Unlike the Data
        // tab's fixed five, these come and go with the attributes themselves.
        public ObservableCollection<FilterSlotViewModel> AttributeFilterSlots { get; } = [];
        public IEnumerable<FilterSlotViewModel> VisibleFilterSlots => AttributeFilterSlots.Where(s => s.IsVisible);
        public IEnumerable<FilterSlotViewModel> HiddenFilterSlots => AttributeFilterSlots.Where(s => !s.IsVisible);

        // Raised instead of deleting when the map still has games recorded on it. Unlike a build, a map
        // can't be detached from its games — Games.MapId has no cascade — so the view reports this rather
        // than offering to go ahead anyway.
        public event Action<Map>? DeleteBlocked;

        partial void OnNameFilterChanged(string value) => ApplyFilters();

        [RelayCommand]
        public void AddMap()
        {
            Map map = new() { Name = "New Map" };
            _repository.InsertMap(map);

            // Every existing attribute applies to it immediately, with no value.
            foreach (MapAttribute attribute in Attributes)
                map.AttributeValues.Add(new MapAttributeValue(attribute));

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

            _repository.DeleteMap(map.Id);
            _allMaps.Remove(map);
            ApplyFilters();

            if (wasSelected)
                SelectedMap = Maps.ElementAtOrDefault(index) ?? Maps.ElementAtOrDefault(index - 1);
        }

        [RelayCommand]
        public void AddAttribute()
        {
            MapAttribute attribute = new() { Name = "New Attribute" };
            _repository.InsertAttribute(attribute, Attributes.Count);
            WireAttribute(attribute);
            Attributes.Add(attribute);

            // Defined for every map at once, and unset on all of them until someone fills it in.
            foreach (Map map in _allMaps)
                map.AttributeValues.Add(new MapAttributeValue(attribute));

            RebuildFilterSlots();
            ApplyFilters();
        }

        [RelayCommand]
        public void RemoveAttribute(MapAttribute attribute)
        {
            _repository.DeleteAttribute(attribute.Id);
            Attributes.Remove(attribute);

            foreach (Map map in _allMaps)
            {
                MapAttributeValue? value = map.AttributeValues.FirstOrDefault(v => v.Attribute == attribute);
                if (value != null)
                    map.AttributeValues.Remove(value);
            }

            RebuildFilterSlots();
            ApplyFilters();
        }

        private void WireMap(Map map)
        {
            map.PropertyChanged += (s, e) =>
            {
                if (s is Map m && e.PropertyName == nameof(Map.Name))
                {
                    _repository.UpdateMap(m);
                    ApplyFilters();
                }
            };

            foreach (MapAttributeValue value in map.AttributeValues)
                WireValue(map, value);

            // Values are appended when a new attribute is defined and removed when one is deleted, so
            // the per-value subscriptions have to follow the collection rather than being set up once.
            map.AttributeValues.CollectionChanged += (s, e) =>
            {
                if (e.NewItems == null) return;
                foreach (MapAttributeValue value in e.NewItems.OfType<MapAttributeValue>())
                    WireValue(map, value);
            };
        }

        private void WireValue(Map map, MapAttributeValue value)
        {
            value.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MapAttributeValue.HasValue))
                    return;

                // Serialize() returns null when unset, and SaveValue deletes the row for null — that
                // absence is what "no value" actually is in the database.
                _repository.SaveValue(map.Id, value.Attribute.Id, value.Serialize());
                ApplyFilters();
            };
        }

        private void WireAttribute(MapAttribute attribute)
        {
            attribute.PropertyChanged += (s, e) =>
            {
                if (s is not MapAttribute a) return;
                if (e.PropertyName != nameof(MapAttribute.Name) && e.PropertyName != nameof(MapAttribute.Type))
                    return;

                _repository.UpdateAttribute(a);
                // The type decides which kind of filter slot the attribute gets, and the name is its
                // label, so either edit invalidates the current slots.
                RebuildFilterSlots();
                ApplyFilters();
            };

            attribute.ValueOptions.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (string value in e.NewItems.OfType<string>())
                        _repository.InsertValueOption(attribute.Id, value, attribute.ValueOptions.IndexOf(value));
                if (e.OldItems != null)
                    foreach (string value in e.OldItems.OfType<string>())
                        _repository.DeleteValueOption(attribute.Id, value);

                RebuildFilterSlots();
                ApplyFilters();
            };
        }

        // Discards and recreates every slot from the current definitions. Rebuilding wholesale (rather
        // than patching) keeps this correct when an attribute changes type mid-flight, at the cost of
        // dropping whatever was selected in the affected filter — acceptable, since the selection often
        // isn't expressible in the new type anyway.
        private void RebuildFilterSlots()
        {
            HashSet<int> previouslyVisible = _attributeBySlot
                .Where(kvp => kvp.Key.IsVisible)
                .Select(kvp => kvp.Value.Id)
                .ToHashSet();

            AttributeFilterSlots.Clear();
            _attributeBySlot.Clear();

            foreach (MapAttribute attribute in Attributes)
            {
                FilterSlotViewModel slot = CreateSlot(attribute);
                slot.IsVisible = previouslyVisible.Contains(attribute.Id);
                slot.VisibilityChanged += OnSlotVisibilityChanged;
                slot.Changed += ApplyFilters;

                _attributeBySlot[slot] = attribute;
                AttributeFilterSlots.Add(slot);
            }

            OnSlotVisibilityChanged();
        }

        private void OnSlotVisibilityChanged()
        {
            OnPropertyChanged(nameof(VisibleFilterSlots));
            OnPropertyChanged(nameof(HiddenFilterSlots));
        }

        private static FilterSlotViewModel CreateSlot(MapAttribute attribute) => attribute.Type switch
        {
            AttributeType.Bool => new CheckboxFilterSlotViewModel<bool>(attribute.Name,
                [new CheckboxFilterOptionViewModel<bool>(true, "Yes"), new CheckboxFilterOptionViewModel<bool>(false, "No")]),
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
            if (!MapFilter.MatchesName(map, NameFilter))
                return false;

            foreach ((FilterSlotViewModel slot, MapAttribute attribute) in _attributeBySlot)
            {
                if (!slot.IsVisible)
                    continue;

                MapAttributeValue? value = map.AttributeValues.FirstOrDefault(v => v.Attribute == attribute);
                if (value == null)
                    continue;

                if (!MatchesSlot(slot, value))
                    return false;
            }

            return true;
        }

        private static bool MatchesSlot(FilterSlotViewModel slot, MapAttributeValue value) => slot switch
        {
            NumericRangeFilterSlotViewModel range =>
                MapFilter.MatchesRange(value, range.Min, range.Max, range.IncludeUnset),
            CheckboxFilterSlotViewModel<bool> booleans =>
                MapFilter.MatchesSelection(Checked(booleans), value.HasValue, value.BoolValue ?? false, booleans.IncludeUnset),
            CheckboxFilterSlotViewModel<string> strings =>
                MapFilter.MatchesSelection(Checked(strings), value.HasValue, value.SelectedValue ?? "", strings.IncludeUnset),
            _ => true,
        };

        private static IReadOnlySet<T> Checked<T>(CheckboxFilterSlotViewModel<T> slot) =>
            slot.Options.Where(o => o.IsChecked).Select(o => o.Value).ToHashSet();
    }
}
