using System.Collections.Specialized;
using StatCraft.ViewModels;

namespace StatCraft.Tests;

public class AttributeValueOptionSyncTests
{
    [Fact]
    public void Apply_ItemAdded_InsertsAtItsCurrentIndex()
    {
        List<(int AttributeId, string Value, int SortOrder)> inserted = [];
        List<string> options = ["Macro", "Rush"];
        NotifyCollectionChangedEventArgs e = new(NotifyCollectionChangedAction.Add, "Rush", index: 1);

        AttributeValueOptionSync.Apply(e, attributeId: 7, options,
            insertOption: (id, value, sortOrder) => inserted.Add((id, value, sortOrder)),
            deleteOption: (_, _) => Assert.Fail("Should not delete on an Add."));

        (int AttributeId, string Value, int SortOrder) call = Assert.Single(inserted);
        Assert.Equal((7, "Rush", 1), call);
    }

    [Fact]
    public void Apply_ItemRemoved_DeletesByValue()
    {
        List<(int AttributeId, string Value)> deleted = [];
        List<string> options = ["Macro"];
        NotifyCollectionChangedEventArgs e = new(NotifyCollectionChangedAction.Remove, "Rush", index: 0);

        AttributeValueOptionSync.Apply(e, attributeId: 7, options,
            insertOption: (_, _, _) => Assert.Fail("Should not insert on a Remove."),
            deleteOption: (id, value) => deleted.Add((id, value)));

        (int AttributeId, string Value) call = Assert.Single(deleted);
        Assert.Equal((7, "Rush"), call);
    }

    // ObservableCollection.Clear() raises a Reset with null NewItems/OldItems rather than per-item
    // entries — neither callback should be invoked, since there's nothing to attribute the change to.
    [Fact]
    public void Apply_Reset_DoesNothing()
    {
        List<string> options = [];
        NotifyCollectionChangedEventArgs e = new(NotifyCollectionChangedAction.Reset);

        AttributeValueOptionSync.Apply(e, attributeId: 7, options,
            insertOption: (_, _, _) => Assert.Fail("Reset must not insert."),
            deleteOption: (_, _) => Assert.Fail("Reset must not delete."));
    }
}
