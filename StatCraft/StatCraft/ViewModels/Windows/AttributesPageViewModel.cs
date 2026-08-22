using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Services.DatabaseRepository;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace StatCraft.ViewModels.Windows
{
    public partial class AttributesPageViewModel : ViewModelBase
    {
        private readonly AttributeRepository _repository;

        private readonly Dictionary<AttributeScope, ObservableCollection<AttributeDefinition>> _attributesByScope = new()
        {
            [AttributeScope.Game] = [],
            [AttributeScope.Build] = [],
            [AttributeScope.Map] = [],
        };

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Attributes))]
        [NotifyPropertyChangedFor(nameof(SelectedGame))]
        [NotifyPropertyChangedFor(nameof(SelectedBuild))]
        [NotifyPropertyChangedFor(nameof(SelectedMap))]
        private AttributeScope _selectedScope;

        public bool SelectedGame => _selectedScope == AttributeScope.Game;
        public bool SelectedBuild => _selectedScope == AttributeScope.Build;
        public bool SelectedMap => _selectedScope == AttributeScope.Map;

        public ObservableCollection<AttributeDefinition> Attributes => _attributesByScope[SelectedScope];

        public ObservableCollection<AttributeDefinition> FilteredAttributes { get; } = [];

        [ObservableProperty] private string _nameFilter = "";

        [NotifyPropertyChangedFor(nameof(SelectedAttributeValue))]
        [ObservableProperty] private AttributeDefinition? _selectedAttribute;
        public AttributeValue? SelectedAttributeValue => _selectedAttribute?.DefaultValue;

        public AttributesPageViewModel(AttributeRepository attributeRepository)
        {
            _repository = attributeRepository;


            var scopeGroups = _repository.GetAllAttributes().GroupBy(a => a.Scope);
            foreach (var group in scopeGroups ) 
            {
                _attributesByScope[group.Key] = new ObservableCollection<AttributeDefinition>(group.ToArray());
            }

            SelectedScope = AttributeScope.Game;
        }

        partial void OnSelectedScopeChanged(AttributeScope value) => ApplyFilter();

        partial void OnNameFilterChanged(string value) => ApplyFilter();

        [RelayCommand]
        public void SetScope(AttributeScope scope)
        {
            SelectedScope = scope;
        }

        partial void OnSelectedAttributeChanging(AttributeDefinition? value)
        {
            if (SelectedAttribute != null)
                UnWireAttribute(SelectedAttribute);
            if (value != null)
                WireAttribute(value);
        }

        [RelayCommand]
        private void AddAttribute()
        {
            AttributeDefinition attribute = new(SelectedScope) { Name = "New Attribute" };
            Attributes.Add(attribute);
            ApplyFilter();
            SelectedAttribute = attribute;

            _repository.InsertAttribute(attribute, Attributes.Count);
        }

        [RelayCommand]
        private void DeleteAttribute(AttributeDefinition attribute)
        {
            Attributes.Remove(attribute);
            ApplyFilter();
            if (SelectedAttribute == attribute)
                SelectedAttribute = FilteredAttributes.FirstOrDefault();

            _repository.DeleteAttribute(attribute.Id);
        }

        private void WireAttribute(AttributeDefinition attribute)
        {
            attribute.DefinitionChanged += OnAttributeEdited;
            attribute.ValueOptionsChanged += OnAttributeValuesEdited;

            attribute.DefaultValue.ValueChanged += OnDefaultValueEdited;
        }
        private void UnWireAttribute(AttributeDefinition attribute)
        {
            attribute.DefinitionChanged -= OnAttributeEdited;
            attribute.ValueOptionsChanged -= OnAttributeValuesEdited;

            attribute.DefaultValue.ValueChanged -= OnDefaultValueEdited;
        }

        private void OnAttributeEdited(object? sender, PropertyChangedEventArgs args)
        {
            _repository.UpdateAttribute(SelectedAttribute!);
        }
        private void OnAttributeValuesEdited(object? sender, CollectionChangeEventArgs args)
        {
            switch (args.Action)
            {
                case CollectionChangeAction.Add:
                    _repository.InsertValueOption(SelectedAttribute!.Id, (string)args.Element!);
                    return;
                case CollectionChangeAction.Remove:
                    _repository.DeleteValueOption(SelectedAttribute!.Id, (string)args.Element!);
                    return;
            }
        }

        private void OnDefaultValueEdited(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(AttributeValue.HasValue))
                _repository.UpdateAttribute(SelectedAttribute!);
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
