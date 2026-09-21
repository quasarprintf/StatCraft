using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Maps;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.CollatedFilters;
using StatCraft.ViewModels.Windows.AttributeComponents;
using StatCraft.ViewModels.Windows.Filters;
using StatCraft.ViewModels.Windows.Filters.WrappedFilters;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace StatCraft.ViewModels.Windows;

public partial class MapsPageViewModel : ViewModelBase
{
    private readonly MapRepository _mapRepo;
    private readonly AttributeRepository _attributeRepo;
    private readonly GameDataRepository _gameDataRepo;

    private readonly List<Map> _allMaps = [];
    public ObservableCollection<Map> FilteredMaps { get; } = [];
    [ObservableProperty] private Map? _selectedMap;

    [ObservableProperty] private string _nameFilter = "";
    public FilterMenuViewModel<Map> FilterMenu { get; set; }
    private readonly Dictionary<AttributeDefinition, FilterMenuItemViewModel<Map>> _filterMenuItems = [];

    public ObservableCollection<AttributeDefinition> AllAttributes { get; } = [];
    public AttributeValuesSelectViewModel AttributeValuesSelect { get; }

    // Raised instead of deleting when the map still has games recorded on it
    public event Action<Map>? DeleteBlocked;

    public MapsPageViewModel(MapRepository mapRepository, AttributeRepository attributeRepository, GameDataRepository gameDataRepository)
    {
        _mapRepo = mapRepository;
        _attributeRepo = attributeRepository;
        _gameDataRepo = gameDataRepository;

        foreach (AttributeDefinition attribute in _attributeRepo.GetAllAttributes(AttributeScope.Map))
            AllAttributes.Add(attribute);
        AttributeValuesSelect = new AttributeValuesSelectViewModel(AllAttributes);

        foreach (Map map in _mapRepo.GetAllMaps(AllAttributes))
        {
            _allMaps.Add(map);
        }

        FilterMenu = new FilterMenuViewModel<Map>([]);
        FilterMenu.FiltersChanged += ApplyFilters;

        foreach (AttributeDefinition attribute in AllAttributes)
            AddFilterSlot(attribute);
        ApplyFilters();
        SelectedMap = FilteredMaps.FirstOrDefault();

        AttributeValuesSelect.ValueChanged += AttributeValueChanged;
        AttributeValuesSelect.ValueDeleted += AttributeValueDeleted;

        _attributeRepo.AttributesChanged += SyncAttributesFromRepository;
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
                if (_filterMenuItems.TryGetValue(cachedAttr, out FilterMenuItemViewModel<Map>? slot))
                    slot.Filter!.Title = dbAttr.Name;
            }

            if (cachedAttr.Type != dbAttr.Type)
            {
                cachedAttr.Type = dbAttr.Type;
                if (_filterMenuItems.TryGetValue(cachedAttr, out FilterMenuItemViewModel<Map>? slot))
                    ((AttributeFilterSlotViewModel<Map>)slot.Filter!).Rebuild();
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

        if (_filterMenuItems.TryGetValue(attribute, out FilterMenuItemViewModel<Map>? slot) &&
            slot.Filter is AttributeFilterSlotViewModel<Map> attributeSlot)
        {
            attributeSlot.Refresh();
        }
    }

    #region event handling for selected map
    partial void OnSelectedMapChanging(Map? value)
    {
        if (SelectedMap != null)
            UnWireMap(SelectedMap);

        if (value != null)
            WireMap(value);

        AttributeValuesSelect.Object = value;
    }
    private void WireMap(Map map)
    {
        map.PropertyChanged += MapPropertyChanged;
    }
    private void UnWireMap(Map map)
    {
        map.PropertyChanged -= MapPropertyChanged;
    }
    
    private void MapPropertyChanged(object? s, PropertyChangedEventArgs e)
    {
        if (s is Map m && e.PropertyName == nameof(Map.Name))
        {
            _mapRepo.UpdateMap(m);
            ApplyFilters();
        }
    }

    private void AttributeValueDeleted(object? s, AttributeValue value)
    {
        if (s is not Map map)
            return; //TODO: log this, it's unexpected

        // SaveValue deletes if value is null
        _mapRepo.SaveValue(map.Id, value.Definition.Id, null);
        ApplyFilters();
    }
    private void AttributeValueChanged(object? s, AttributeValue value)
    {
        if (s is not Map map)
            return; //TODO: log this, it's unexpected

        _mapRepo.SaveValue(map.Id, value.Definition.Id, value.Serialize());
        ApplyFilters();
    }
    #endregion

    #region filters
    private void AddFilterSlot(AttributeDefinition attribute)
    {
        IFilterSlotViewModel<Map> slot = new AttributeFilterSlotViewModel<Map>(attribute);
        slot.AllowIncludeUnset = true;
        FilterMenuItemViewModel<Map> menuItem = new FilterMenuItemViewModel<Map>(slot);

        _filterMenuItems[attribute] = menuItem;
        FilterMenu.AddFilter(menuItem);
    }
    private void RemoveFilterSlot(AttributeDefinition attribute)
    {
        if (!_filterMenuItems.Remove(attribute, out FilterMenuItemViewModel<Map>? slot))
            return;

        FilterMenu.RemoveFilter(slot);
    }


    partial void OnNameFilterChanged(string value)
    {
        ApplyFilters();
    }

    // Rebuilds the visible map list from the name box and every active attribute filter, ANDed.
    // Kept as an in-place edit of the bound collection rather than a fresh one, so the ListBox's
    // selection survives a filter change that still includes the selected map.
    private void ApplyFilters()
    {
        AndFilter<Map> filter = GetFilters();

        List<Map> matching = _allMaps.Where(m => filter.MatchesFilter(m)).ToList();

        //TODO: figure out why this is modifying FilteredMaps instead of reassigning it
        for (int i = FilteredMaps.Count - 1; i >= 0; i--)
            if (!matching.Contains(FilteredMaps[i]))
                FilteredMaps.RemoveAt(i);
        for (int i = 0; i < matching.Count; i++)
            if (!FilteredMaps.Contains(matching[i]))
                FilteredMaps.Insert(i, matching[i]);
    }
    private AndFilter<Map> GetFilters()
    {
        StringFilter<Map> nameFilter = new StringFilter<Map>(m => m.Name)
        {
            FilterValue = NameFilter.Trim(),
            MatchExact = false,
            AcceptNull = string.IsNullOrWhiteSpace(NameFilter)
        };
        var attributeFilters = FilterMenu.GetFilter();
        return new AndFilter<Map>([nameFilter, attributeFilters]);
    }
    #endregion
}
