using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using StatCraft.Models.GameData.Race;
using System.Text;
using StatCraft.ViewModels;

namespace StatCraft.Models.GameData.Builds
{
    public partial class BuildNode : ObservableObject
    {
        public int Id { get; set; }

        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _description = string.Empty;
        [ObservableProperty] private bool _isExpanded;

        [ObservableProperty] private Race.Race _playerRace = Race.Race.Z;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(VsZ), nameof(VsT), nameof(VsP))]
        private Matchups _matchups = Race.Matchups.None;

        public bool VsZ => Matchups.HasFlag(Matchups.VsZ);
        public bool VsT => Matchups.HasFlag(Matchups.VsT);
        public bool VsP => Matchups.HasFlag(Matchups.VsP);

        // Transient UI-only flag driving the Builds tab's opponent-race filter; never persisted.
        [ObservableProperty] private bool _matchesOpponentFilter = true;

        // Transient UI-only nesting depth (0 = root), set when the tree is loaded/built; never persisted.
        // Drives DepthTicks, which the TreeView's item template renders as a per-level guide line.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DepthTicks))]
        private int _depth;

        public IEnumerable<int> DepthTicks => Enumerable.Range(0, Depth);

        public ObservableCollection<BuildAttribute> Attributes { get; } = [];
        public ObservableCollection<BuildNode> Children { get; } = [];
    }
}
