using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        [ObservableProperty] private Race _playerRace = Race.Z;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(VsZ), nameof(VsT), nameof(VsP))]
        private Matchups _matchups = Matchups.None;

        public bool VsZ => Matchups.HasFlag(Matchups.VsZ);
        public bool VsT => Matchups.HasFlag(Matchups.VsT);
        public bool VsP => Matchups.HasFlag(Matchups.VsP);

        // Transient UI-only flag driving the Builds tab's opponent-race filter; never persisted.
        [ObservableProperty] private bool _matchesOpponentFilter = true;

        public ObservableCollection<BuildAttribute> Attributes { get; } = [];
        public ObservableCollection<BuildNode> Children { get; } = [];
    }
}
