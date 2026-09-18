using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using StatCraft.ViewModels.Windows;
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

    public static FilterPanel Of(MapsPageViewModel page) =>
        new(page, nameof(page.VisibleFilterSlots), () => page.VisibleFilterSlots,
            nameof(page.HiddenFilterSlots), () => page.HiddenFilterSlots);

    public static FilterPanel Of(BuildsPageViewModel page) =>
        new(page, nameof(page.VisibleFilterSlots), () => page.VisibleFilterSlots,
            nameof(page.HiddenFilterSlots), () => page.HiddenFilterSlots);

    // The Data tab's FilterBar and AddFilterMenu, including its mandatory profile and date range filters.
    // The menu lists every entry and hides the applied ones itself (see Leaves).
    public static FilterPanel Of(DataPageViewModel page) =>
        new(page.Filters.FilterMenu, nameof(page.Filters.FilterMenu.AppliedFilters), () => page.Filters.FilterMenu.AppliedFilters,
            nameof(page.Filters.FilterMenu.FilterSlots), () => page.Filters.FilterMenu.FilterSlots);

    // Titles of the filters currently shown in the filter bar.
    public IReadOnlyList<string> AppliedTitles => AppliedSlots().Select(s => s.Title).ToList();

    // Every filter the "+" menu offers, flattened through any submenus.
    public IReadOnlyList<string> AddableTitles => MenuLeaves().Select(l => l.Text).ToList();

    // Clicks the filter's entry in the "+" menu.
    public FilterHandle Add(string title)
    {
        IFilterSlotViewModel slot = OfferedSlot(title);
        slot.AddCommand.Execute(null);
        return new FilterHandle(slot);
    }

    // A filter the "+" menu offers, without adding it.
    public FilterHandle Offered(string title) => new(OfferedSlot(title));

    private IFilterSlotViewModel OfferedSlot(string title)
    {
        List<MenuLeaf> matches = MenuLeaves().Where(l => l.Text == title).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException($"Expected one \"{title}\" entry in the add menu, found {matches.Count}. Offered: [{string.Join(", ", AddableTitles)}]");
        return matches[0].Slot;
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

    private IEnumerable<MenuLeaf> Leaves(object entry) => entry switch
    {
        // AddFilterMenu's item theme binds IsVisible to !IsApplied, so an applied entry (a filter already in
        // the bar, or a submenu with nothing left to add) is hidden, again only as of its last announcement.
        IFilterMenuItemViewModel item when Hidden(item) => [],
        // A submenu lists only the entries not applied yet, re-read when the submenu announces a change.
        IFilterMenuItemViewModel { Filter: null } subMenu => SubMenu(subMenu).Items.Cast<object>().SelectMany(Leaves),
        IFilterMenuItemViewModel { Filter: { } slot } item => [new MenuLeaf(item.DisplayText, slot)],
        IFilterSlotViewModel slot => [new MenuLeaf(slot.Title, slot)],
        _ => throw new InvalidOperationException($"Unrecognised add-menu entry {entry.GetType().Name}"),
    };

    private BoundList SubMenu(IFilterMenuItemViewModel subMenu)
    {
        if (!_subMenus.TryGetValue(subMenu, out BoundList? bound))
        {
            bound = new BoundList((INotifyPropertyChanged)subMenu, nameof(subMenu.UnAppliedSubMenuItems),
                () => subMenu.UnAppliedSubMenuItems ?? Enumerable.Empty<IFilterMenuItemViewModel>());
            _subMenus[subMenu] = bound;
        }
        return bound;
    }

    // One IsVisible binding per menu entry, made the first time it is read, like the submenu bindings above.
    private readonly Dictionary<IFilterMenuItemViewModel, BoundFlag> _hidden = new(ReferenceEqualityComparer.Instance);

    private bool Hidden(IFilterMenuItemViewModel item)
    {
        if (!_hidden.TryGetValue(item, out BoundFlag? hidden))
        {
            hidden = new BoundFlag((INotifyPropertyChanged)item, nameof(item.IsApplied), () => item.IsApplied);
            _hidden[item] = hidden;
        }
        return hidden.Value;
    }

    private sealed record MenuLeaf(string Text, IFilterSlotViewModel Slot);

    private sealed class BoundFlag
    {
        public bool Value { get; private set; }

        public BoundFlag(INotifyPropertyChanged owner, string property, Func<bool> read)
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
    public bool IsApplied => slot.IsApplied;

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
