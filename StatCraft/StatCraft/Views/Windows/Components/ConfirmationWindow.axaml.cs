using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StatCraft.Views
{
    public partial class ConfirmationWindow : Window
    {
        // Parameterless constructor required by the Avalonia XAML designer to create a design-time instance.
        public ConfirmationWindow()
        {
            InitializeComponent();
        }

        public ConfirmationWindow(string message) : this()
        {
            MessageText.Text = message;
        }

        private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

        private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
    }
}
