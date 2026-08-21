using Avalonia.Markup.Xaml.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.ComponentModel;

namespace StatCraft.Models.GameData.Attributes
{
    public partial class AttributeValue : ObservableObject
    {
        public event EventHandler<PropertyChangedEventArgs>? ValueChanged;

        public AttributeDefinition Definition { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasValue))]
        private decimal? _numericValue;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasValue))]
        private bool? _boolValue;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasValue))]
        private decimal? _percentValue;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasValue))]
        private string? _selectedValue;

        // Only the slot matching the attribute's type counts — switching an attribute's type leaves the
        // old slot populated, and that stale value must not read as "set".
        public bool HasValue => Definition.Type switch
        {
            AttributeType.Numeric => NumericValue.HasValue,
            AttributeType.Bool => BoolValue.HasValue,
            AttributeType.Percent => PercentValue.HasValue,
            AttributeType.Values => !string.IsNullOrEmpty(SelectedValue),
            _ => false,
        };

        internal AttributeValue(AttributeDefinition attribute)
        {
            Definition = attribute;

            PropertyChanged += (_, e) => ValueChanged?.Invoke(this, e);
        }

        // Returns null when unset, so callers persist by deleting the row rather than writing a value
        // that would read back as 0/false.
        internal string? Serialize() => HasValue
            ? AttributeValueSerializer.Serialize(Definition.Type, NumericValue ?? 0m, BoolValue ?? false, PercentValue ?? 0m, SelectedValue)
            : null;

        internal void ApplyStoredValue(string stored)
        {
            AttributeValueSerializer.ParsedValue parsed = AttributeValueSerializer.Parse(Definition.Type, stored);
            switch (Definition.Type)
            {
                case AttributeType.Numeric: NumericValue = parsed.NumericValue; break;
                case AttributeType.Bool: BoolValue = parsed.BoolValue; break;
                case AttributeType.Percent: PercentValue = parsed.PercentValue; break;
                case AttributeType.Values: SelectedValue = parsed.SelectedValue; break;
            }
        }

        [RelayCommand]
        public void Clear()
        {
            NumericValue = null;
            BoolValue = null;
            PercentValue = null;
            SelectedValue = null;
        }

        public AttributeValue Clone()
        {
            return new AttributeValue(Definition)
                {
                    NumericValue = NumericValue,
                    BoolValue = BoolValue,
                    PercentValue = PercentValue,
                    SelectedValue = SelectedValue,
                };
        }
    }
}
