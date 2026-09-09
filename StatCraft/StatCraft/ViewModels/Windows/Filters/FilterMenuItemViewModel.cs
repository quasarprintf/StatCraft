using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace StatCraft.ViewModels.Windows.Filters;

public partial class FilterMenuItemViewModel : ViewModelBase
{
    public event EventHandler? IsAppliedChanged;
    [ObservableProperty] private string _displayText;
    //should either have a Filter or SubMenuItems, but not both
    public FilterSlotViewModel? Filter { get; private set; }
    public ObservableCollection<FilterMenuItemViewModel>? SubMenuItems { get; private set; }
    public bool IsApplied => Filter != null ? Filter.IsApplied : SubMenuItems!.All(i => i.IsApplied);

    public IEnumerable<FilterSlotViewModel> ContainedFilters => Filter != null ? [Filter] : SubMenuItems!.SelectMany(i => i.ContainedFilters);

    public FilterMenuItemViewModel(FilterSlotViewModel filter)
    {
        Filter = filter;
        _displayText = filter.Title;
        Filter.IsAppliedChanged += RefreshIsApplied;
    }
    public FilterMenuItemViewModel(ObservableCollection<FilterMenuItemViewModel> subMenu, string name)
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
                WireSubMenuItem((FilterMenuItemViewModel)newItem);
        }
        if (e.OldItems != null)
        {
            foreach (var oldItem in e.OldItems)
                UnWireSubMenuItem((FilterMenuItemViewModel)oldItem);
        }
    }
    private void WireSubMenuItem(FilterMenuItemViewModel menuItem)
    {
        menuItem.IsAppliedChanged += RefreshIsApplied;
    }
    private void UnWireSubMenuItem(FilterMenuItemViewModel menuItem)
    {
        menuItem.IsAppliedChanged -= RefreshIsApplied;
    }
    private void RefreshIsApplied(object? sender, EventArgs e)
    {
        IsAppliedChanged?.Invoke(this, EventArgs.Empty); 
        OnPropertyChanged(nameof(IsApplied)); 
    }
}
