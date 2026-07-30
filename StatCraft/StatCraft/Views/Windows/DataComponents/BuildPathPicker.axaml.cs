using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using StatCraft.Models.GameData.Builds;
using StatCraft.ViewModels;

namespace StatCraft.Views
{
    public partial class BuildPathPicker : UserControl
    {
        private GameDataRowViewModel ViewModel => (GameDataRowViewModel)DataContext!;

        public BuildPathPicker()
        {
            InitializeComponent();
        }

        // Applies the selection (resolving the BuildNode from the ancestor MenuItem's DataContext, since
        // the Button's own DataContext here is just the Header's bound string) and then closes the flyout,
        // in that order — doing this via the Button's own Command binding instead doesn't work, because
        // Button.OnClick raises Click (running this handler) before reading Command/CommandParameter, so
        // hiding the flyout here would tear down those bindings before Command.Execute ever ran.
        private void OnBuildSelected(object? sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.FindAncestorOfType<MenuItem>()?.DataContext is BuildNode node)
                ViewModel.SelectBuildCommand.Execute(node);

            PickerButton.Flyout?.Hide();
        }
    }
}
