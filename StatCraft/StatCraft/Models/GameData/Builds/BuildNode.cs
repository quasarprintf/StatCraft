using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Race;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace StatCraft.Models.GameData.Builds
{
    public partial class BuildNode : ObservableObject
    {
        public int Id { get; set; }

        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _description = string.Empty;
        [ObservableProperty] private bool _isExpanded;

        [ObservableProperty] private Race.Race _playerRace = Race.Race.Zerg;

        [NotifyPropertyChangedFor(nameof(VsZ), nameof(VsT), nameof(VsP))]
        [ObservableProperty] private Matchups _matchups = Race.Matchups.None;

        public bool VsZ => Matchups.HasFlag(Matchups.VsZ);
        public bool VsT => Matchups.HasFlag(Matchups.VsT);
        public bool VsP => Matchups.HasFlag(Matchups.VsP);

        // Transient UI-only flags driving the Builds tab's opponent-race and name/attribute filters; never
        // persisted. MatchesOpponentFilter is a plain per-node check — the child-⊆-parent matchup
        // invariant guarantees a matching child never has a non-matching parent, so it needs no help from
        // its descendants. MatchesFilter (name + attributes) has no such invariant (a child's name or
        // static attribute value has no relationship to its parent's), so
        // BuildsPageViewModel.RefreshFilterMatch folds in "or any descendant matches" when computing it,
        // to keep a match's ancestor chain visible.
        [NotifyPropertyChangedFor(nameof(IsVisibleInTree))]
        [ObservableProperty] private bool _matchesOpponentFilter = true;

        [NotifyPropertyChangedFor(nameof(IsVisibleInTree))]
        [ObservableProperty] private bool _matchesFilter = true;

        public bool IsVisibleInTree => MatchesOpponentFilter && MatchesFilter;

        public ObservableCollection<AttributeDefinition> Details { get; } = [];
        public ObservableCollection<AttributeValue> StaticAttributes { get; } = [];

        [NotifyPropertyChangedFor(nameof(HasChildren))]
        [ObservableProperty] private ObservableCollection<BuildNode> _children = [];
        public bool HasChildren => Children.Count > 0;

        public BuildNode()
        {
            Children.CollectionChanged += (s,e) => OnPropertyChanged(nameof(HasChildren));
        }

        [RelayCommand]
        public void AddAttribute(AttributeDefinition definition) 
        {
            StaticAttributes.Add(definition.DefaultValue.Clone());
        }

        [RelayCommand]
        public void RemoveAttribute(AttributeValue value)
        {
            StaticAttributes.Remove(value);
        }
    }
}
