using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Services;

namespace StatCraft.ViewModels
{
    public enum Matchup { VsP, VsT, VsZ }

    public enum AttributeType { Numeric, Bool, Percent, Values }

    public partial class BuildAttribute : ObservableObject
    {
        public static IReadOnlyList<AttributeType> AllTypes { get; } =
            [AttributeType.Numeric, AttributeType.Bool, AttributeType.Percent, AttributeType.Values];

        public int Id { get; set; }

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
        public int Id { get; set; }

        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _description = string.Empty;
        [ObservableProperty] private bool _isExpanded;
        public ObservableCollection<BuildAttribute> Attributes { get; } = [];
        public ObservableCollection<BuildNode> Children { get; } = [];
    }

    public partial class BuildsPageViewModel : ViewModelBase
    {
        private readonly BuildRepository _repository;
        private readonly HashSet<Matchup> _loadedMatchups = [];

        public BuildsPageViewModel(BuildRepository repository)
        {
            _repository = repository;
            LoadMatchupIfNeeded(Matchup.VsP);
        }

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

        partial void OnSelectedMatchupChanged(Matchup value)
        {
            LoadMatchupIfNeeded(value);
            SelectFirstBuild();
        }

        private void LoadMatchupIfNeeded(Matchup matchup)
        {
            if (!_loadedMatchups.Add(matchup)) return;
            foreach (var node in _repository.GetBuildsForMatchup(matchup))
            {
                WireNode(node);
                _buildsByMatchup[matchup].Add(node);
            }
        }

        private void WireNode(BuildNode node)
        {
            node.PropertyChanged += (s, e) =>
            {
                if (s is BuildNode n && e.PropertyName is nameof(BuildNode.Name) or nameof(BuildNode.Description))
                    _repository.UpdateBuild(n);
            };
            foreach (var attr in node.Attributes)
                WireAttribute(attr);
            foreach (var child in node.Children)
                WireNode(child);
        }

        private void WireAttribute(BuildAttribute attr)
        {
            attr.PropertyChanged += (s, e) =>
            {
                if (s is BuildAttribute a && e.PropertyName is nameof(BuildAttribute.Name) or nameof(BuildAttribute.Type)
                    or nameof(BuildAttribute.NumericValue) or nameof(BuildAttribute.BoolValue)
                    or nameof(BuildAttribute.PercentValue) or nameof(BuildAttribute.SelectedValue))
                    _repository.UpdateAttribute(a);
            };
            attr.ValueOptions.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                    foreach (string? value in e.NewItems)
                        if (value != null)
                            _repository.InsertValueOption(attr.Id, value, attr.ValueOptions.IndexOf(value));
                if (e.OldItems != null)
                    foreach (string? value in e.OldItems)
                        if (value != null)
                            _repository.DeleteValueOption(attr.Id, value);
            };
        }

        public void SelectFirstBuild() => SelectedBuild = Builds.Count > 0 ? Builds[0] : null;

        [RelayCommand]
        public void AddBuild()
        {
            var node = new BuildNode { Name = "New Build" };
            _repository.InsertBuild(node, SelectedMatchup, null, Builds.Count);
            WireNode(node);
            Builds.Add(node);
            SelectedBuild = node;
        }

        [RelayCommand]
        public void AddChildBuild()
        {
            if (SelectedBuild is null) return;
            var parent = SelectedBuild;
            var node = new BuildNode { Name = "New Build" };
            _repository.InsertBuild(node, SelectedMatchup, parent.Id, parent.Children.Count);
            WireNode(node);
            parent.Children.Add(node);
            parent.IsExpanded = true;
            SelectedBuild = node;
        }

        [RelayCommand]
        public void AddAttribute()
        {
            if (SelectedBuild is null) return;
            var attr = new BuildAttribute();
            _repository.InsertAttribute(attr, SelectedBuild.Id, SelectedBuild.Attributes.Count);
            WireAttribute(attr);
            SelectedBuild.Attributes.Add(attr);
        }

        [RelayCommand]
        public void RemoveAttribute(BuildAttribute attribute)
        {
            if (SelectedBuild is null) return;
            _repository.DeleteAttribute(attribute.Id);
            SelectedBuild.Attributes.Remove(attribute);
        }

        [RelayCommand]
        public void SelectVsP() => SelectedMatchup = Matchup.VsP;

        [RelayCommand]
        public void SelectVsT() => SelectedMatchup = Matchup.VsT;

        [RelayCommand]
        public void SelectVsZ() => SelectedMatchup = Matchup.VsZ;
    }
}
