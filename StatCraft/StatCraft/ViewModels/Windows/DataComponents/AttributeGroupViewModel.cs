using System.Collections.ObjectModel;
using Avalonia;
using StatCraft.Models.GameData.Attributes;

namespace StatCraft.ViewModels.Windows.DataComponents
{
    // One build's own attribute editors, grouped for display so it's clear which build each row came
    // from. Depth is that build's position within whichever selected path first introduced it (0 =
    // that path's own root), used to indent a nested build's group further right than its ancestor's.
    public sealed class AttributeGroupViewModel(string buildName, int depth, ObservableCollection<AttributeValue> attributes)
    {
        private const double IndentPerDepth = 16;

        public string BuildName { get; } = buildName;
        public Thickness Margin { get; } = new(depth * IndentPerDepth, 0, 0, 0);
        public ObservableCollection<AttributeValue> Attributes { get; } = attributes;
    }
}
