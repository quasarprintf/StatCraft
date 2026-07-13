using Avalonia.Controls;
using Avalonia.Input;
using StatCraft.Models;
using StatCraft.ViewModels;

namespace StatCraft.Views
{
    public partial class LinkAccountWindow : Window
    {
        private readonly LinkAccountViewModel _vm;

        public LinkAccountWindow(LinkAccountViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            vm.Closed += success => Close(success ? vm.LinkedProfile : null);
            Opened += async (_, _) => await vm.InitializeAsync();
        }

        private void OnProfileDoubleTapped(object? sender, TappedEventArgs e)
        {
            _vm.ConfirmProfile((Sc2Profile?)((Control?)e.Source)?.DataContext);
        }
    }
}
