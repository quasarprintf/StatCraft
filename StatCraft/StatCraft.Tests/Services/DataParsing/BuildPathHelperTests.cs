using StatCraft.Models.GameData.Builds;
using StatCraft.Services.DataParsing;

namespace StatCraft.Tests;

public class BuildPathHelperTests
{
    [Fact]
    public void FindPath_RootNode_ReturnsSingleElementPath()
    {
        BuildNode root = new() { Id = 1, Name = "Root" };

        List<BuildNode>? path = BuildPathHelper.FindPath([root], 1);

        Assert.Equal([root], path);
    }

    [Fact]
    public void FindPath_NestedNode_ReturnsFullPathFromRoot()
    {
        BuildNode child = new() { Id = 2, Name = "Child" };
        BuildNode root = new() { Id = 1, Name = "Root" };
        root.Children.Add(child);

        List<BuildNode>? path = BuildPathHelper.FindPath([root], 2);

        Assert.Equal([root, child], path);
    }

    [Fact]
    public void FindPath_DeeplyNestedNode_ReturnsFullPath()
    {
        BuildNode grandchild = new() { Id = 3, Name = "Grandchild" };
        BuildNode child = new() { Id = 2, Name = "Child" };
        child.Children.Add(grandchild);
        BuildNode root = new() { Id = 1, Name = "Root" };
        root.Children.Add(child);

        List<BuildNode>? path = BuildPathHelper.FindPath([root], 3);

        Assert.Equal([root, child, grandchild], path);
    }

    [Fact]
    public void FindPath_MissingId_ReturnsNull()
    {
        BuildNode root = new() { Id = 1, Name = "Root" };

        List<BuildNode>? path = BuildPathHelper.FindPath([root], 999);

        Assert.Null(path);
    }

    [Fact]
    public void FindPath_MultipleRoots_FindsNodeUnderSecondRoot()
    {
        BuildNode firstRoot = new() { Id = 1, Name = "First" };
        BuildNode secondChild = new() { Id = 3, Name = "SecondChild" };
        BuildNode secondRoot = new() { Id = 2, Name = "Second" };
        secondRoot.Children.Add(secondChild);

        List<BuildNode>? path = BuildPathHelper.FindPath([firstRoot, secondRoot], 3);

        Assert.Equal([secondRoot, secondChild], path);
    }

    [Fact]
    public void FlattenAttributes_PreservesRootToLeafOrder()
    {
        BuildAttribute rootAttr = new() { Id = 10, Name = "RootAttr" };
        BuildAttribute childAttr1 = new() { Id = 20, Name = "ChildAttr1" };
        BuildAttribute childAttr2 = new() { Id = 21, Name = "ChildAttr2" };

        BuildNode child = new() { Id = 2, Name = "Child" };
        child.Attributes.Add(childAttr1);
        child.Attributes.Add(childAttr2);

        BuildNode root = new() { Id = 1, Name = "Root" };
        root.Attributes.Add(rootAttr);
        root.Children.Add(child);

        List<BuildAttribute> flattened = BuildPathHelper.FlattenAttributes([root, child]);

        Assert.Equal([rootAttr, childAttr1, childAttr2], flattened);
    }
}
