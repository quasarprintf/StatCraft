using System.Globalization;

namespace StatCraft.Models.GameData.Attributes
{
    // A single value is stored in sqlite as a text field, can be converted back/forth here
    //
    // Note this encoding has no representation of "unset" for Numeric/Bool/Percent — Parse degrades an
    // empty string to 0/false. Callers that need a null (map attribute values do) must track that
    // separately, by the presence or absence of the row, and never Parse a missing value.
    internal static class AttributeValueSerializer
    {
        internal readonly record struct ParsedValue(decimal? NumericValue, bool? BoolValue, decimal? PercentValue, string? SelectedValue);

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
            decimal? numeric = null, percent = null;
            bool? boolValue = null;
            string? selectedValue = null;

            switch (type)
            {
                case AttributeType.Numeric:
                    if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedNum))
                        numeric = parsedNum;
                    break;
                case AttributeType.Bool:
                    if (bool.TryParse(value, out bool parsedBool))
                        boolValue = parsedBool;
                    break;
                case AttributeType.Percent:
                    if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedPercent))
                        percent = parsedPercent;
                    break;
                case AttributeType.Values:
                    selectedValue = string.IsNullOrEmpty(value) ? null : value;
                    break;
            }

            return new ParsedValue(numeric, boolValue, percent, selectedValue);
        }
    }
}
