using StatCraft.ViewModels.Windows.Filters;
using System.Collections.ObjectModel;

namespace StatCraft.Tests;

// An add-menu entry is a factory for the filter it adds plus, optionally, a submenu of related entries.
// Adding runs the factory, so the filters an entry has produced are the ones showing in the bar, and
// removing one in the bar takes it back out of the entry.
public class FilterMenuItemViewModelTests
{
    [Fact]
    public void Add_RunsTheFactoryAndKeepsWhatItProduced()
    {
        FilterMenuItemViewModel<string> item = Item("Supply");

        item.Add();

        IFilterSlotViewModel<string> added = Assert.Single(item.AppliedFilters);
        Assert.Equal("Supply", added.Title);
        Assert.Equal([added], item.ContainedFilters);
    }

    // Each add builds its own filter, so the same dimension can be asked two different questions.
    [Fact]
    public void Add_OnANonUniqueEntry_BuildsAFilterEachTime()
    {
        FilterMenuItemViewModel<string> item = Item("Supply");

        item.Add();
        item.Add();

        Assert.Equal(2, item.AppliedFilters.Count);
        Assert.NotSame(item.AppliedFilters[0], item.AppliedFilters[1]);
        Assert.True(item.IsAvailable);
    }

    // A unique entry leaves the menu while its filter is in the bar, and is offered again once it's gone.
    [Fact]
    public void IsAvailable_UniqueEntry_FollowsWhetherItsFilterIsInTheBar()
    {
        FilterMenuItemViewModel<string> item = Item("Profile");
        item.IsUnique = true;
        List<string?> changes = [];
        item.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        item.Add();

        Assert.False(item.IsAvailable);
        Assert.Contains(nameof(item.IsAvailable), changes);

        item.AppliedFilters[0].RemoveCommand.Execute(null);

        Assert.Empty(item.AppliedFilters);
        Assert.True(item.IsAvailable);
    }

    [Fact]
    public void Add_OnAUniqueEntryAlreadyInTheBar_DoesNothing()
    {
        FilterMenuItemViewModel<string> item = Item("Profile");
        item.IsUnique = true;
        item.Add();

        item.Add();

        Assert.Single(item.AppliedFilters);
    }

    // A mandatory filter can't be taken out of the bar at all, so its remove button clears it instead.
    [Fact]
    public void RemovingAMandatoryFilter_LeavesItInTheBar()
    {
        FilterMenuItemViewModel<string> item = new(() => new BoolFilterSlotViewModel<string>("Date", _ => true) { Mandatory = true }, "Date");
        item.Add();
        IBoolFilterSlotViewModel filter = (IBoolFilterSlotViewModel)item.AppliedFilters[0];
        filter.Value = true;

        filter.RemoveCommand.Execute(null);

        Assert.Single(item.AppliedFilters);
        Assert.Null(filter.Value);
    }

    // ContainedFilters is what the menu collects the bar's contents from, so a nested entry's filters have
    // to reach it through every level above them.
    [Fact]
    public void ContainedFilters_ReturnsItsOwnFiltersAndThoseOfNestedEntries()
    {
        FilterMenuItemViewModel<string> nested = Item("4 Gate Proxy");
        FilterMenuItemViewModel<string> item = new(() => Slot("4 Gate"), "4 Gate", [nested]);

        item.Add();
        nested.Add();

        Assert.Equal([item.AppliedFilters[0], nested.AppliedFilters[0]], item.ContainedFilters);
    }

    [Fact]
    public void AppliedFilters_NestedEntrysFilter_ShowsInTheMenusBar()
    {
        FilterMenuItemViewModel<string> nested = Item("4 Gate Proxy");
        FilterMenuItemViewModel<string> item = new(() => Slot("4 Gate"), "4 Gate", [nested]);
        FilterMenuViewModel<string> menu = new([item]);

        nested.Add();

        Assert.Equal([nested.AppliedFilters[0]], menu.AppliedFilters);
    }

    // A submenu is only worth showing while something under it can still be added, which is what keeps an
    // empty "Game Attributes" or a race with no builds out of the menu.
    [Fact]
    public void IsAvailable_SubMenuEntry_FollowsWhetherAnythingUnderItIsAvailable()
    {
        FilterMenuItemViewModel<string> empty = new([], "Game Attributes");
        Assert.False(empty.IsAvailable);

        FilterMenuItemViewModel<string> unique = Item("Only One");
        unique.IsUnique = true;
        FilterMenuItemViewModel<string> parent = new([unique], "Game Attributes");
        Assert.True(parent.IsAvailable);

        unique.Add();

        Assert.False(parent.IsAvailable);
    }

    // Criteria typed into any of an entry's filters, at any depth, have to reach whoever re-runs them.
    [Fact]
    public void Changed_RaisedByAnyOfItsFilters_IsForwarded()
    {
        FilterMenuItemViewModel<string> nested = Item("4 Gate Proxy");
        FilterMenuItemViewModel<string> item = new(() => Slot("4 Gate"), "4 Gate", [nested]);
        int changed = 0;
        item.Changed += () => changed++;

        item.Add();
        nested.Add();
        Assert.Equal(2, changed); //adding a filter is itself a change: an empty filter can still exclude

        ((IBoolFilterSlotViewModel)item.AppliedFilters[0]).Value = true;
        ((IBoolFilterSlotViewModel)nested.AppliedFilters[0]).Value = false;

        Assert.Equal(4, changed);
    }

    [Fact]
    public void Changed_FilterRemovedFromTheBar_IsForwarded()
    {
        FilterMenuItemViewModel<string> item = Item("Supply");
        item.Add();
        int changed = 0;
        item.Changed += () => changed++;

        item.AppliedFilters[0].RemoveCommand.Execute(null);

        Assert.Equal(1, changed);
    }

    // A renamed attribute keeps whatever was already added from it, under its new name.
    [Fact]
    public void Rename_RenamesTheEntryAndTheFiltersAlreadyAddedFromIt()
    {
        FilterMenuItemViewModel<string> item = Item("Supply");
        item.Add();

        item.Rename("Worker Count");

        Assert.Equal("Worker Count", item.DisplayText);
        Assert.Equal("Worker Count", item.AppliedFilters[0].Title);
    }

    [Fact]
    public void Destroy_UnwiresTheFiltersItHadAdded()
    {
        FilterMenuItemViewModel<string> item = Item("Supply");
        item.Add();
        IFilterSlotViewModel<string> filter = item.AppliedFilters[0];
        int changed = 0;
        item.Changed += () => changed++;

        item.Destroy();
        ((IBoolFilterSlotViewModel)filter).Value = true;

        Assert.Empty(item.AppliedFilters);
        Assert.Equal(0, changed);
    }

    private static FilterMenuItemViewModel<string> Item(string title) => new(() => Slot(title), title);

    private static BoolFilterSlotViewModel<string> Slot(string title) => new(title, _ => true);
}
