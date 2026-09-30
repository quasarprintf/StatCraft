using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Race;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace StatCraft.Models.GameData.Builds;

public partial class BuildNode : ObservableObject, IAttributedObject
{
    public int Id { get; set; }

    public BuildNode? Parent { get; set; }

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string Description { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsExpanded { get; set; }

    [ObservableProperty] public partial Race.Race PlayerRace { get; set; } = Race.Race.Zerg;

    [NotifyPropertyChangedFor(nameof(VsZ), nameof(VsT), nameof(VsP))]
    [ObservableProperty] public partial Matchups Matchups { get; set; } = Race.Matchups.None;

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
    [ObservableProperty] public partial bool MatchesOpponentFilter { get; set; } = true;

    [NotifyPropertyChangedFor(nameof(IsVisibleInTree))]
    [ObservableProperty] public partial bool MatchesFilter { get; set; } = true;

    public bool IsVisibleInTree => MatchesOpponentFilter && MatchesFilter;

    public ObservableCollection<AttributeDefinition> Details { get; } = [];
    public ObservableCollection<AttributeValue> AttributeValues { get; } = [];

    [NotifyPropertyChangedFor(nameof(HasChildren))]
    [ObservableProperty] public partial ObservableCollection<BuildNode> Children { get; set; } = [];
    public bool HasChildren => Children.Count > 0;

    public BuildNode()
    {
        Children.CollectionChanged += (s,e) => OnPropertyChanged(nameof(HasChildren));
    }

    public void AddAttribute(AttributeDefinition definition) 
    {
        AttributeValues.Add(definition.DefaultValue.Clone());
    }
    public void RemoveAttribute(AttributeValue value)
    {
        AttributeValues.Remove(value);
    }
    public AttributeValue? GetAttributeByDefinitionId(int id)
    {
        return AttributeValues.FirstOrDefault(v => v.Definition.Id == id);
    }

    public void AddChild(BuildNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }
    public void RemoveChild(BuildNode child)
    {
        if (!Children.Remove(child))
            throw new Exception("attempting to remove child that doesn't exist");
        child.Parent = null;
    }

    public IEnumerable<BuildNode> EnumerateAncestors()
    {
        BuildNode? currentParent = Parent;
        while (currentParent != null)
        {
            yield return currentParent;
            currentParent = currentParent.Parent;
        }
    }
    public IEnumerable<BuildNode> EnumerateDescendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var descendant in child.EnumerateDescendants())
                yield return descendant;
        }
    }

    public bool HasAncestor(BuildNode node)
    {
        return EnumerateAncestors().Contains(node);
    }
}
