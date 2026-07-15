using Avalonia.Controls;
using Avalonia.Input;
using StatCraft.Models.Battlenet;
using StatCraft.ViewModels;

namespace StatCraft.Views
{
    public partial class AccountPickerWindow : Window
    {
        private readonly AccountPickerViewModel? _vm;

        // Parameterless constructor required by the Avalonia XAML designer to create a design-time instance.
        public AccountPickerWindow()
        {
            InitializeComponent();
        }

        public AccountPickerWindow(AccountPickerViewModel vm) : this()
        {
            _vm = vm;
            DataContext = vm;
            vm.Closed += Close;
        }

        private void OnAccountDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (_vm == null) return;

            Sc2Profile? profile = (Sc2Profile?)((Control?)e.Source)?.DataContext;
            _vm.SelectAccount(profile);
        }
    }
}
