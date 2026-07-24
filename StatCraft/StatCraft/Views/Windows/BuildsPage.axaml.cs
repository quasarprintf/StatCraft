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
    }
}
