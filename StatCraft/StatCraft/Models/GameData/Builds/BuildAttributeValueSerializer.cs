using System.Globalization;
using StatCraft.ViewModels;

namespace StatCraft.Models.GameData.Builds
{
    // Shared string round-trip convention for an attribute value, used both for a BuildAttribute's
    // shared template default (BuildRepository) and a per-game value snapshot (GameDataRepository).
    internal static class BuildAttributeValueSerializer
    {
        internal readonly record struct ParsedValue(decimal NumericValue, bool BoolValue, decimal PercentValue, string? SelectedValue);

        internal static string Serialize(AttributeType type, decimal numericValue, bool boolValue, decimal percentValue, string? selectedValue) => type switch
        {
            AttributeType.Numeric => numericValue.ToString(CultureInfo.InvariantCulture),
            AttributeType.Bool => boolValue.ToString(CultureInfo.InvariantCulture),
            AttributeType.Percent => percentValue.ToString(CultureInfo.InvariantCulture),
            AttributeType.Values => selectedValue ?? string.Empty,
            _ => string.Empty,
        };

        internal static ParsedValue Parse(AttributeType type, string value)
        {
            decimal numeric = 0, percent = 0;
            bool boolValue = false;
            string? selectedValue = null;

            switch (type)
            {
                case AttributeType.Numeric:
                    decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out numeric);
                    break;
                case AttributeType.Bool:
                    bool.TryParse(value, out boolValue);
                    break;
                case AttributeType.Percent:
                    decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out percent);
                    break;
                case AttributeType.Values:
                    selectedValue = string.IsNullOrEmpty(value) ? null : value;
                    break;
            }

            return new ParsedValue(numeric, boolValue, percent, selectedValue);
        }
    }
}
