using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StatCraft.Views.Components
{
    public partial class MessageWindow : Window
    {
        // Parameterless constructor required by the Avalonia XAML designer to create a design-time instance.
        public MessageWindow()
        {
            InitializeComponent();
        }

        public MessageWindow(string title, string message) : this()
        {
            Title = title;
            MessageText.Text = message;
        }

        private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
    }
}
