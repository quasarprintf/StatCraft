using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Builds;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.ViewModels
{
    public enum Matchup { VsP, VsT, VsZ }

    public enum AttributeType { Numeric, Bool, Percent, Values }

    public partial class BuildsPageViewModel : ViewModelBase
    {
        private readonly BuildRepository _repository;
        private readonly GameDataRepository _gameDataRepository;
        private readonly HashSet<Matchup> _loadedMatchups = [];

        public BuildsPageViewModel(BuildRepository repository, GameDataRepository gameDataRepository)
        {
            _repository = repository;
            _gameDataRepository = gameDataRepository;
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
            foreach (BuildNode node in _repository.GetBuildsForMatchup(matchup))
            {
                WireNode(node);
                _buildsByMatchup[matchup].Add(node);
            }
        }

        private void WireNode(BuildNode node)
        {
            node.PropertyChanged += (s, e) =>
            {
                if (s is BuildNode n && (e.PropertyName == nameof(BuildNode.Name) || e.PropertyName == nameof(BuildNode.Description)))
                    _repository.UpdateBuild(n);
            };
            foreach (BuildAttribute attr in node.Attributes)
                WireAttribute(attr);
            foreach (BuildNode child in node.Children)
                WireNode(child);
        }

        private void WireAttribute(BuildAttribute attr)
        {
            attr.PropertyChanged += (s, e) =>
            {
                if (s is BuildAttribute a &&  (e.PropertyName == nameof(BuildAttribute.Name) || e.PropertyName == nameof(BuildAttribute.Type)
                    || e.PropertyName == nameof(BuildAttribute.NumericValue) || e.PropertyName == nameof(BuildAttribute.BoolValue)
                    || e.PropertyName == nameof(BuildAttribute.PercentValue) || e.PropertyName == nameof(BuildAttribute.SelectedValue)))
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
            BuildNode node = new BuildNode { Name = "New Build" };
            _repository.InsertBuild(node, SelectedMatchup, null, Builds.Count);
            WireNode(node);
            Builds.Add(node);
            SelectedBuild = node;
        }

        [RelayCommand]
        public void AddChildBuild(BuildNode parent)
        {
            BuildNode node = new BuildNode { Name = "New Build" };
            _repository.InsertBuild(node, SelectedMatchup, parent.Id, parent.Children.Count);
            WireNode(node);
            parent.Children.Add(node);
            parent.IsExpanded = true;
            SelectedBuild = node;
        }

        // Raised instead of deleting immediately when the build (or a descendant, since deleting a
        // parent cascades its whole subtree) has games recorded against it. The view shows a
        // confirmation dialog and, if accepted, calls ConfirmDeleteBuild.
        public event Action<BuildNode>? DeleteConfirmationRequested;

        [RelayCommand]
        public void DeleteBuild(BuildNode node)
        {
            if (_gameDataRepository.IsAnyBuildReferenced(CollectSubtreeIds(node)))
            {
                DeleteConfirmationRequested?.Invoke(node);
                return;
            }

            PerformDelete(node);
        }

        public void ConfirmDeleteBuild(BuildNode node) => PerformDelete(node);

        private void PerformDelete(BuildNode node)
        {
            bool needsReselect = SelectedBuild == node || (SelectedBuild != null && ContainsDescendant(node, SelectedBuild));
            BuildNode? replacement = needsReselect ? FindReplacementSelection(node) : null;

            _repository.DeleteBuild(node.Id);
            RemoveNode(Builds, node);

            if (needsReselect)
                SelectedBuild = replacement;
        }

        private static IEnumerable<int> CollectSubtreeIds(BuildNode node)
        {
            yield return node.Id;
            foreach (BuildNode child in node.Children)
                foreach (int id in CollectSubtreeIds(child))
                    yield return id;
        }

        private BuildNode? FindReplacementSelection(BuildNode node)
        {
            BuildNode? parent = FindParent(Builds, node);
            if (parent != null) return parent;

            int index = Builds.IndexOf(node);
            if (index > 0) return Builds[index - 1];

            return Builds.Count > 1 ? Builds[1] : null;
        }

        private static BuildNode? FindParent(ObservableCollection<BuildNode> nodes, BuildNode target)
        {
            foreach (BuildNode n in nodes)
            {
                if (n.Children.Contains(target)) return n;
                BuildNode? found = FindParent(n.Children, target);
                if (found != null) return found;
            }
            return null;
        }

        private static bool RemoveNode(ObservableCollection<BuildNode> nodes, BuildNode target)
        {
            if (nodes.Remove(target)) return true;
            foreach (BuildNode node in nodes)
                if (RemoveNode(node.Children, target)) return true;
            return false;
        }

        private static bool ContainsDescendant(BuildNode root, BuildNode target)
        {
            foreach (BuildNode child in root.Children)
                if (child == target || ContainsDescendant(child, target)) return true;
            return false;
        }

        [RelayCommand]
        public void AddAttribute()
        {
            if (SelectedBuild == null) return;
            BuildAttribute attr = new BuildAttribute();
            _repository.InsertAttribute(attr, SelectedBuild.Id, SelectedBuild.Attributes.Count);
            WireAttribute(attr);
            SelectedBuild.Attributes.Add(attr);
        }

        [RelayCommand]
        public void RemoveAttribute(BuildAttribute attribute)
        {
            if (SelectedBuild == null) return;
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
