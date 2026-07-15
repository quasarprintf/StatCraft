using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace StatCraft.Models.GameData.Builds
{
    public partial class BuildNode : ObservableObject
    {
        public int Id { get; set; }

        [ObservableProperty] private string _name = string.Empty;
        [ObservableProperty] private string _description = string.Empty;
        [ObservableProperty] private bool _isExpanded;
        public ObservableCollection<BuildAttribute> Attributes { get; } = [];
        public ObservableCollection<BuildNode> Children { get; } = [];
    }
}
