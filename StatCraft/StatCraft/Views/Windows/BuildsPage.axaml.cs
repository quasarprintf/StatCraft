using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using StatCraft.Models.GameData.Builds;
using StatCraft.ViewModels.Windows;
using StatCraft.Views.Components;

namespace StatCraft.Views.Windows
{
    public partial class BuildsPage : UserControl
    {
        private BuildsPageViewModel ViewModel => (BuildsPageViewModel)DataContext!;

        public BuildsPage()
        {
            InitializeComponent();

            BuildsPageViewModel vm = App.Services.GetRequiredService<BuildsPageViewModel>();
            vm.DeleteConfirmationRequested += async node => await OnDeleteConfirmationRequestedAsync(node);
            DataContext = vm;
        }

        // Right-clicking an opponent-race filter button adds/removes it from the filter set instead of
        // selecting it exclusively (which is what a left click / Command does).
        private void OnOpponentRaceRightTapped(object? sender, TappedEventArgs e)
        {
            if (sender is Button { DataContext: RaceOption option })
                ViewModel.ToggleOpponentRace(option.Value);
        }

        private async Task OnDeleteConfirmationRequestedAsync(BuildNode node)
        {
            if (!(TopLevel.GetTopLevel(this) is Window owner)) return;

            string message = $"\"{node.Name}\" has games recorded against it. Deleting it will remove " +
                "that build from those games and erase their recorded attribute values for it. Delete anyway?";
            bool confirmed = await new ConfirmationWindow(message).ShowDialog<bool>(owner);

            if (confirmed)
                ViewModel.ConfirmDeleteBuild(node);
        }
    }
}
