using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Attributes;

namespace StatCraft.ViewModels.Windows
{
    public partial class AttributesPageViewModel : ViewModelBase
    {
        private readonly Dictionary<AttributeScope, ObservableCollection<AttributeDefinition>> _attributesByScope = new()
        {
            [AttributeScope.Game] = [],
            [AttributeScope.Build] = [],
            [AttributeScope.Map] = [],
        };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Attributes))]
        private AttributeScope _selectedScope;

        public ObservableCollection<AttributeDefinition> Attributes => _attributesByScope[SelectedScope];

        public ObservableCollection<AttributeDefinition> FilteredAttributes { get; } = [];

        [ObservableProperty] private string _nameFilter = "";

        [ObservableProperty] private AttributeDefinition? _selectedAttribute;

        // A definition has no "current value" of its own — this wraps SelectedAttribute the same way
        // BuildDetailsPanel/MapDetailsPanel wrap one per row, purely so the details panel's default-value
        // editor has an AttributeValue to bind against.
        [ObservableProperty] private AttributeValue? _selectedAttributeValue;

        partial void OnSelectedScopeChanged(AttributeScope value) => ApplyFilter();

        partial void OnNameFilterChanged(string value) => ApplyFilter();

        [RelayCommand]
        public void SetScope(AttributeScope scope)
        {
            SelectedScope = scope;
        }

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

        private void WireAttribute(AttributeDefinition attribute)
        {
            attribute.PropertyChanged += (_, _) => OnAttributeEdited(attribute);
            attribute.ValueOptions.CollectionChanged += (_, _) => OnAttributeEdited(attribute);
        }

        
        private void OnAttributeEdited(AttributeDefinition attribute)
        {
            // TODO: persist Name/Type/Description/ValueOptions changes for this attribute definition once a repository exists for its scope.
        }

        private void WireAttributeValue(AttributeValue value)
        {
            value.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(AttributeValue.HasValue))
                    OnDefaultValueEdited(value);
            };
        }

        private void OnDefaultValueEdited(AttributeValue value)
        {
            // TODO: persist this attribute's default value once a backend exists for its scope.
        }

        private void ApplyFilter()
        {
            bool Matches(AttributeDefinition attribute)
            {
                return string.IsNullOrWhiteSpace(NameFilter) || attribute.Name.Contains(NameFilter.Trim(), StringComparison.OrdinalIgnoreCase);
            }

            List<AttributeDefinition> matching = Attributes.Where(Matches).ToList();

            for (int i = FilteredAttributes.Count - 1; i >= 0; i--)
                if (!matching.Contains(FilteredAttributes[i]))
                    FilteredAttributes.RemoveAt(i);

            for (int i = 0; i < matching.Count; i++)
                if (!FilteredAttributes.Contains(matching[i]))
                    FilteredAttributes.Insert(i, matching[i]);
        }
    }
}
