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
            vm.Closed += success => Close(success ? vm.LinkedAccount : null);
            Opened += async (_, _) => await vm.InitializeAsync();
        }

        private void OnProfileDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (e.Source is Control { DataContext: Sc2Profile } && _vm.ConfirmProfileCommand.CanExecute(null))
                _vm.ConfirmProfileCommand.Execute(null);
        }
    }
}
