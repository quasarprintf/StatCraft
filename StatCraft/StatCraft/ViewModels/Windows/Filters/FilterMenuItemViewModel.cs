using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    IRelayCommand AddCommand { get; }
    event Action? Changed;
    string DisplayText { get; set; }
    Func<IFilterSlotViewModel>? FilterFactory { get; }
    IEnumerable<IFilterSlotViewModel> AppliedFilters { get; }
    IEnumerable<IFilterMenuItemViewModel>? SubMenuItems { get; }
    IEnumerable<IFilterMenuItemViewModel>? UnAppliedSubMenuItems { get; }
    IEnumerable<IFilterSlotViewModel> ContainedFilters { get; }
    public bool IsUnique { get; set; }
    public bool IsAvailable { get; }
}
public partial class FilterMenuItemViewModel<T> : ViewModelBase, IFilterMenuItemViewModel
{
    public event Action? Changed;
    public event EventHandler? IsAppliedChanged;
    [ObservableProperty] public partial string DisplayText { get; set; }
    Func<IFilterSlotViewModel>? IFilterMenuItemViewModel.FilterFactory => FilterFactory;
    public Func<IFilterSlotViewModel<T>>? FilterFactory { get; private set; }

    IEnumerable<IFilterSlotViewModel> IFilterMenuItemViewModel.AppliedFilters => AppliedFilters;
    public ObservableCollection<IFilterSlotViewModel<T>> AppliedFilters { get; private set; } = new ObservableCollection<IFilterSlotViewModel<T>>();

    private ObservableCollection<FilterMenuItemViewModel<T>>? _subMenuItems { get; set; }
    public IEnumerable<IFilterMenuItemViewModel>? SubMenuItems => _subMenuItems;
    public IEnumerable<IFilterMenuItemViewModel>? UnAppliedSubMenuItems => _subMenuItems;
    public bool IsUnique { get; set; }
    public bool IsAvailable => !IsUnique || AppliedFilters.Count == 0;

    IEnumerable<IFilterSlotViewModel> IFilterMenuItemViewModel.ContainedFilters => ContainedFilters;
    public IEnumerable<IFilterSlotViewModel<T>> ContainedFilters
    {
        get
        {
            foreach (var filter in AppliedFilters)
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
    public FilterMenuItemViewModel(Func<IFilterSlotViewModel<T>> filterFactory, string name, ObservableCollection<FilterMenuItemViewModel<T>> subMenu)
    {
        DisplayText = name;
        FilterFactory = filterFactory;
        WireSubMenu(subMenu);
    }

    public void Destroy()
    {
        if (_subMenuItems != null)
        {
            foreach (var item in _subMenuItems)
                UnWireSubMenuItem(item);
            _subMenuItems.CollectionChanged -= SubMenuChanged;
        }
        FilterFactory = null;
        AppliedFilters = null;
        _subMenuItems = null;
    }
    public void DestroyRecursively()
    {
        if (_subMenuItems != null)
        {
            foreach (var item in _subMenuItems)
                item.DestroyRecursively();
        }
        Destroy();
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
        OnPropertyChanged(nameof(UnAppliedSubMenuItems)); 
    }
    private void ForwardChangedEvent()
    {
        Changed?.Invoke();
    }

    [RelayCommand]
    public void Add()
    {
        if (FilterFactory == null)
        {
            //TODO: log this, shouldn't happen
            return;
        }
        if (!IsAvailable)
            return;
        AppliedFilters.Add(FilterFactory());
        RefreshIsApplied(null, null); //TODO: clean this up
    }
}
