using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Attributes;

namespace StatCraft.ViewModels.Windows
{
    // The Attributes tab. Purely UI scaffolding for now — every attribute definition lives only in
    // memory, bucketed by scope, and every persistence hook below is a TODO stub rather than a real
    // BuildRepository/MapRepository call. Structurally mirrors BuildsPageViewModel's "one bucket per
    // selector value" pattern (there: Dictionary<Race, ...>, here: Dictionary<AttributeScope, ...>) and
    // MapsPageViewModel's name-filtered, in-place-diffed list.
    public partial class AttributesPageViewModel : ViewModelBase
    {
        // Only these three scopes get a tab — BuildDetail already has its own home on the Builds tab.
        private static readonly AttributeScope[] TabScopes = [AttributeScope.Game, AttributeScope.Build, AttributeScope.Map];

        private readonly Dictionary<AttributeScope, ObservableCollection<AttributeDefinition>> _attributesByScope = new()
        {
            [AttributeScope.Game] = [],
            [AttributeScope.Build] = [],
            [AttributeScope.Map] = [],
        };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Attributes))]
        private int _selectedScopeIndex;

        public AttributeScope SelectedScope => TabScopes[SelectedScopeIndex];

        // The currently-selected scope's full (unfiltered) bucket.
        public ObservableCollection<AttributeDefinition> Attributes => _attributesByScope[SelectedScope];

        // The subset of Attributes currently passing NameFilter — kept as an in-place-diffed collection
        // (matching MapsPageViewModel.ApplyFilters) so the ListBox's selection survives a filter/tab
        // change that still includes it.
        public ObservableCollection<AttributeDefinition> FilteredAttributes { get; } = [];

        [ObservableProperty] private string _nameFilter = "";

        [ObservableProperty] private AttributeDefinition? _selectedAttribute;

        // A definition has no "current value" of its own — this wraps SelectedAttribute the same way
        // BuildDetailsPanel/MapDetailsPanel wrap one per row, purely so the details panel's default-value
        // editor has an AttributeValue to bind against.
        [ObservableProperty] private AttributeValue? _selectedAttributeValue;

        partial void OnSelectedScopeIndexChanged(int value) => ApplyFilter();

        partial void OnNameFilterChanged(string value) => ApplyFilter();

        partial void OnSelectedAttributeChanged(AttributeDefinition? value)
        {
            SelectedAttributeValue = value == null ? null : new AttributeValue(value);
            if (SelectedAttributeValue != null)
                WireAttributeValue(SelectedAttributeValue);
        }

        [RelayCommand]
        private void AddAttribute()
        {
            AttributeDefinition attribute = new(SelectedScope) { Name = "New Attribute" };
            WireAttribute(attribute);
            Attributes.Add(attribute);
            ApplyFilter();
            SelectedAttribute = attribute;
            // TODO: persist the new attribute definition once a repository exists for this scope.
        }

        [RelayCommand]
        private void DeleteAttribute(AttributeDefinition attribute)
        {
            Attributes.Remove(attribute);
            ApplyFilter();
            if (SelectedAttribute == attribute)
                SelectedAttribute = FilteredAttributes.FirstOrDefault();
            // TODO: delete the attribute definition from the backend once a repository exists for this scope.
        }

        // Subscribes so edits can be persisted once a backend exists for this scope — mirrors
        // MapsPageViewModel.WireAttribute's shape, minus the actual repository call.
        private void WireAttribute(AttributeDefinition attribute)
        {
            attribute.PropertyChanged += (_, _) => OnAttributeEdited(attribute);
            attribute.ValueOptions.CollectionChanged += (_, _) => OnAttributeEdited(attribute);
        }

        // TODO: persist Name/Type/Description/ValueOptions changes for this attribute definition once a
        // repository exists for its scope.
        private void OnAttributeEdited(AttributeDefinition attribute)
        {
        }

        private void WireAttributeValue(AttributeValue value) =>
            value.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(AttributeValue.HasValue))
                    OnDefaultValueEdited(value);
            };

        // TODO: persist this attribute's default value once a backend exists for its scope.
        private void OnDefaultValueEdited(AttributeValue value)
        {
        }

        // Rebuilds FilteredAttributes from Attributes (the currently-selected scope's bucket) + NameFilter,
        // in place — same Remove-then-Insert diffing MapsPageViewModel.ApplyFilters uses, so the ListBox's
        // selection survives a filter/tab change that still includes the selected attribute.
        private void ApplyFilter()
        {
            List<AttributeDefinition> matching = Attributes.Where(Matches).ToList();

            for (int i = FilteredAttributes.Count - 1; i >= 0; i--)
                if (!matching.Contains(FilteredAttributes[i]))
                    FilteredAttributes.RemoveAt(i);

            for (int i = 0; i < matching.Count; i++)
                if (!FilteredAttributes.Contains(matching[i]))
                    FilteredAttributes.Insert(i, matching[i]);
        }

        private bool Matches(AttributeDefinition attribute) =>
            string.IsNullOrWhiteSpace(NameFilter) || attribute.Name.Contains(NameFilter.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
