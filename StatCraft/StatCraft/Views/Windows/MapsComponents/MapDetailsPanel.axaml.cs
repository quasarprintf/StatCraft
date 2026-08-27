using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

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
        // Button.OnClick raises this Click event before it reads CommandParameter and invokes Command, so
        // hiding the flyout synchronously here tears down the (now-recycled) item container first and
        // CommandParameter arrives null. Posting the Hide() defers it to the next dispatcher pass, after
        // this click's Command.Execute has already run against the still-live container.
        private void OnAddAttributeOptionClicked(object? sender, RoutedEventArgs e) =>
            Dispatcher.UIThread.Post(() => AddAttributeButton.Flyout?.Hide());
    }
}
