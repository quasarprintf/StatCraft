using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using StatCraft.Models.GameData.Race;
using StatCraft.Models.GameData.Attributes;

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

        // Transient UI-only flag driving the Builds tab's opponent-race filter; never persisted.
        [ObservableProperty] private bool _matchesOpponentFilter = true;

        public ObservableCollection<AttributeValue> Details { get; } = [];
        public ObservableCollection<AttributeValue> StaticAttributes { get; } = []; //NYI

        [NotifyPropertyChangedFor(nameof(HasChildren))]
        [ObservableProperty] private ObservableCollection<BuildNode> _children = [];
        public bool HasChildren => Children.Count > 0;

        public BuildNode()
        {
            Children.CollectionChanged += (s,e) => OnPropertyChanged(nameof(HasChildren));
        }
    }
}
