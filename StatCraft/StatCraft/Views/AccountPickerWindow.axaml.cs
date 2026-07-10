using Avalonia.Controls;
using StatCraft.ViewModels;

namespace StatCraft.Views
{
    public partial class AccountPickerWindow : Window
    {
        public AccountPickerWindow(AccountPickerViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            vm.Closed += result => Close(result);
        }
    }
}
