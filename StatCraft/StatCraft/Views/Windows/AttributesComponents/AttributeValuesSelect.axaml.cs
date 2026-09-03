using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace StatCraft.Views.Windows.AttributesComponents;

public partial class AttributeValuesSelect : UserControl
{
    public AttributeValuesSelect()
    {
        InitializeComponent();
    }

    private void OnAddAttributeOptionClicked(object? sender, RoutedEventArgs e)
    {
        //Click handler fires before Command handler, and hiding the flyout breaks CommandParameter binding
        //so delay the hide with dispatcher
        Dispatcher.UIThread.Post(() => AddAttributeButton.Flyout?.Hide());
    }
}