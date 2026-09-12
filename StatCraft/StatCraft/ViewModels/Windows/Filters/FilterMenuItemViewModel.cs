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
    // Backs SubMenuItems. A separate collection from GenericSubMenuItems because the view binds
    // without knowing T, which means every change to the generic one has to be replayed onto it.
    private ObservableCollection<IFilterMenuItemViewModel>? _subMenuItems;
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
        _subMenuItems = new ObservableCollection<IFilterMenuItemViewModel>(GenericSubMenuItems);
        SubMenuItems = new ReadOnlyObservableCollection<IFilterMenuItemViewModel>(_subMenuItems);
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
        MirrorSubMenuChange(e);
    }

    // Replays a change onto _subMenuItems, which is what the menu is actually bound to — without this
    // the bound collection keeps whatever it was built with and the menu silently goes stale.
    private void MirrorSubMenuChange(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                InsertMirrored(e.NewItems!, e.NewStartingIndex);
                break;
            case NotifyCollectionChangedAction.Remove:
                RemoveMirrored(e.OldStartingIndex, e.OldItems!.Count);
                break;
            case NotifyCollectionChangedAction.Replace:
                RemoveMirrored(e.OldStartingIndex, e.OldItems!.Count);
                InsertMirrored(e.NewItems!, e.NewStartingIndex);
                break;
            case NotifyCollectionChangedAction.Move:
                _subMenuItems!.Move(e.OldStartingIndex, e.NewStartingIndex);
                break;
            // Reset carries no items, so the mirror is rebuilt from the source rather than patched.
            case NotifyCollectionChangedAction.Reset:
                _subMenuItems!.Clear();
                foreach (var item in GenericSubMenuItems!)
                    _subMenuItems.Add(item);
                break;
        }
    }
    private void InsertMirrored(IList newItems, int startingIndex)
    {
        for (int i = 0; i < newItems.Count; i++)
            _subMenuItems!.Insert(startingIndex + i, (IFilterMenuItemViewModel)newItems[i]!);
    }
    private void RemoveMirrored(int startingIndex, int count)
    {
        for (int i = 0; i < count; i++)
            _subMenuItems!.RemoveAt(startingIndex);
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
