using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;

namespace StatCraft.ViewModels
{
    public partial class RaceOption(Race value) : ObservableObject
    {
        public Race Value { get; } = value;

        [ObservableProperty] private bool _isSelected;
    }

    public partial class BuildsPageViewModel : ViewModelBase
    {
        private readonly BuildRepository _repository;
        private readonly GameDataRepository _gameDataRepository;
        private readonly HashSet<Race> _loadedPlayerRaces = [];

        public BuildsPageViewModel(BuildRepository repository, GameDataRepository gameDataRepository)
        {
            _repository = repository;
            _gameDataRepository = gameDataRepository;
            PlayerRaceOptions = Enum.GetValues<Race>()
                .Select(r => new RaceOption(r) { IsSelected = r == PlayerRace })
                .ToList();
            OpponentRaceOptions = Enum.GetValues<Race>()
                .Select(r => new RaceOption(r))
                .ToList();
            LoadPlayerRaceIfNeeded(PlayerRace);
            RefreshOpponentFilter();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Builds))]
        private Race _playerRace = Race.Z;

        [ObservableProperty] private BuildNode? _selectedBuild;

        public IReadOnlyList<RaceOption> PlayerRaceOptions { get; }
        public IReadOnlyList<RaceOption> OpponentRaceOptions { get; }

        private readonly Dictionary<Race, ObservableCollection<BuildNode>> _buildsByPlayerRace =
            Enum.GetValues<Race>().ToDictionary(r => r, _ => new ObservableCollection<BuildNode>());

        public ObservableCollection<BuildNode> Builds => _buildsByPlayerRace[PlayerRace];

        [RelayCommand]
        public void SelectPlayerRace(Race race)
        {
            PlayerRace = race;
            foreach (RaceOption option in PlayerRaceOptions)
                option.IsSelected = option.Value == race;
            LoadPlayerRaceIfNeeded(race);
            RefreshOpponentFilter();
            SelectFirstBuild();
        }

        [RelayCommand]
        public void ToggleOpponentRace(Race race)
        {
            RaceOption? option = OpponentRaceOptions.FirstOrDefault(o => o.Value == race);
            if (option == null) return;

            option.IsSelected = !option.IsSelected;
            AfterOpponentFilterChanged();
        }

        [RelayCommand]
        public void SelectOpponentRaceExclusive(Race race)
        {
            foreach (RaceOption option in OpponentRaceOptions)
                option.IsSelected = option.Value == race;
            AfterOpponentFilterChanged();
        }

        private void AfterOpponentFilterChanged()
        {
            RefreshOpponentFilter();
            if (SelectedBuild == null || !SelectedBuild.MatchesOpponentFilter)
                SelectFirstBuild();
        }

        // Recomputes BuildNode.MatchesOpponentFilter (which drives the TreeView's per-item visibility)
        // for every currently-loaded build under the current PlayerRace, based on the union of every
        // currently-selected opponent race — a build matches if it supports at least one of them. The
        // child-⊆-parent matchup invariant still guarantees a non-matching node never has a matching
        // descendant under this OR'd filter (any bit a child has, its parent has too), so filtering
        // never orphans a visible child under a hidden parent.
        private void RefreshOpponentFilter()
        {
            Matchups flags = OpponentRaceOptions.Where(o => o.IsSelected)
                .Aggregate(Matchups.None, (acc, o) => acc | ToMatchupFlag(o.Value));
            foreach (BuildNode root in Builds)
                RefreshOpponentFilter(root, flags);
        }

        private static void RefreshOpponentFilter(BuildNode node, Matchups flags)
        {
            node.MatchesOpponentFilter = node.Matchups == Matchups.None || (node.Matchups & flags) != Matchups.None;
            foreach (BuildNode child in node.Children)
                RefreshOpponentFilter(child, flags);
        }

        private static Matchups ToMatchupFlag(Race race) => race switch
        {
            Race.Z => Matchups.VsZ,
            Race.T => Matchups.VsT,
            Race.P => Matchups.VsP,
            _ => Matchups.None,
        };

        private void LoadPlayerRaceIfNeeded(Race playerRace)
        {
            if (!_loadedPlayerRaces.Add(playerRace)) return;
            foreach (BuildNode node in _repository.GetBuildsForPlayerRace(playerRace))
            {
                WireNode(node);
                _buildsByPlayerRace[playerRace].Add(node);
            }
        }

        private void WireNode(BuildNode node)
        {
            node.PropertyChanged += (s, e) =>
            {
                if (s is BuildNode n && (e.PropertyName == nameof(BuildNode.Name) || e.PropertyName == nameof(BuildNode.Description)
                    || e.PropertyName == nameof(BuildNode.Matchups)))
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
                AttributeValueOptionSync.Apply(e, attr.Id, attr.ValueOptions, _repository.InsertValueOption, _repository.DeleteValueOption);
        }

        public void SelectFirstBuild() => SelectedBuild = Builds.FirstOrDefault(n => n.MatchesOpponentFilter);

        [RelayCommand]
        public void AddBuild()
        {
            BuildNode node = new BuildNode { Name = "New Build", PlayerRace = PlayerRace, Matchups = Matchups.VsZ | Matchups.VsT | Matchups.VsP };
            _repository.InsertBuild(node, null, Builds.Count);
            WireNode(node);
            Builds.Add(node);
            RefreshOpponentFilter();
            SelectedBuild = node;
        }

        [RelayCommand]
        public void AddChildBuild(BuildNode parent)
        {
            BuildNode node = new BuildNode { Name = "New Build", PlayerRace = parent.PlayerRace, Matchups = parent.Matchups };
            _repository.InsertBuild(node, parent.Id, parent.Children.Count);
            WireNode(node);
            parent.Children.Add(node);
            parent.IsExpanded = true;
            RefreshOpponentFilter();
            SelectedBuild = node;
        }

        [RelayCommand]
        public void ToggleMatchup(Race opponentRace)
        {
            if (SelectedBuild == null) return;

            Matchups flag = ToMatchupFlag(opponentRace);
            bool turningOn = !SelectedBuild.Matchups.HasFlag(flag);

            if (turningOn)
            {
                BuildNode? parent = FindParent(Builds, SelectedBuild);
                if (parent != null && !parent.Matchups.HasFlag(flag))
                    return;
                SelectedBuild.Matchups |= flag;
            }
            else
            {
                SelectedBuild.Matchups &= ~flag;
            }

            CascadeMatchupToDescendants(SelectedBuild, flag, turningOn);
            RefreshOpponentFilter();
        }

        private static void CascadeMatchupToDescendants(BuildNode node, Matchups flag, bool turningOn)
        {
            foreach (BuildNode child in node.Children)
            {
                child.Matchups = turningOn ? child.Matchups | flag : child.Matchups & ~flag;
                CascadeMatchupToDescendants(child, flag, turningOn);
            }
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
    }
}
