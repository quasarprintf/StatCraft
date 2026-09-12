using CommunityToolkit.Mvvm.ComponentModel;
using System;
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
    ReadOnlyObservableCollection<IFilterMenuItemViewModel>? SubMenuItems { get; }
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

    public ReadOnlyObservableCollection<IFilterMenuItemViewModel>? SubMenuItems { get; private set; }
    private ObservableCollection<FilterMenuItemViewModel<T>>? GenericSubMenuItems { get; set; }
    public bool IsApplied => Filter != null ? Filter.IsApplied : GenericSubMenuItems!.All(i => i.IsApplied);

    IEnumerable<IFilterSlotViewModel> IFilterMenuItemViewModel.ContainedFilters => ContainedFilters;
    public IEnumerable<IFilterSlotViewModel<T>> ContainedFilters => Filter != null ? [Filter] : GenericSubMenuItems!.SelectMany(i => i.ContainedFilters);

    public FilterMenuItemViewModel(IFilterSlotViewModel<T> filter)
    {
        Filter = filter;
        _displayText = filter.Title;
        Filter.IsAppliedChanged += RefreshIsApplied;
    }
    public FilterMenuItemViewModel(ObservableCollection<FilterMenuItemViewModel<T>> subMenu, string name)
    {
        _displayText = name;
        GenericSubMenuItems = subMenu;
        GenericSubMenuItems.CollectionChanged += SubMenuChanged;
        foreach (var item in GenericSubMenuItems)
            WireSubMenuItem(item);
        var subMenuItems = new ObservableCollection<IFilterMenuItemViewModel>(GenericSubMenuItems);
        SubMenuItems = new ReadOnlyObservableCollection<IFilterMenuItemViewModel>(subMenuItems);
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
    private void WireSubMenuItem(FilterMenuItemViewModel<T> menuItem)
    {
        menuItem.IsAppliedChanged += RefreshIsApplied;
    }
    private void UnWireSubMenuItem(FilterMenuItemViewModel<T> menuItem)
    {
        menuItem.IsAppliedChanged -= RefreshIsApplied;
    }
    private void RefreshIsApplied(object? sender, EventArgs e)
    {
        IsAppliedChanged?.Invoke(this, EventArgs.Empty); 
        OnPropertyChanged(nameof(IsApplied)); 
    }
}
