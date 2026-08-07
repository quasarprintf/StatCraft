using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Attributes.FixedAttribute;

namespace StatCraft.Models.GameData.Attributes.FixedAttribute
{
    // One map's value for one global MapAttribute.
    //
    // Every slot is nullable and all four start null, because a newly defined attribute is genuinely
    // unset on every map. That's the crucial difference from BuildAttribute, whose value slots are
    // non-nullable and default to 0/false — AttributeValueSerializer has no encoding for "unset", so
    // null is represented by the *absence* of a stored row and the serializer is only ever consulted
    // when HasValue is true.
    public partial class FixedAttributeValue : ObservableObject
    {
        public FixedAttribute Attribute { get; }

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
        public bool HasValue => Attribute.Type switch
        {
            AttributeType.Numeric => NumericValue.HasValue,
            AttributeType.Bool => BoolValue.HasValue,
            AttributeType.Percent => PercentValue.HasValue,
            AttributeType.Values => !string.IsNullOrEmpty(SelectedValue),
            _ => false,
        };

        internal FixedAttributeValue(FixedAttribute attribute)
        {
            Attribute = attribute;
        }

        // Returns null when unset, so callers persist by deleting the row rather than writing a value
        // that would read back as 0/false.
        internal string? Serialize() => HasValue
            ? AttributeValueSerializer.Serialize(Attribute.Type, NumericValue ?? 0m, BoolValue ?? false, PercentValue ?? 0m, SelectedValue)
            : null;

        internal void ApplyStoredValue(string stored)
        {
            AttributeValueSerializer.ParsedValue parsed = AttributeValueSerializer.Parse(Attribute.Type, stored);
            switch (Attribute.Type)
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
    }
}
