using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using StatCraft.Models.GameData.Builds;
using StatCraft.ViewModels.Windows;
using StatCraft.ViewModels.Windows.DataComponents;
using StatCraft.ViewModels.Windows.DataComponents.GameRow;
using StatCraft.ViewModels.Windows.Filters;
using StatCraft.ViewModels.Windows.Filters.WrappedFilters;

namespace StatCraft.Tests;

// The one place the page tests know how a page exposes its filters. Tests drive filters the way a
// user does (by title, through the "+" menu, the filter's ✕ button, and its kind-specific controls)
// and assert on what the page shows, so a refactor of how pages hold their filters only has to update
// the Of(...) factories here, not every test.
//
// Reads mirror the view's bindings rather than peeking at live state: the filter bar and the add menu
// are only re-read when the page raises the notification the bound control would react to. A plain
// IEnumerable is snapshotted (an ItemsControl materializes it once), a collection that raises
// CollectionChanged is read live. A page that changes its filters without announcing it goes stale
// here the same way the UI does.
internal sealed class FilterPanel
{
    private readonly BoundList _applied;
    private readonly BoundList _addMenu;

    private FilterPanel(INotifyPropertyChanged owner, string appliedProperty, Func<IEnumerable> readApplied,
        string addMenuProperty, Func<IEnumerable> readAddMenu)
    {
        _applied = new BoundList(owner, appliedProperty, readApplied);
        _addMenu = new BoundList(owner, addMenuProperty, readAddMenu);
    }

    public static FilterPanel Of(MapsPageViewModel page) => OfMenu(page.FilterMenu);

    public static FilterPanel Of(BuildsPageViewModel page) => OfMenu(page.FilterMenu);

    // The Data tab, including its mandatory profile and date range filters.
    public static FilterPanel Of(DataPageViewModel page) => OfMenu(page.Filters.FilterMenu);

    // The Data tab's filters on their own, without the page around them.
    public static FilterPanel Of(DataPageFiltersViewModel filters) => OfMenu(filters.FilterMenu);

    // A page whose filters live in a FilterMenuViewModel, shown by FilterBar and AddFilterMenu: the bar
    // binds AppliedFilters, and the menu binds MenuItems and hides whatever has nothing left to offer
    // (see Leaves).
    private static FilterPanel OfMenu(IFilterMenuViewModel menu) =>
        new(menu, nameof(menu.AppliedFilters), () => menu.AppliedFilters,
            nameof(menu.MenuItems), () => menu.MenuItems);

    // Titles of the filters currently shown in the filter bar.
    public IReadOnlyList<string> AppliedTitles => AppliedSlots().Select(s => s.Title).ToList();

    // Every filter the "+" menu offers, flattened through any submenus.
    public IReadOnlyList<string> AddableTitles => MenuLeaves().Select(l => l.Text).ToList();

    // Every filter the "+" menu offers under one of its menus.
    public IReadOnlyList<string> AddableTitlesIn(string menu) =>
        MenuLeaves().Where(l => l.Path.Contains(menu)).Select(l => l.Text).ToList();

    // Clicks the filter's entry in the "+" menu. A click builds a filter and puts it in the bar, so the
    // handle is on the filter that click produced — clicking twice gives two filters, unless the entry is
    // unique, in which case it leaves the menu after the first.
    public FilterHandle Add(string title) => Add(null, title);

    // The same leaf can be offered under more than one menu — each matchup is offered once per set of
    // players — so this names the menu it should be taken from.
    public FilterHandle Add(string? menu, string title)
    {
        IFilterMenuItemViewModel item = OfferedItem(menu, title);
        int before = item.AppliedFilters.Count();
        item.AddCommand.Execute(null);

        IFilterSlotViewModel? added = item.AppliedFilters.Skip(before).LastOrDefault();
        if (added == null)
            throw new InvalidOperationException($"Clicking \"{title}\" added no filter to the bar.");
        return new FilterHandle(added);
    }

    // What this entry would put in the bar, without leaving it there: the filter is built, handed back for
    // inspection and taken straight out again, so the page's own results are untouched.
    public FilterHandle Preview(string title) => Preview(null, title);

    public FilterHandle Preview(string? menu, string title)
    {
        FilterHandle handle = Add(menu, title);
        handle.Remove();
        return handle;
    }

    // Whether the "+" menu still offers this entry.
    public bool IsOffered(string title) => IsOffered(null, title);

    public bool IsOffered(string? menu, string title) =>
        MenuLeaves().Any(l => l.Text == title && (menu == null || l.Path.Contains(menu)));

    private IFilterMenuItemViewModel OfferedItem(string? menu, string title)
    {
        List<MenuLeaf> matches = MenuLeaves()
            .Where(l => l.Text == title && (menu == null || l.Path.Contains(menu)))
            .ToList();
        if (matches.Count != 1)
        {
            string where = menu == null ? "the add menu" : $"the \"{menu}\" menu";
            throw new InvalidOperationException($"Expected one \"{title}\" entry in {where}, found {matches.Count}. Offered: [{string.Join(", ", AddableTitles)}]");
        }
        return matches[0].Item;
    }

    // A filter already showing in the filter bar.
    public FilterHandle Applied(string title)
    {
        List<IFilterSlotViewModel> matches = AppliedSlots().Where(s => s.Title == title).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException($"Expected one \"{title}\" filter in the filter bar, found {matches.Count}. Showing: [{string.Join(", ", AppliedTitles)}]");
        return new FilterHandle(matches[0]);
    }

    private IEnumerable<IFilterSlotViewModel> AppliedSlots() => _applied.Items.Cast<IFilterSlotViewModel>();

    private IEnumerable<MenuLeaf> MenuLeaves() => _addMenu.Items.Cast<object>().SelectMany(Leaves);

    // One binding per submenu, made the first time it is read and kept for the life of the panel. That is
    // stricter than a view that happens to rebuild the submenu whenever the top-level menu changes: a
    // submenu has to announce its own changes rather than rely on a rebuild to pick them up.
    private readonly Dictionary<IFilterMenuItemViewModel, BoundList> _subMenus = new(ReferenceEqualityComparer.Instance);

    private IEnumerable<MenuLeaf> Leaves(object entry) => Leaves(entry, []);

    private IEnumerable<MenuLeaf> Leaves(object entry, IReadOnlyList<string> path) => entry switch
    {
        // AddFilterMenu's item theme binds IsVisible to IsAvailable, so an entry with nothing left to add
        // (a unique filter already in the bar, or a submenu whose entries are all taken) is hidden, again
        // only as of its last announcement.
        IFilterMenuItemViewModel item when !Available(item) => [],
        // An entry can add a filter, hold a submenu, or both: it offers itself first, then whatever its
        // submenu offers, re-read when the submenu announces a change.
        IFilterMenuItemViewModel item => Offerings(item, path),
        _ => throw new InvalidOperationException($"Unrecognised add-menu entry {entry.GetType().Name}"),
    };

    private IEnumerable<MenuLeaf> Offerings(IFilterMenuItemViewModel item, IReadOnlyList<string> path)
    {
        if (item.FilterFactory != null)
            yield return new MenuLeaf(Header(item), item, path);
        if (item.SubMenuItems == null)
            yield break;
        List<string> childPath = [.. path, Header(item)];
        foreach (MenuLeaf leaf in SubMenu(item).Items.Cast<object>().SelectMany(entry => Leaves(entry, childPath)))
            yield return leaf;
    }

    private BoundList SubMenu(IFilterMenuItemViewModel subMenu)
    {
        if (!_subMenus.TryGetValue(subMenu, out BoundList? bound))
        {
            bound = new BoundList(subMenu, nameof(subMenu.SubMenuItems),
                () => subMenu.SubMenuItems ?? Enumerable.Empty<IFilterMenuItemViewModel>());
            _subMenus[subMenu] = bound;
        }
        return bound;
    }

    // The IsVisible and Header bindings of each menu entry, made the first time the entry is read, like
    // the submenu bindings above. An entry that changes without announcing it stays as it was.
    private readonly Dictionary<IFilterMenuItemViewModel, BoundValue<bool>> _available = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IFilterMenuItemViewModel, BoundValue<string>> _headers = new(ReferenceEqualityComparer.Instance);

    private bool Available(IFilterMenuItemViewModel item) =>
        Bound(_available, item, nameof(item.IsAvailable), () => item.IsAvailable);

    private string Header(IFilterMenuItemViewModel item) =>
        Bound(_headers, item, nameof(item.DisplayText), () => item.DisplayText);

    private static TValue Bound<TValue>(Dictionary<IFilterMenuItemViewModel, BoundValue<TValue>> bindings,
        IFilterMenuItemViewModel item, string property, Func<TValue> read)
    {
        if (!bindings.TryGetValue(item, out BoundValue<TValue>? bound))
        {
            bound = new BoundValue<TValue>(item, property, read);
            bindings[item] = bound;
        }
        return bound.Value;
    }

    // One clickable entry in the menu. Path is the headers of the menus it sits under, outermost first.
    private sealed record MenuLeaf(string Text, IFilterMenuItemViewModel Item, IReadOnlyList<string> Path);

    private sealed class BoundValue<TValue>
    {
        public TValue Value { get; private set; }

        public BoundValue(INotifyPropertyChanged owner, string property, Func<TValue> read)
        {
            Value = read();
            owner.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == property || string.IsNullOrEmpty(e.PropertyName))
                    Value = read();
            };
        }
    }

    private sealed class BoundList
    {
        private readonly Func<IEnumerable> _read;
        public IEnumerable Items { get; private set; }

        public BoundList(INotifyPropertyChanged owner, string property, Func<IEnumerable> read)
        {
            _read = read;
            Items = Bind();
            owner.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == property || string.IsNullOrEmpty(e.PropertyName))
                    Items = Bind();
            };
        }

        private IEnumerable Bind()
        {
            IEnumerable source = _read();
            return source is INotifyCollectionChanged ? source : source.Cast<object>().ToList();
        }
    }
}

// One filter, driven through the same members its controls bind to. Resolves the kind-specific part
// itself, so tests don't care whether the page hands out that slot directly or wraps it.
internal sealed class FilterHandle(IFilterSlotViewModel slot)
{
    public string Title => slot.Title;

    // A mandatory filter is always showing and has no ✕ button.
    public bool Mandatory => slot.Mandatory;
    public bool AllowIncludeUnset => slot.AllowIncludeUnset;

    public bool IncludeUnset
    {
        get => slot.IncludeUnset;
        set => slot.IncludeUnset = value;
    }

    // The filter's ✕ button.
    public void Remove() => slot.RemoveCommand.Execute(null);

    public IReadOnlyList<string> OptionLabels => Kind<ICheckboxFilterSlotViewModel>().Options.Select(o => o.Label).ToList();
    public IReadOnlyList<string> CheckedLabels => Kind<ICheckboxFilterSlotViewModel>().Options.Where(o => o.IsChecked).Select(o => o.Label).ToList();

    public FilterHandle Check(params string[] labels) => SetChecked(labels, true);
    public FilterHandle Uncheck(params string[] labels) => SetChecked(labels, false);

    public bool? BoolValue
    {
        get => Kind<IBoolFilterSlotViewModel>().Value;
        set => Kind<IBoolFilterSlotViewModel>().Value = value;
    }

    public decimal? Min
    {
        get => Kind<INumericRangeFilterSlotViewModel>().Min;
        set => Kind<INumericRangeFilterSlotViewModel>().Min = value;
    }

    public decimal? Max
    {
        get => Kind<INumericRangeFilterSlotViewModel>().Max;
        set => Kind<INumericRangeFilterSlotViewModel>().Max = value;
    }

    // A build filter covers the builds nested under its picked build unless this is turned off.
    public bool IncludeDescendants
    {
        get => Kind<IBuildFilterSlotViewModel>().IncludeDescendants;
        set => Kind<IBuildFilterSlotViewModel>().IncludeDescendants = value;
    }

    // Every build the filter's own picker offers, at any depth of the tree it was given.
    public IReadOnlyList<string> BuildOptions => OfferedBuilds().Select(b => b.Name).ToList();

    // The picked build, as the picker labels it: its whole path, or blank while none is picked.
    public string SelectedBuild => Picker().SelectedBuildLabel;

    // The name of the picked build, or null while none is picked.
    public string? PickedBuild => Kind<IBuildFilterSlotViewModel>().Build?.Name;

    // Picks a build in the filter's own picker, by the name it is shown under; null picks "(none)", the
    // picker's own way of offering "no build at all".
    public FilterHandle SelectBuild(string? name)
    {
        if (name == null)
        {
            Picker().SelectBuildCommand.Execute(BuildSelectionSlotViewModel.NoneOption);
            return this;
        }

        List<BuildNode> matches = OfferedBuilds().Where(b => b.Name == name).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException($"Expected one \"{name}\" build in \"{Title}\"'s picker, found {matches.Count}. Offering: [{string.Join(", ", BuildOptions)}]");

        Picker().SelectBuildCommand.Execute(matches[0]);
        return this;
    }

    // The filter fields for the picked build's own details, shown behind the filter's "Details" button.
    public IReadOnlyList<string> DetailTitles =>
        Kind<IBuildFilterSlotViewModel>().BuildDetailSlots.Select(s => s.Title).ToList();

    // One of those fields, driven like any other filter.
    public FilterHandle Detail(string title)
    {
        List<IFilterSlotViewModel> matches = Kind<IBuildFilterSlotViewModel>().BuildDetailSlots
            .Where(s => s.Title == title)
            .Cast<IFilterSlotViewModel>()
            .ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException($"Expected one \"{title}\" detail field on \"{Title}\", found {matches.Count}. Showing: [{string.Join(", ", DetailTitles)}]");
        return new FilterHandle(matches[0]);
    }

    private BuildSelectionSlotViewModel Picker() => Kind<IBuildFilterSlotViewModel>().BuildSelection;

    private IEnumerable<BuildNode> OfferedBuilds() =>
        Picker().BuildTree.SelectMany(root => root.EnumerateDescendants().Prepend(root));

    public bool IsCheckboxFilter => TryKind<ICheckboxFilterSlotViewModel>() != null;
    public bool IsBoolFilter => TryKind<IBoolFilterSlotViewModel>() != null;
    public DateTime? FromDate
    {
        get => Kind<IDateRangeFilterSlotViewModel>().FromDate;
        set => Kind<IDateRangeFilterSlotViewModel>().FromDate = value;
    }

    public DateTime? ToDate
    {
        get => Kind<IDateRangeFilterSlotViewModel>().ToDate;
        set => Kind<IDateRangeFilterSlotViewModel>().ToDate = value;
    }

    public bool IsNumericRangeFilter => TryKind<INumericRangeFilterSlotViewModel>() != null;
    public bool IsDateRangeFilter => TryKind<IDateRangeFilterSlotViewModel>() != null;

    private FilterHandle SetChecked(string[] labels, bool isChecked)
    {
        ICheckboxFilterSlotViewModel checkbox = Kind<ICheckboxFilterSlotViewModel>();
        foreach (string label in labels)
        {
            List<ICheckboxFilterOptionViewModel> matches = checkbox.Options.Where(o => o.Label == label).ToList();
            if (matches.Count != 1)
                throw new InvalidOperationException($"Expected one \"{label}\" option on \"{Title}\", found {matches.Count}. Options: [{string.Join(", ", OptionLabels)}]");
            matches[0].IsChecked = isChecked;
        }
        return this;
    }

    private TKind Kind<TKind>() where TKind : class =>
        TryKind<TKind>() ?? throw new InvalidOperationException($"\"{Title}\" is not a {typeof(TKind).Name}");

    private TKind? TryKind<TKind>() where TKind : class =>
        slot as TKind ?? (slot as IWrappedFilterSlotViewModel)?.WrappedFilter as TKind;
}
