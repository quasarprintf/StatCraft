using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StatCraft.ViewModels
{
    public enum Matchup { VsP, VsT, VsZ }

    public enum AttributeType { Numeric, Bool, Percent, Values }

    public partial class BuildAttribute : ObservableObject
    {
        public static IReadOnlyList<AttributeType> AllTypes { get; } =
            [AttributeType.Numeric, AttributeType.Bool, AttributeType.Percent, AttributeType.Values];

        [ObservableProperty] private string _name = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNumeric), nameof(IsBool), nameof(IsPercent), nameof(IsValues))]
        private AttributeType _type = AttributeType.Numeric;

        [ObservableProperty] private decimal _numericValue;
        [ObservableProperty] private bool _boolValue;
        [ObservableProperty] private decimal _percentValue;

        public ObservableCollection<string> ValueOptions { get; } = [];
        [ObservableProperty] private string? _selectedValue;

        public bool IsNumeric => Type == AttributeType.Numeric;
        public bool IsBool    => Type == AttributeType.Bool;
        public bool IsPercent => Type == AttributeType.Percent;
        public bool IsValues  => Type == AttributeType.Values;
    }

    public partial class BuildNode : ObservableObject
    {
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _description = string.Empty;
        public ObservableCollection<BuildAttribute> Attributes { get; } = [];
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
        public void AddAttribute() => SelectedBuild?.Attributes.Add(new BuildAttribute());

        [RelayCommand]
        public void SelectVsP() => SelectedMatchup = Matchup.VsP;

        [RelayCommand]
        public void SelectVsT() => SelectedMatchup = Matchup.VsT;

        [RelayCommand]
        public void SelectVsZ() => SelectedMatchup = Matchup.VsZ;
    }
}
