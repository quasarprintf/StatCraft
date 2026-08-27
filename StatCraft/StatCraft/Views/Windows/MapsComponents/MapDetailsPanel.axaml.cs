using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StatCraft.Views.Windows.MapsComponents
{
    public partial class MapDetailsPanel : UserControl
    {
        public MapDetailsPanel()
        {
            InitializeComponent();
        }

        // The flyout's ItemsControl is plain Buttons, not MenuItems, so nothing closes it automatically
        // once one is clicked (unlike a MenuFlyout) — close it explicitly alongside the AddAttribute call.
        private void OnAddAttributeOptionClicked(object? sender, RoutedEventArgs e) => AddAttributeButton.Flyout?.Hide();
    }
}
