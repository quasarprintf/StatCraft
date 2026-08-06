using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StatCraft.Models.GameData.Attributes
{
    // Shared shape between BuildAttribute (scoped to one build, carries its own default value) and
    // MapAttribute (global across every map, carries no value of its own — a map's value lives in a
    // separate MapAttributeValue). Everything about naming, typing, and the Values type's option list is
    // identical between the two; only what a default/current value even means differs, and that's left
    // to each subclass.
    public abstract partial class AttributeDefinition : ObservableObject
    {
        public static IReadOnlyList<AttributeType> AllTypes { get; } =
            [AttributeType.Numeric, AttributeType.Bool, AttributeType.Percent, AttributeType.Values];

        public int Id { get; set; }

        [ObservableProperty] private string _name = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNumeric), nameof(IsBool), nameof(IsPercent), nameof(IsValues))]
        private AttributeType _type = AttributeType.Numeric;

        // Options for a Values-type attribute. Setter is protected rather than absent so
        // BuildAttribute.Clone() can reassign it wholesale (a reference copy, not a deep one — existing
        // behavior); MapAttribute never assigns it, so its own ValueOptions is effectively immutable.
        public ObservableCollection<string> ValueOptions { get; protected set; } = [];

        [ObservableProperty] private string _newOptionText = string.Empty;

        public bool IsNumeric => Type == AttributeType.Numeric;
        public bool IsBool    => Type == AttributeType.Bool;
        public bool IsPercent => Type == AttributeType.Percent;
        public bool IsValues  => Type == AttributeType.Values;

        [RelayCommand]
        private void AddOption()
        {
            if (string.IsNullOrWhiteSpace(NewOptionText)) return;
            ValueOptions.Add(NewOptionText.Trim());
            NewOptionText = string.Empty;
        }

        [RelayCommand]
        private void RemoveOption(string option) => ValueOptions.Remove(option);
    }
}
