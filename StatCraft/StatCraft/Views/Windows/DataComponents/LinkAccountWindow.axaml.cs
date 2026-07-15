using Avalonia.Controls;
using Avalonia.Input;
using StatCraft.Models.Battlenet;
using StatCraft.ViewModels;

namespace StatCraft.Views
{
    public partial class LinkAccountWindow : Window
    {
        private readonly LinkAccountViewModel? _vm;

        // Parameterless constructor required by the Avalonia XAML designer to create a design-time instance.
        public LinkAccountWindow()
        {
            InitializeComponent();
        }

        public LinkAccountWindow(LinkAccountViewModel vm) : this()
        {
            _vm = vm;
            DataContext = vm;
            vm.Closed += success => Close(success ? vm.LinkedProfile : null);
            Opened += async (_, _) => await vm.InitializeAsync();
        }

        private void OnProfileDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (_vm == null) return;

            _vm.ConfirmProfile((Sc2Profile?)((Control?)e.Source)?.DataContext);
        }
    }
}
