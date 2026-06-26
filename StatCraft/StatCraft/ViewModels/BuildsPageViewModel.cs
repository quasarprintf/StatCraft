using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StatCraft.ViewModels
{
    public enum Matchup { VsP, VsT, VsZ }

    public partial class BuildNode : ObservableObject
    {
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _notes = string.Empty;
        public ObservableCollection<BuildNode> Children { get; } = [];
    }

    public partial class BuildsPageViewModel : ViewModelBase
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsVsP), nameof(IsVsT), nameof(IsVsZ), nameof(CurrentView))]
        private Matchup _selectedMatchup = Matchup.VsP;

        [ObservableProperty] private BuildNode? _selectedBuild;

        public bool IsVsP => SelectedMatchup == Matchup.VsP;
        public bool IsVsT => SelectedMatchup == Matchup.VsT;
        public bool IsVsZ => SelectedMatchup == Matchup.VsZ;

        public Matchup CurrentView => SelectedMatchup;

        public ObservableCollection<BuildNode> Builds { get; } =
        [
            new BuildNode { Name = "Early aggression", Children = {
                new BuildNode { Name = "4-pool" },
                new BuildNode { Name = "6-pool" },
            }},
            new BuildNode { Name = "Economy" },
        ];

        [RelayCommand]
        public void SelectVsP() => SelectedMatchup = Matchup.VsP;

        [RelayCommand]
        public void SelectVsT() => SelectedMatchup = Matchup.VsT;

        [RelayCommand]
        public void SelectVsZ() => SelectedMatchup = Matchup.VsZ;
    }
}
