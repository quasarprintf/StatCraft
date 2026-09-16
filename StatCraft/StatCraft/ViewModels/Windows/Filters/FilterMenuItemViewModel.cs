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
    string DisplayText { get; set; }
    IFilterSlotViewModel? Filter { get; }
    ObservableCollection<IFilterMenuItemViewModel>? SubMenuItems { get; }
    bool IsApplied { get; }
    IEnumerable<IFilterSlotViewModel> ContainedFilters { get; }
}
public partial class FilterMenuItemViewModel<T> : ViewModelBase, IFilterMenuItemViewModel
{
    public event EventHandler? IsAppliedChanged;
    [ObservableProperty] private string _displayText;
    //should either have a Filter or SubMenuItems, but not both
    IFilterSlotViewModel? IFilterMenuItemViewModel.Filter => Filter;
    public IFilterSlotViewModel<T>? Filter { get; private set; }

    public ObservableCollection<IFilterMenuItemViewModel>? SubMenuItems { get; private set; }
    public bool IsApplied => Filter != null ? Filter.IsApplied : SubMenuItems!.All(i => i.IsApplied);

    IEnumerable<IFilterSlotViewModel> IFilterMenuItemViewModel.ContainedFilters => ContainedFilters;
    public IEnumerable<IFilterSlotViewModel<T>> ContainedFilters => Filter != null ? [Filter] : SubMenuItems!.SelectMany(i => i.ContainedFilters).OfType<IFilterSlotViewModel<T>>();

    public FilterMenuItemViewModel(IFilterSlotViewModel<T> filter)
    {
        Filter = filter;
        _displayText = filter.Title;
        Filter.IsAppliedChanged += RefreshIsApplied;
    }
    public FilterMenuItemViewModel(ObservableCollection<IFilterMenuItemViewModel> subMenu, string name)
    {
        _displayText = name;
        SubMenuItems = subMenu;
        SubMenuItems.CollectionChanged += SubMenuChanged;
        foreach (var item in SubMenuItems)
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
    }

    private void WireSubMenuItem(IFilterMenuItemViewModel menuItem)
    {
        menuItem.IsAppliedChanged += RefreshIsApplied;
    }
    private void UnWireSubMenuItem(IFilterMenuItemViewModel menuItem)
    {
        menuItem.IsAppliedChanged -= RefreshIsApplied;
    }
    private void RefreshIsApplied(object? sender, EventArgs e)
    {
        IsAppliedChanged?.Invoke(this, EventArgs.Empty); 
        OnPropertyChanged(nameof(IsApplied)); 
    }
}
