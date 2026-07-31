using System.Collections.ObjectModel;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.ViewModels
{
    // One tab on the Data page's "other players" side: an ally or opponent's own build tracker, plus
    // enough display info to label the tab.
    public partial class OtherPlayerBuildViewModel : ViewModelBase
    {
        public string TabHeader { get; }
        public PlayerBuildTrackerViewModel Tracker { get; }

        internal OtherPlayerBuildViewModel(string role, GamePlayer player, GameDataRepository repository, ObservableCollection<BuildNode>? buildTree)
        {
            string clanAndName = $"{player.FormattedClan} {player.Name}".Trim();
            TabHeader = $"{role}: {clanAndName}";
            Tracker = new PlayerBuildTrackerViewModel(player, repository, buildTree);
        }
    }
}
