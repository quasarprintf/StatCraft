using Avalonia;
using Avalonia.Controls;

namespace StatCraft.Styles
{
    // DataGridColumn isn't a Control (no Classes property), so a column can't carry a style class the
    // way a normal control can. This attached property lets a column be marked in XAML anyway; the
    // owning page's code-behind reads it once its DataGrid loads and copies it onto the column's actual
    // generated DataGridColumnHeader as the "noSort" Classes entry, which the header's ControlTheme
    // override (see App.axaml) uses to hide the sort-arrow glyph and reclaim its reserved gutter.
    public static class DataGridColumnBehaviors
    {
        public static readonly AttachedProperty<bool> NoSortProperty =
            AvaloniaProperty.RegisterAttached<DataGridColumn, bool>("NoSort", typeof(DataGridColumnBehaviors));

        public static void SetNoSort(DataGridColumn column, bool value) => column.SetValue(NoSortProperty, value);
        public static bool GetNoSort(DataGridColumn column) => column.GetValue(NoSortProperty);
    }
}
