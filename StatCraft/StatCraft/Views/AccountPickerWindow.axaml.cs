using Avalonia.Controls;
using Avalonia.Input;
using StatCraft.Models;
using StatCraft.ViewModels;

namespace StatCraft.Views
{
    public partial class AccountPickerWindow : Window
    {
        private readonly AccountPickerViewModel _vm;

        public AccountPickerWindow(AccountPickerViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            vm.Closed += Close;
        }

        private void OnAccountDoubleTapped(object? sender, TappedEventArgs e)
        {
            Sc2Profile? profile = (Sc2Profile?)((Control?)e.Source)?.DataContext;
            _vm.SelectAccount(profile);
        }
    }
}
