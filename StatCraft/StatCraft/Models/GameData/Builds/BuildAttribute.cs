using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData.Attributes;

namespace StatCraft.Models.GameData.Builds
{
    // Scoped to one build node, and — unlike MapAttribute — carries its own default value, which every
    // game recorded against the build starts from until overridden per game.
    public partial class BuildAttribute : AttributeDefinition
    {
        [ObservableProperty] private decimal _numericValue;
        [ObservableProperty] private bool _boolValue;
        [ObservableProperty] private decimal _percentValue;
        [ObservableProperty] private string? _selectedValue;

        internal string SerializeValue() =>
            AttributeValueSerializer.Serialize(Type, NumericValue, BoolValue, PercentValue, SelectedValue);

        internal void ApplyValue(string value)
        {
            AttributeValueSerializer.ParsedValue parsed = AttributeValueSerializer.Parse(Type, value);
            switch (Type)
            {
                case AttributeType.Numeric:
                    NumericValue = parsed.NumericValue;
                    break;
                case AttributeType.Bool:
                    BoolValue = parsed.BoolValue;
                    break;
                case AttributeType.Percent:
                    PercentValue = parsed.PercentValue;
                    break;
                case AttributeType.Values:
                    SelectedValue = parsed.SelectedValue;
                    break;
            }
        }

        public BuildAttribute Clone()
        {
            return new BuildAttribute()
            {
                Id = this.Id,
                Name = this.Name,
                Type = this.Type,
                NumericValue = this.NumericValue,
                BoolValue = this.BoolValue,
                PercentValue = this.PercentValue,
                ValueOptions = this.ValueOptions,
                SelectedValue = this.SelectedValue,
                NewOptionText = this.NewOptionText,
            };
        }
    }
}
