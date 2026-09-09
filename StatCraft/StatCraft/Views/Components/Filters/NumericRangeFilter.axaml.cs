using Avalonia;
using Avalonia.Controls;

namespace StatCraft.Views.Components.Filters;

// One [Min, Max] range filter slot. Stacked by default (the Maps and Builds side panes) or inline
// when Inline is set (the Data tab's horizontal bar). The inline form also uses narrower fields and
// whole numbers, since the only inline range is opponent MMR.
public partial class NumericRangeFilter : UserControl
{
    public static readonly StyledProperty<bool> InlineProperty =
        AvaloniaProperty.Register<NumericRangeFilter, bool>(nameof(Inline));

    public bool Inline
    {
        get => GetValue(InlineProperty);
        set => SetValue(InlineProperty, value);
    }

    public NumericRangeFilter()
    {
        InitializeComponent();
    }
}
