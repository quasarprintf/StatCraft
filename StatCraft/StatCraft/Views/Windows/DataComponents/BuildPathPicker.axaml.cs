using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace StatCraft.Views
{
    public partial class BuildPathPicker : UserControl
    {
        public BuildPathPicker()
        {
            InitializeComponent();
        }

        private void OnPickerButtonClick(object? sender, RoutedEventArgs e)
        {
            TreePopup.IsOpen = true;
        }

        private void OnNodeTapped(object? sender, TappedEventArgs e)
        {
            TreePopup.IsOpen = false;
        }
    }
}
