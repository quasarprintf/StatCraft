using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace StatCraft.ViewModels.Windows.Filters;

public interface IFilterMenuItemViewModel : INotifyPropertyChanged
{
    event EventHandler? IsAppliedChanged;
    event Action? Changed;
    string DisplayText { get; set; }
    IFilterSlotViewModel? Filter { get; }
    IEnumerable<IFilterMenuItemViewModel>? SubMenuItems { get; }
    IEnumerable<IFilterMenuItemViewModel>? UnAppliedSubMenuItems { get; }
    bool IsApplied { get; }
    IEnumerable<IFilterSlotViewModel> ContainedFilters { get; }
}
public partial class FilterMenuItemViewModel<T> : ViewModelBase, IFilterMenuItemViewModel
{
    public event Action? Changed;
    public event EventHandler? IsAppliedChanged;
    [ObservableProperty] private string _displayText;
    IFilterSlotViewModel? IFilterMenuItemViewModel.Filter => Filter;
    public IFilterSlotViewModel<T>? Filter { get; private set; }

    private ObservableCollection<FilterMenuItemViewModel<T>>? _subMenuItems { get; set; }
    public IEnumerable<IFilterMenuItemViewModel>? SubMenuItems => _subMenuItems;
    public IEnumerable<IFilterMenuItemViewModel>? UnAppliedSubMenuItems => _subMenuItems?.Where(i => !i.IsApplied);

    // Applied means "nothing left here to add", which is what hides the entry from the add menu. An
    // entry carrying both a filter and a submenu keeps offering itself until both halves are applied.
    public bool IsApplied => (Filter?.IsApplied ?? true) && (_subMenuItems?.All(i => i.IsApplied) ?? true);

    IEnumerable<IFilterSlotViewModel> IFilterMenuItemViewModel.ContainedFilters => ContainedFilters;
    public IEnumerable<IFilterSlotViewModel<T>> ContainedFilters
    {
        get
        {
            if (Filter != null)
                yield return Filter;
            if (_subMenuItems == null)
                yield break;
            foreach (FilterMenuItemViewModel<T> subMenuItem in _subMenuItems)
                foreach (IFilterSlotViewModel<T> containedFilter in subMenuItem.ContainedFilters)
                    yield return containedFilter;
        }
    }

    public FilterMenuItemViewModel(IFilterSlotViewModel<T> filter)
    {
        _displayText = filter.Title;
        WireFilter(filter);
    }
    public FilterMenuItemViewModel(ObservableCollection<FilterMenuItemViewModel<T>> subMenu, string name)
    {
        _displayText = name;
        WireSubMenu(subMenu);
    }
    // An entry that is both a filter of its own and a submenu of related ones — e.g. a build that can be
    // filtered on directly, with the builds nested under it offered beneath it.
    public FilterMenuItemViewModel(IFilterSlotViewModel<T> filter, ObservableCollection<FilterMenuItemViewModel<T>> subMenu)
    {
        _displayText = filter.Title;
        WireFilter(filter);
        WireSubMenu(subMenu);
    }

    private void WireFilter(IFilterSlotViewModel<T> filter)
    {
        Filter = filter;
        filter.PropertyChanged += (_,e) => { if (e.PropertyName == nameof(filter.Title)) DisplayText = filter.Title; };
        filter.IsAppliedChanged += RefreshIsApplied;
        filter.Changed += ForwardChangedEvent;
    }

    private void WireSubMenu(ObservableCollection<FilterMenuItemViewModel<T>> subMenu)
    {
        _subMenuItems = subMenu;
        _subMenuItems.CollectionChanged += SubMenuChanged;
        foreach (var item in _subMenuItems)
            WireSubMenuItem(item);
    }

    private void SubMenuChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var newItem in e.NewItems)
                WireSubMenuItem((FilterMenuItemViewModel<T>)newItem);
        }
        if (e.OldItems != null)
        {
            foreach (var oldItem in e.OldItems)
                UnWireSubMenuItem((FilterMenuItemViewModel<T>)oldItem);
        }
        IsAppliedChanged?.Invoke(this, EventArgs.Empty); 
        OnPropertyChanged(nameof(IsApplied)); 
        OnPropertyChanged(nameof(UnAppliedSubMenuItems)); 
    }

    private void WireSubMenuItem(IFilterMenuItemViewModel menuItem)
    {
        menuItem.IsAppliedChanged += RefreshIsApplied;
        menuItem.Changed += ForwardChangedEvent;
    }
    private void UnWireSubMenuItem(IFilterMenuItemViewModel menuItem)
    {
        menuItem.IsAppliedChanged -= RefreshIsApplied;
        menuItem.Changed -= ForwardChangedEvent;
    }
    private void RefreshIsApplied(object? sender, EventArgs e)
    {
        IsAppliedChanged?.Invoke(this, EventArgs.Empty); 
        OnPropertyChanged(nameof(IsApplied)); 
        OnPropertyChanged(nameof(UnAppliedSubMenuItems)); 
    }
    private void ForwardChangedEvent()
    {
        Changed?.Invoke();
    }
}
