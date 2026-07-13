using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using StatCraft.ViewModels;

namespace StatCraft.Views
{
    public partial class BuildsPage : UserControl
    {
        public BuildsPage()
        {
            InitializeComponent();
            DataContext = App.Services.GetRequiredService<BuildsPageViewModel>();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && IsVisible && DataContext is BuildsPageViewModel vm)
                vm.SelectFirstBuild();
        }
    }
}
