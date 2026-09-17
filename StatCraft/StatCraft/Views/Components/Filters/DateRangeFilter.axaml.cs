using Avalonia.Controls;

namespace StatCraft.Views.Components.Filters;

// One [FromDate, ToDate] date range filter slot, laid out like the other filter controls: title and
// remove button above, the two date pickers side by side below.
public partial class DateRangeFilter : UserControl
{
    public DateRangeFilter()
    {
        InitializeComponent();
    }
}
