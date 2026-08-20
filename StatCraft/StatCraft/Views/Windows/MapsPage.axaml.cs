using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using StatCraft.Models.GameData.Maps;
using StatCraft.ViewModels.Windows;
using StatCraft.Views.Components;

namespace StatCraft.Views
{
    public partial class MapsPage : UserControl
    {
        public MapsPage()
        {
            InitializeComponent();

            MapsPageViewModel vm = App.Services.GetRequiredService<MapsPageViewModel>();
            vm.DeleteBlocked += async map => await OnDeleteBlockedAsync(map);
            DataContext = vm;
        }

        // Purely informational — unlike a build, a map with games on it can't be deleted at all, so
        // there's nothing to confirm.
        private async Task OnDeleteBlockedAsync(Map map)
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;

            string message = $"\"{map.Name}\" can't be deleted because games were played on it. " +
                "Delete those games first, or rename the map instead.";
            await new MessageWindow("Map In Use", message).ShowDialog(owner);
        }
    }
}
