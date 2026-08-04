using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Attributes;

namespace StatCraft.Models.GameData.Maps
{
    // A map attribute *definition*. Unlike BuildAttribute this is global — it exists once and applies to
    // every map — so it deliberately carries no owning map and no default value. A map's actual value
    // lives in a separate MapAttributeValue, and a freshly defined attribute is unset everywhere until
    // someone fills it in.
    public partial class MapAttribute : ObservableObject
    {
        public static IReadOnlyList<AttributeType> AllTypes { get; } =
            [AttributeType.Numeric, AttributeType.Bool, AttributeType.Percent, AttributeType.Values];

        public int Id { get; set; }

        [ObservableProperty] private string _name = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNumeric), nameof(IsBool), nameof(IsPercent), nameof(IsValues))]
        private AttributeType _type = AttributeType.Numeric;

        // Options for a Values attribute. Owned by the single global definition, so editing them is
        // meant to be visible from every map — unlike BuildAttribute.Clone(), nothing copies this.
        public ObservableCollection<string> ValueOptions { get; } = [];

        [ObservableProperty] private string _newOptionText = string.Empty;

        public bool IsNumeric => Type == AttributeType.Numeric;
        public bool IsBool => Type == AttributeType.Bool;
        public bool IsPercent => Type == AttributeType.Percent;
        public bool IsValues => Type == AttributeType.Values;

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
