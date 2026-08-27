using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StatCraft.Views.Converters
{
    // Maps true/false to full/zero opacity. Avalonia's IsVisible="False" collapses a control out of
    // layout entirely (unlike WPF's Visibility.Hidden) — pairing this with IsHitTestVisible instead keeps
    // the control's space reserved while hiding and disabling it.
    public class BoolToOpacityConverter : IValueConverter
    {
        public static readonly BoolToOpacityConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is true ? 1.0 : 0.0;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
