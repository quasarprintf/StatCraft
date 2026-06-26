using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StatCraft.ViewModels
{
    public enum Matchup { VsP, VsT, VsZ }

    public partial class BuildsPageViewModel : ViewModelBase
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsVsP), nameof(IsVsT), nameof(IsVsZ), nameof(CurrentView))]
        private Matchup _selectedMatchup = Matchup.VsP;

        public bool IsVsP => SelectedMatchup == Matchup.VsP;
        public bool IsVsT => SelectedMatchup == Matchup.VsT;
        public bool IsVsZ => SelectedMatchup == Matchup.VsZ;

        public Matchup CurrentView => SelectedMatchup;

        [RelayCommand]
        public void SelectVsP() => SelectedMatchup = Matchup.VsP;

        [RelayCommand]
        public void SelectVsT() => SelectedMatchup = Matchup.VsT;

        [RelayCommand]
        public void SelectVsZ() => SelectedMatchup = Matchup.VsZ;
    }
}
