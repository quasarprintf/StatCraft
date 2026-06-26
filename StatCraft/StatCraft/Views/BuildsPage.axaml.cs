using Avalonia;
using Avalonia.Controls;
using StatCraft.ViewModels;

namespace StatCraft.Views
{
    public partial class BuildsPage : UserControl
    {
        public BuildsPage()
        {
            InitializeComponent();
            DataContext = new BuildsPageViewModel();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && IsVisible && DataContext is BuildsPageViewModel vm)
                vm.SelectFirstBuild();
        }
    }
}
