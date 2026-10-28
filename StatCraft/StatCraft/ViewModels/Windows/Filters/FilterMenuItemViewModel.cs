using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace StatCraft.ViewModels.Windows.Filters;

public interface IFilterMenuItemViewModel : INotifyPropertyChanged
{
    // Raised whenever this entry's own applied filters, or those of anything beneath it, are added to or
    // removed from — what the bar and the menu both redraw off.
    event EventHandler? AppliedFiltersChanged;
    IRelayCommand AddCommand { get; }
    event Action? Changed;
    string DisplayText { get; set; }
    Func<IFilterSlotViewModel>? FilterFactory { get; }
    IEnumerable<IFilterSlotViewModel> AppliedFilters { get; }
    IEnumerable<IFilterMenuItemViewModel>? SubMenuItems { get; }
    IEnumerable<IFilterSlotViewModel> ContainedFilters { get; }

    public bool IsUnique { get; set; } //unique items can only be added once
    public bool IsAvailable { get; }
}

public partial class FilterMenuItemViewModel<T> : ViewModelBase, IFilterMenuItemViewModel
{
    public event Action? Changed;
    public event EventHandler? AppliedFiltersChanged;
    [ObservableProperty] public partial string DisplayText { get; set; }
    Func<IFilterSlotViewModel>? IFilterMenuItemViewModel.FilterFactory => FilterFactory;
    public Func<IFilterSlotViewModel<T>>? FilterFactory { get; private set; }

    IEnumerable<IFilterSlotViewModel> IFilterMenuItemViewModel.AppliedFilters => AppliedFilters;
    public ObservableCollection<IFilterSlotViewModel<T>> AppliedFilters { get; } = [];

    private ObservableCollection<FilterMenuItemViewModel<T>>? _subMenuItems { get; set; }
    public IEnumerable<IFilterMenuItemViewModel>? SubMenuItems => _subMenuItems;
    public bool IsUnique { get; set; }

    public bool IsAvailable => (FilterFactory != null && (!IsUnique || AppliedFilters.Count == 0)) || (_subMenuItems?.Any(i => i.IsAvailable) ?? false);

    IEnumerable<IFilterSlotViewModel> IFilterMenuItemViewModel.ContainedFilters => ContainedFilters;
    public IEnumerable<IFilterSlotViewModel<T>> ContainedFilters
    {
        get
        {
            foreach (IFilterSlotViewModel<T> filter in AppliedFilters)
                yield return filter;
            if (_subMenuItems == null)
                yield break;
            foreach (FilterMenuItemViewModel<T> subMenuItem in _subMenuItems)
                foreach (IFilterSlotViewModel<T> containedFilter in subMenuItem.ContainedFilters)
                    yield return containedFilter;
        }
    }

    public FilterMenuItemViewModel(Func<IFilterSlotViewModel<T>> filterFactory, string name)
    {
        DisplayText = name;
        FilterFactory = filterFactory;
    }
    public FilterMenuItemViewModel(ObservableCollection<FilterMenuItemViewModel<T>> subMenu, string name)
    {
        DisplayText = name;
        WireSubMenu(subMenu);
    }
    // An entry that is both a filter of its own and a submenu of related ones — e.g. a build that can be
    // filtered on directly, with the builds nested under it offered beneath it.
    public FilterMenuItemViewModel(Func<IFilterSlotViewModel<T>> filterFactory, string name, ObservableCollection<FilterMenuItemViewModel<T>> subMenu)
    {
        DisplayText = name;
        FilterFactory = filterFactory;
        WireSubMenu(subMenu);
    }

    public void Destroy()
    {
        foreach (IFilterSlotViewModel<T> filter in AppliedFilters)
            UnWireFilter(filter);
        AppliedFilters.Clear();

        if (_subMenuItems != null)
        {
            foreach (FilterMenuItemViewModel<T> item in _subMenuItems)
                UnWireSubMenuItem(item);
            _subMenuItems.CollectionChanged -= SubMenuChanged;
        }
        FilterFactory = null;
        _subMenuItems = null;
    }
    public void DestroyRecursively()
    {
        if (_subMenuItems != null)
        {
            foreach (FilterMenuItemViewModel<T> item in _subMenuItems)
                item.DestroyRecursively();
        }
        Destroy();
    }

    // Renames the entry and every filter already added from it. The factory reads whatever it closed over,
    // so filters added after this pick the new name up on their own.
    public void Rename(string name)
    {
        DisplayText = name;
        foreach (IFilterSlotViewModel<T> filter in AppliedFilters)
            filter.Title = name;
    }

    // Moves another entry's applied filters onto this one, used where an entry is rebuilt from fresh
    // source data (see DataPageFiltersViewModel.RefreshBuildMenu) — the filters are the user's own work
    // and must survive a menu that was thrown away and rebuilt underneath them.
    public void AdoptAppliedFilters(FilterMenuItemViewModel<T> previous)
    {
        foreach (IFilterSlotViewModel<T> filter in previous.AppliedFilters.ToList())
        {
            previous.UnWireFilter(filter);
            previous.AppliedFilters.Remove(filter);
            WireFilter(filter);
            AppliedFilters.Add(filter);
        }
        previous.RaiseAppliedFiltersChanged();
        RaiseAppliedFiltersChanged();
    }

    private void WireSubMenu(ObservableCollection<FilterMenuItemViewModel<T>> subMenu)
    {
        _subMenuItems = subMenu;
        _subMenuItems.CollectionChanged += SubMenuChanged;
        foreach (FilterMenuItemViewModel<T> item in _subMenuItems)
            WireSubMenuItem(item);
    }

    private void SubMenuChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (object newItem in e.NewItems)
                WireSubMenuItem((FilterMenuItemViewModel<T>)newItem);
        }
        if (e.OldItems != null)
        {
            foreach (object oldItem in e.OldItems)
                UnWireSubMenuItem((FilterMenuItemViewModel<T>)oldItem);
        }
        RaiseAppliedFiltersChanged();
    }

    private void WireSubMenuItem(IFilterMenuItemViewModel menuItem)
    {
        menuItem.AppliedFiltersChanged += ForwardAppliedFiltersChanged;
        menuItem.Changed += ForwardChangedEvent;
    }
    private void UnWireSubMenuItem(IFilterMenuItemViewModel menuItem)
    {
        menuItem.AppliedFiltersChanged -= ForwardAppliedFiltersChanged;
        menuItem.Changed -= ForwardChangedEvent;
    }

    private void WireFilter(IFilterSlotViewModel<T> filter)
    {
        filter.Changed += ForwardChangedEvent;
        filter.Removed += RemoveFilter;
    }
    private void UnWireFilter(IFilterSlotViewModel<T> filter)
    {
        filter.Changed -= ForwardChangedEvent;
        filter.Removed -= RemoveFilter;
    }

    private void ForwardAppliedFiltersChanged(object? sender, EventArgs e) => RaiseAppliedFiltersChanged();

    private void RaiseAppliedFiltersChanged()
    {
        AppliedFiltersChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(IsAvailable));
    }

    private void ForwardChangedEvent()
    {
        Changed?.Invoke();
    }

    [RelayCommand]
    public void Add()
    {
        if (FilterFactory == null || !IsAvailable)
            return;

        IFilterSlotViewModel<T> filter = FilterFactory();
        WireFilter(filter);
        AppliedFilters.Add(filter);
        RaiseAppliedFiltersChanged();

        // A filter with no criteria of its own still constrains in one way: an attribute filter excludes
        // whatever has no value for it at all, so the results have to be re-derived on an add as well.
        Changed?.Invoke();
    }

    private void RemoveFilter(object? sender, EventArgs e)
    {
        if (sender is not IFilterSlotViewModel<T> filter || !AppliedFilters.Remove(filter))
            return;

        UnWireFilter(filter);
        RaiseAppliedFiltersChanged();
        Changed?.Invoke();
    }
}
