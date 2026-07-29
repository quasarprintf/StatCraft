using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace StatCraft.Views.Converters
{
    // TreeViewItem's FluentTheme reserves a chevron column of width TreeViewItemExpandCollapseChevronSize
    // plus TreeViewItemExpandCollapseChevronMargin on each side, even for leaf items (whose chevron is
    // just hidden, not removed). Content placed in a TreeDataTemplate only ever renders to the right of
    // that column, so pulling something left into the reserved chevron space needs a negative margin —
    // this derives that margin from the theme's own resources instead of a hardcoded pixel guess, so it
    // stays correct if those resources ever change.
    internal sealed class ChevronReservedSpaceConverter : IMultiValueConverter
    {
        public static readonly ChevronReservedSpaceConverter Instance = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values is [double chevronSize, Thickness chevronMargin])
                return new Thickness(-(chevronSize + chevronMargin.Left + chevronMargin.Right), 0, 0, 0);
            return AvaloniaProperty.UnsetValue;
        }
    }
}
