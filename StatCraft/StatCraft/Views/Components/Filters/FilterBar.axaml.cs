using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace StatCraft.Views.Components.Filters;

public partial class FilterBar : UserControl
{
    public static readonly StyledProperty<Avalonia.Layout.Orientation> OrientationProperty =
        AvaloniaProperty.Register<FilterBar, Avalonia.Layout.Orientation>(nameof(Orientation), Avalonia.Layout.Orientation.Horizontal);
    public Avalonia.Layout.Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<FilterBar, double>(nameof(Spacing), 8);
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public FilterBar()
    {
        InitializeComponent();
    }
}