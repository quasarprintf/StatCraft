using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace StatCraft.ViewModels.Windows.Filters.WrappedFilters;

public interface IWrappedFilterSlotViewModel : IFilterSlotViewModel
{
    IFilterSlotViewModel WrappedFilter { get; }
}

public abstract partial class WrappedFilterSlotViewModel<T> : ViewModelBase, IWrappedFilterSlotViewModel
{
    public event Action? Changed;
    public event EventHandler? Removed;

    [ObservableProperty] public required partial IFilterSlotViewModel WrappedFilter { get; set; }

    public string Title
    {
        get => WrappedFilter.Title;
        set => WrappedFilter.Title = value;
    }

    public bool Mandatory
    {
        get => WrappedFilter.Mandatory;
        set => WrappedFilter.Mandatory = value;
    }
    public bool IncludeUnset
    {
        get => WrappedFilter.IncludeUnset;
        set => WrappedFilter.IncludeUnset = value;
    }
    public bool AllowIncludeUnset
    {
        get => WrappedFilter.AllowIncludeUnset;
        set => WrappedFilter.AllowIncludeUnset = value;
    }

    public void Clear()
    {
        WrappedFilter.Clear();
    }
    public IRelayCommand RemoveCommand => WrappedFilter.RemoveCommand;

    partial void OnWrappedFilterChanging(IFilterSlotViewModel value)
    {
        if (WrappedFilter != null)
        {
            WrappedFilter.Removed -= ForwardRemoved;
            WrappedFilter.Changed -= ForwardChanged;
            WrappedFilter.PropertyChanged -= ForwardPropertyChanged;
        }
        if (value != null)
        {
            value.Removed += ForwardRemoved;
            value.Changed += ForwardChanged;
            value.PropertyChanged += ForwardPropertyChanged;
        }
    }

    private void ForwardRemoved(object? sender, EventArgs e)
    {
        Removed?.Invoke(this, e);
    }
    private void ForwardChanged()
    {
        Changed?.Invoke();
    }
    private void ForwardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}