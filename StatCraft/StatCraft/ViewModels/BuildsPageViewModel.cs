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
        [ObservableProperty] private string _newOptionText = string.Empty;

        public bool IsNumeric => Type == AttributeType.Numeric;
        public bool IsBool    => Type == AttributeType.Bool;
        public bool IsPercent => Type == AttributeType.Percent;
        public bool IsValues  => Type == AttributeType.Values;

        [RelayCommand]
        private void AddOption()
        {
            if (string.IsNullOrWhiteSpace(NewOptionText)) return;
            ValueOptions.Add(NewOptionText.Trim());
            NewOptionText = string.Empty;
        }

        [RelayCommand]
        private void RemoveOption(string option) => ValueOptions.Remove(option);
    }

    public partial class BuildNode : ObservableObject
    {
        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _description = string.Empty;
        [ObservableProperty] private bool _isExpanded;
        public ObservableCollection<BuildAttribute> Attributes { get; } = [];
        public ObservableCollection<BuildNode> Children { get; } = [];
    }

    public partial class BuildsPageViewModel : ViewModelBase
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsVsP), nameof(IsVsT), nameof(IsVsZ), nameof(CurrentView), nameof(Builds))]
        private Matchup _selectedMatchup = Matchup.VsP;

        [ObservableProperty] private BuildNode? _selectedBuild;

        public bool IsVsP => SelectedMatchup == Matchup.VsP;
        public bool IsVsT => SelectedMatchup == Matchup.VsT;
        public bool IsVsZ => SelectedMatchup == Matchup.VsZ;

        public Matchup CurrentView => SelectedMatchup;

        private readonly Dictionary<Matchup, ObservableCollection<BuildNode>> _buildsByMatchup = new()
        {
            [Matchup.VsP] = [],
            [Matchup.VsT] = [],
            [Matchup.VsZ] = [],
        };

        public ObservableCollection<BuildNode> Builds => _buildsByMatchup[SelectedMatchup];

        partial void OnSelectedMatchupChanged(Matchup value) => SelectFirstBuild();

        public void SelectFirstBuild() => SelectedBuild = Builds.Count > 0 ? Builds[0] : null;

        [RelayCommand]
        public void AddBuild()
        {
            var node = new BuildNode { Name = "New Build" };
            Builds.Add(node);
            SelectedBuild = node;
        }

        [RelayCommand]
        public void AddChildBuild()
        {
            if (SelectedBuild is null) return;
            var parent = SelectedBuild;
            var node = new BuildNode { Name = "New Build" };
            parent.Children.Add(node);
            parent.IsExpanded = true;
            SelectedBuild = node;
        }

        [RelayCommand]
        public void AddAttribute() => SelectedBuild?.Attributes.Add(new BuildAttribute());

        [RelayCommand]
        public void RemoveAttribute(BuildAttribute attribute) => SelectedBuild?.Attributes.Remove(attribute);

        [RelayCommand]
        public void SelectVsP() => SelectedMatchup = Matchup.VsP;

        [RelayCommand]
        public void SelectVsT() => SelectedMatchup = Matchup.VsT;

        [RelayCommand]
        public void SelectVsZ() => SelectedMatchup = Matchup.VsZ;
    }
}
