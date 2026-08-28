using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StatCraft.Models.GameData.Attributes
{
    public partial class AttributeDefinition : ObservableObject
    {
        public event EventHandler<PropertyChangedEventArgs>? DefinitionChanged;
        public event EventHandler<CollectionChangeEventArgs>? ValueOptionsChanged; 

        public static IReadOnlyList<AttributeType> AllTypes { get; } =
            [AttributeType.Numeric, AttributeType.Bool, AttributeType.Percent, AttributeType.Values];

        public int Id { get; set; }

        [ObservableProperty] private string _name = string.Empty;

        [ObservableProperty] private string _description = string.Empty;

        [NotifyPropertyChangedFor(nameof(IsNumeric), nameof(IsBool), nameof(IsPercent), nameof(IsValues))]
        [ObservableProperty] private AttributeType _type = AttributeType.Numeric;

        public AttributeScope Scope { get; private set; }
        [ObservableProperty] private bool _isMandatory;
        public bool IsNullable => Scope != AttributeScope.BuildDetail;

        // Options for a Values-type attribute
        public ObservableCollection<string> ValueOptions { get; protected set; } = [];

        [ObservableProperty] private string _newOptionText = string.Empty;

        public bool IsNumeric => Type == AttributeType.Numeric;
        public bool IsBool    => Type == AttributeType.Bool;
        public bool IsPercent => Type == AttributeType.Percent;
        public bool IsValues  => Type == AttributeType.Values;

        public bool IsGameAttribute => Scope == AttributeScope.Game;
        public bool IsBuildAttribute => Scope == AttributeScope.Build;
        public bool IsBuildDetailAttribute => Scope == AttributeScope.BuildDetail;
        public bool IsMapAttribute => Scope == AttributeScope.Map;

        public AttributeValue DefaultValue { get; set; }

        public AttributeDefinition(AttributeScope scope)
        {
            Scope = scope;
            DefaultValue = new AttributeValue(this);

            PropertyChanged += (_, e) => { if (e.PropertyName != nameof(NewOptionText)) DefinitionChanged?.Invoke(this, e); };
        }
        public AttributeDefinition(AttributeScope scope, AttributeType type, string rawDefaultValue) : this(scope)
        {
            Type = type;
            DefaultValue.ApplyStoredValue(rawDefaultValue);
        }

        [RelayCommand]
        private void AddOption()
        {
            if (string.IsNullOrWhiteSpace(NewOptionText)) return;
            ValueOptions.Add(NewOptionText.Trim());
            CollectionChangeEventArgs eventArgs = new CollectionChangeEventArgs(CollectionChangeAction.Add, NewOptionText);
            NewOptionText = string.Empty;

            if (!IsNullable && ValueOptions.Count == 1)
                DefaultValue.SelectedValue = ValueOptions[0];

            ValueOptionsChanged?.Invoke(this, eventArgs);
        }

        [RelayCommand]
        private void RemoveOption(string option)
        {
            ValueOptions.Remove(option);
            CollectionChangeEventArgs eventArgs = new CollectionChangeEventArgs(CollectionChangeAction.Remove, option);
            if (!IsNullable && ValueOptions.Count > 0 && (DefaultValue.SelectedValue == null || DefaultValue.SelectedValue == option))
                DefaultValue.SelectedValue = ValueOptions[0];

            ValueOptionsChanged?.Invoke(this, eventArgs);
        }
    }
}
