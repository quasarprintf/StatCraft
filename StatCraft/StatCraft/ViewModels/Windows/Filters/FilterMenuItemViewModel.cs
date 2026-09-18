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

public interface IFilterMenuItemViewModel
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
    //should either have a Filter or SubMenuItems, but not both
    IFilterSlotViewModel? IFilterMenuItemViewModel.Filter => Filter;
    public IFilterSlotViewModel<T>? Filter { get; private set; }

    private ObservableCollection<FilterMenuItemViewModel<T>>? _subMenuItems { get; set; }
    public IEnumerable<IFilterMenuItemViewModel>? SubMenuItems => _subMenuItems;
    public IEnumerable<IFilterMenuItemViewModel>? UnAppliedSubMenuItems => _subMenuItems?.Where(i => !i.IsApplied);
    public bool IsApplied => Filter != null ? Filter.IsApplied : SubMenuItems!.All(i => i.IsApplied);

    IEnumerable<IFilterSlotViewModel> IFilterMenuItemViewModel.ContainedFilters => ContainedFilters;
    public IEnumerable<IFilterSlotViewModel<T>> ContainedFilters => Filter != null ? [Filter] : _subMenuItems!.SelectMany(i => i.ContainedFilters);

    public FilterMenuItemViewModel(IFilterSlotViewModel<T> filter)
    {
        Filter = filter;
        _displayText = filter.Title;
        Filter.IsAppliedChanged += RefreshIsApplied;
        Filter.Changed += ForwardChangedEvent;
    }
    public FilterMenuItemViewModel(ObservableCollection<FilterMenuItemViewModel<T>> subMenu, string name)
    {
        _displayText = name;
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
