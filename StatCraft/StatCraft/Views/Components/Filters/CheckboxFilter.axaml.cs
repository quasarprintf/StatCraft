using Avalonia;
using Avalonia.Controls;

namespace StatCraft.Views.Components.Filters;

// One checkbox-list filter slot. Renders stacked by default (the Maps and Builds side panes) or
// inline when Inline is set (the Data tab's horizontal bar), since the two differ by more than
// orientation: only the stacked form carries a title/remove header and an "include unset" opt-in.
public partial class CheckboxFilter : UserControl
{
    public static readonly StyledProperty<bool> InlineProperty =
        AvaloniaProperty.Register<CheckboxFilter, bool>(nameof(Inline));

    public bool Inline
    {
        get => GetValue(InlineProperty);
        set => SetValue(InlineProperty, value);
    }

    public CheckboxFilter()
    {
        InitializeComponent();
    }
}
