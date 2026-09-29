using StatCraft.ViewModels.Windows.Filters;
using System.Collections.ObjectModel;

namespace StatCraft.Tests;

// An add-menu entry used to be either one filter or a submenu of them. It can now be both at once — a
// filter that is itself addable, with related filters nested beneath it — so everything the menu derives
// from an entry has to account for both halves rather than picking one.
public class FilterMenuItemViewModelTests
{
    [Fact]
    public void ContainedFilters_EntryWithBothAFilterAndASubMenu_ReturnsItsOwnFilterAndTheNestedOnes()
    {
        BoolFilterSlotViewModel<string> own = Slot("4 Gate");
        BoolFilterSlotViewModel<string> nested = Slot("4 Gate Proxy");
        FilterMenuItemViewModel<string> item = new(own, SubMenu(nested));

        Assert.Equal([own, nested], item.ContainedFilters);
    }

    // ContainedFilters is what the menu collects applied filters from, so a nested filter has to reach the
    // filter bar even when its parent entry carries a filter of its own.
    [Fact]
    public void AppliedFilters_NestedFilterUnderAFilterEntry_ShowsInTheBar()
    {
        BoolFilterSlotViewModel<string> own = Slot("4 Gate");
        BoolFilterSlotViewModel<string> nested = Slot("4 Gate Proxy");
        FilterMenuViewModel<string> menu = new([new FilterMenuItemViewModel<string>(own, SubMenu(nested))]);

        nested.IsApplied = true;

        Assert.Equal([nested], menu.AppliedFilters);
    }

    // IsApplied hides the entry from the add menu, so it can only be true once there is nothing left to
    // add — neither the entry's own filter nor anything beneath it.
    [Fact]
    public void IsApplied_EntryWithBothAFilterAndASubMenu_IsTrueOnlyOnceBothHalvesAre()
    {
        BoolFilterSlotViewModel<string> own = Slot("4 Gate");
        BoolFilterSlotViewModel<string> nested = Slot("4 Gate Proxy");
        FilterMenuItemViewModel<string> item = new(own, SubMenu(nested));
        List<string?> changes = [];
        item.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.False(item.IsApplied);

        own.IsApplied = true;
        Assert.False(item.IsApplied);
        Assert.Contains(nameof(item.IsApplied), changes);

        nested.IsApplied = true;
        Assert.True(item.IsApplied);
    }

    // Only the half that still has something to offer stays in the menu.
    [Fact]
    public void UnAppliedSubMenuItems_NestedFilterApplied_LeavesTheParentEntryOffered()
    {
        BoolFilterSlotViewModel<string> own = Slot("4 Gate");
        BoolFilterSlotViewModel<string> nested = Slot("4 Gate Proxy");
        FilterMenuItemViewModel<string> item = new(own, SubMenu(nested));

        nested.IsApplied = true;

        Assert.False(item.IsApplied);
        Assert.Empty(item.UnAppliedSubMenuItems!);
    }

    // Criteria typed into either half have to reach whoever re-runs the filters.
    [Fact]
    public void Changed_RaisedByEitherHalf_IsForwarded()
    {
        BoolFilterSlotViewModel<string> own = Slot("4 Gate");
        BoolFilterSlotViewModel<string> nested = Slot("4 Gate Proxy");
        FilterMenuItemViewModel<string> item = new(own, SubMenu(nested));
        int changed = 0;
        item.Changed += () => changed++;

        own.Value = true;
        Assert.Equal(1, changed);

        nested.Value = false;
        Assert.Equal(2, changed);
    }

    // The entry names itself after its own filter, and follows a rename the same way a filter-only entry does.
    [Fact]
    public void DisplayText_EntryWithBothAFilterAndASubMenu_FollowsItsOwnFiltersTitle()
    {
        BoolFilterSlotViewModel<string> own = Slot("4 Gate");
        FilterMenuItemViewModel<string> item = new(own, SubMenu(Slot("4 Gate Proxy")));

        Assert.Equal("4 Gate", item.DisplayText);

        own.Title = "Four Gate";

        Assert.Equal("Four Gate", item.DisplayText);
    }

    private static BoolFilterSlotViewModel<string> Slot(string title) => new(title, _ => true);

    private static ObservableCollection<FilterMenuItemViewModel<string>> SubMenu(params BoolFilterSlotViewModel<string>[] slots) =>
        new(slots.Select(s => new FilterMenuItemViewModel<string>(s)));
}
