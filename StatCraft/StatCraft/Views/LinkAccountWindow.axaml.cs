using Avalonia.Controls;
using StatCraft.ViewModels;

namespace StatCraft.Views
{
    public partial class LinkAccountWindow : Window
    {
        public LinkAccountWindow(LinkAccountViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            vm.Closed += success => Close(success ? vm.LinkedAccount : null);
            Opened += async (_, _) => await vm.InitializeAsync();
        }
    }
}
