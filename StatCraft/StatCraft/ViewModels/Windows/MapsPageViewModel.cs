using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
    public partial class MapsPageViewModel : ViewModelBase
    {
        private readonly MapRepository _mapRepo;
        private readonly AttributeRepository _attributeRepo;
        private readonly GameDataRepository _gameDataRepo;

        private readonly List<Map> _allMaps = [];
        public ObservableCollection<Map> FilteredMaps { get; } = [];
        [ObservableProperty] private Map? _selectedMap;

        [ObservableProperty] private string _nameFilter = "";
        private readonly Dictionary<AttributeDefinition, FilterSlotViewModel> _slotByAttribute = [];
        public ObservableCollection<FilterSlotViewModel> VisibleFilterSlots { get; } = [];
        public ObservableCollection<FilterSlotViewModel> HiddenFilterSlots { get; } = [];

        public ObservableCollection<AttributeDefinition> AllAttributes { get; } = [];
        public IEnumerable<AttributeDefinition> UnusedAttributes => SelectedMap == null ? Enumerable.Empty<AttributeDefinition>() : AllAttributes.Where(a => !SelectedMap.AttributeValues.Any(v => v.Definition.Id == a.Id));

        // Raised instead of deleting when the map still has games recorded on it
        public event Action<Map>? DeleteBlocked;

        public MapsPageViewModel(MapRepository mapRepository, AttributeRepository attributeRepository, GameDataRepository gameDataRepository)
        {
            _mapRepo = mapRepository;
            _attributeRepo = attributeRepository;
            _gameDataRepo = gameDataRepository;

            foreach (AttributeDefinition attribute in _attributeRepo.GetAllAttributes(AttributeScope.Map))
                AllAttributes.Add(attribute);

            foreach (Map map in _mapRepo.GetAllMaps(AllAttributes))
            {
                _allMaps.Add(map);
            }

            foreach (AttributeDefinition attribute in AllAttributes)
                AddFilterSlot(attribute);
            ApplyFilters();
            SelectedMap = FilteredMaps.FirstOrDefault();

            _attributeRepo.AttributesChanged += SyncAttributesFromRepository;

            AllAttributes.CollectionChanged += RaiseUnusedAttributesChanged;
        }

        partial void OnNameFilterChanged(string value)
        {
            ApplyFilters();
        }

        [RelayCommand]
        public void AddMap()
        {
            Map map = new() { Name = "New Map" };
            _mapRepo.InsertMap(map);

            // Every existing attribute applies to it immediately, with no value.
            foreach (AttributeDefinition attribute in AllAttributes)
                map.AttributeValues.Add(attribute.DefaultValue.Clone());

            _allMaps.Add(map);
            ApplyFilters();
            SelectedMap = map;
        }

        [RelayCommand]
        public void DeleteMap(Map map)
        {
            if (_gameDataRepo.IsAnyMapReferenced(map.Id))
            {
                DeleteBlocked?.Invoke(map);
                return;
            }

            // Captured before the list changes: removing the item from Maps makes the ListBox null its
            // own selection, so SelectedMap can't be compared against afterwards.
            bool wasSelected = SelectedMap == map;
            int index = FilteredMaps.IndexOf(map);

            _mapRepo.DeleteMap(map.Id);
            _allMaps.Remove(map);
            ApplyFilters();

            if (wasSelected)
                SelectedMap = FilteredMaps.ElementAtOrDefault(index) ?? FilteredMaps.ElementAtOrDefault(index - 1);
        }

        private void SyncAttributesFromRepository()
        {
            List<AttributeDefinition> dbAttributes = _attributeRepo.GetAllAttributes(AttributeScope.Map);
            Dictionary<int, AttributeDefinition> dbById = dbAttributes.ToDictionary(a => a.Id);

            //sync deleted attributes
            foreach (AttributeDefinition cachedAttr in AllAttributes.Where(a => !dbById.ContainsKey(a.Id)).ToList())
            {
                AllAttributes.Remove(cachedAttr);

                foreach (Map map in _allMaps)
                {
                    AttributeValue? value = map.AttributeValues.FirstOrDefault(v => v.Definition.Id == cachedAttr.Id);
                    if (value != null)
                        map.AttributeValues.Remove(value);
                }

                RemoveFilterSlot(cachedAttr);
            }

            //sync edited attributes
            foreach (AttributeDefinition cachedAttr in AllAttributes)
            {
                AttributeDefinition dbAttr = dbById[cachedAttr.Id];

                if (cachedAttr.Name != dbAttr.Name)
                {
                    cachedAttr.Name = dbAttr.Name;
                    if (_slotByAttribute.TryGetValue(cachedAttr, out FilterSlotViewModel? slot))
                        slot.Title = dbAttr.Name;
                }

                if (cachedAttr.Type != dbAttr.Type)
                {
                    cachedAttr.Type = dbAttr.Type;
                    // Numeric/Percent vs. Bool vs. Values are different FilterSlotViewModel subclasses,
                    // so the slot itself has to be replaced rather than patched — but only for this one
                    // attribute, and preserving whether it was actually showing.
                    bool wasVisible = _slotByAttribute.TryGetValue(cachedAttr, out FilterSlotViewModel? old) && old.IsVisible;
                    RemoveFilterSlot(cachedAttr);
                    AddFilterSlot(cachedAttr, wasVisible);
                }

                if (dbAttr.IsMandatory != cachedAttr.IsMandatory)
                {
                    cachedAttr.IsMandatory = dbAttr.IsMandatory;
                    List<Map> mapsToSave = new List<Map>();
                    if (dbAttr.IsMandatory)
                    {
                        // Defined for every map with default value
                        foreach (Map map in _allMaps)
                        {
                            if (!map.AttributeValues.Any(a => a.Definition.Id == dbAttr.Id))
                            {
                                map.AttributeValues.Add(cachedAttr.DefaultValue.Clone());
                                mapsToSave.Add(map);
                            }
                        }
                    }
                    else
                    {
                        foreach (Map map in _allMaps)
                        {
                            AttributeValue? value = map.AttributeValues.FirstOrDefault(v => v.Definition.Id == cachedAttr.Id);
                            if (value != null && !value.HasValue)
                            {
                                map.AttributeValues.Remove(value);
                                mapsToSave.Add(map);
                            }
                        }
                    }
                    _mapRepo.SaveValues(mapsToSave, dbAttr.Id);
                }

                SyncValueOptions(cachedAttr, dbAttr.ValueOptions);

                if (dbAttr.DefaultValue.HasValue)
                    cachedAttr.DefaultValue.ApplyStoredValue(dbAttr.DefaultValue.Serialize()!);
                else
                    cachedAttr.DefaultValue.Clear();
            }

            //sync new attributes
            HashSet<int> knownIds = AllAttributes.Select(a => a.Id).ToHashSet();
            foreach (AttributeDefinition dbAttr in dbAttributes.Where(a => !knownIds.Contains(a.Id)))
            {
                AllAttributes.Add(dbAttr);

                if (dbAttr.IsMandatory)
                {
                    // Defined for every map at once, and unset on all of them until someone fills it in.
                    List<Map> mapsToSave = new List<Map>();
                    foreach (Map map in _allMaps)
                    {
                        map.AttributeValues.Add(dbAttr.DefaultValue.Clone());
                        mapsToSave.Add(map);
                    }
                    _mapRepo.SaveValues(mapsToSave, dbAttr.Id);
                }

                AddFilterSlot(dbAttr);
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

        private void RaiseUnusedAttributesChanged(object? sender, EventArgs e)
        {
            OnPropertyChanged(nameof(UnusedAttributes));
        }
        partial void OnSelectedMapChanged(Map? value)
        {
            OnPropertyChanged(nameof(UnusedAttributes));
        }
        partial void OnSelectedMapChanging(Map? value)
        {
            if (SelectedMap != null)
                UnWireMap(SelectedMap);

            if (value != null)
                WireMap(value);
        }
        private void UnWireMap(Map map)
        {
            map.AttributeValues.CollectionChanged -= RaiseUnusedAttributesChanged;
            map.PropertyChanged -= MapPropertyChanged;

            foreach (AttributeValue value in map.AttributeValues)
                UnWireValue(map, value);
            map.AttributeValues.CollectionChanged -= MapAttributeValuesChanged;
        }
        private void WireMap(Map map)
        {
            map.AttributeValues.CollectionChanged += RaiseUnusedAttributesChanged;
            map.PropertyChanged += MapPropertyChanged;

            foreach (AttributeValue value in map.AttributeValues)
                WireValue(map, value);
            map.AttributeValues.CollectionChanged += MapAttributeValuesChanged;
        }
        private void MapPropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (s is Map m && e.PropertyName == nameof(Map.Name))
            {
                _mapRepo.UpdateMap(m);
                ApplyFilters();
            }
        }
        private void MapAttributeValuesChanged(object? s, NotifyCollectionChangedEventArgs e)
        {
            if (SelectedMap == null)
                return;
            if (e.OldItems != null)
                {
                    foreach (AttributeValue value in e.OldItems.OfType<AttributeValue>())
                        _mapRepo.SaveValue(SelectedMap.Id, value.Definition.Id, null);
                }
                if (e.NewItems != null)
                {
                    foreach (AttributeValue value in e.NewItems.OfType<AttributeValue>())
                    {
                        WireValue(SelectedMap, value);
                        _mapRepo.SaveValue(SelectedMap.Id, value.Definition.Id, value.Serialize());
                    }
                }
        }

        private void UnWireValue(Map map, AttributeValue value)
        {
            value.PropertyChanged -= ValuePropertyChanged;
        }
        private void WireValue(Map map, AttributeValue value)
        {
            value.PropertyChanged += ValuePropertyChanged;
        }
        private void ValuePropertyChanged(object? s, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AttributeValue.HasValue))
                return;
            if (SelectedMap == null)
                return;
            if (s is not AttributeValue value)
                return;

            // Serialize() returns null when unset, and SaveValue deletes the row for null — that
            // absence is what "no value" actually is in the database.
            _mapRepo.SaveValue(SelectedMap.Id, value.Definition.Id, value.Serialize());
            ApplyFilters();
        }

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

        private static FilterSlotViewModel CreateSlot(AttributeDefinition attribute)
        {
            switch (attribute.Type)
            {
                case AttributeType.Bool:
                    return new BoolFilterSlotViewModel(attribute.Name);
                case AttributeType.Values:
                    var checkboxFilters = attribute.ValueOptions.Select(o => new CheckboxFilterOptionViewModel<string>(o, o));
                    return new CheckboxFilterSlotViewModel<string>(attribute.Name, checkboxFilters, showSearch: true);
                case AttributeType.Numeric:
                case AttributeType.Percent:
                default:
                    return new NumericRangeFilterSlotViewModel(attribute.Name);
            }
        }

        // Rebuilds the visible map list from the name box and every active attribute filter, ANDed.
        // Kept as an in-place edit of the bound collection rather than a fresh one, so the ListBox's
        // selection survives a filter change that still includes the selected map.
        private void ApplyFilters()
        {
            List<Map> matching = _allMaps.Where(Matches).ToList();

            for (int i = FilteredMaps.Count - 1; i >= 0; i--)
                if (!matching.Contains(FilteredMaps[i]))
                    FilteredMaps.RemoveAt(i);

            for (int i = 0; i < matching.Count; i++)
                if (!FilteredMaps.Contains(matching[i]))
                    FilteredMaps.Insert(i, matching[i]);
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

                if (!MatchesSlot(slot, value))
                    return false;
            }

            return true;
        }

        private static bool MatchesName(Map map, string? nameFilter)
        {
            return string.IsNullOrWhiteSpace(nameFilter) || map.Name.Contains(nameFilter.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        private static bool MatchesSlot(FilterSlotViewModel slot, AttributeValue? value)
        {
            if (value == null)
                return slot.IncludeUnset;
            switch (slot)
            {
                case NumericRangeFilterSlotViewModel range:
                    return AttributeFilter.MatchesRange(value, range.Min, range.Max, range.IncludeUnset);
                case BoolFilterSlotViewModel boolSlot:
                    return AttributeFilter.MatchesBool(value, boolSlot.Value, boolSlot.IncludeUnset);
                case CheckboxFilterSlotViewModel<string> strings:
                    return AttributeFilter.MatchesSelection(Checked(strings), value.HasValue, value.SelectedValue ?? "", strings.IncludeUnset);
                default:
                    return true;
            }
        }

        private static IReadOnlySet<T> Checked<T>(CheckboxFilterSlotViewModel<T> slot)
        {
            return slot.Options.Where(o => o.IsChecked).Select(o => o.Value).ToHashSet();
        }
    }
}
