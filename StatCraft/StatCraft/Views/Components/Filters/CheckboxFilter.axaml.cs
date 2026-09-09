using Avalonia;
using Avalonia.Controls;

namespace StatCraft.Views.Components.Filters;

// One checkbox-list filter slot. Renders stacked by default (the Maps and Builds side panes) or
// inline when Inline is set (the Data tab's horizontal bar), since the two differ by more than
// orientation: only the stacked form carries a title/remove header and an "include unset" opt-in.
public partial class CheckboxFilter : UserControl
{
    public CheckboxFilter()
    {
        InitializeComponent();
    }
}
