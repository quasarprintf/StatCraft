using Avalonia.Controls;

namespace StatCraft.Views.Components.Filters;

// One three-state bool filter slot. Stacked-only, unlike the other two kinds: bool slots come from
// map/build attributes, so they never appear in the Data tab's inline bar.
public partial class BoolFilter : UserControl
{
    public BoolFilter()
    {
        InitializeComponent();
    }
}
