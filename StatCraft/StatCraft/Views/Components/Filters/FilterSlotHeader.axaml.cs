using Avalonia.Controls;

namespace StatCraft.Views.Components.Filters;

// The title + remove button row every stacked filter slot starts with. Binds against the
// FilterSlotViewModel base rather than any one slot kind, so all three kinds drop it in as-is.
public partial class FilterSlotHeader : UserControl
{
    public FilterSlotHeader()
    {
        InitializeComponent();
    }
}
