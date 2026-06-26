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
    }
}
