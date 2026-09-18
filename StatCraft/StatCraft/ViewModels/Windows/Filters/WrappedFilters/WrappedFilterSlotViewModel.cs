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
    public event EventHandler? IsAppliedChanged;

    [ObservableProperty] private IFilterSlotViewModel _wrappedFilter;

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
    public bool IsApplied
    {
        get => WrappedFilter.IsApplied;
        set => WrappedFilter.IsApplied = value;
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
    public IRelayCommand AddCommand => WrappedFilter.AddCommand;
    public IRelayCommand RemoveCommand => WrappedFilter.RemoveCommand;

    partial void OnWrappedFilterChanging(IFilterSlotViewModel value)
    {
        if (WrappedFilter != null)
        {
            WrappedFilter.IsAppliedChanged -= ForwardIsAppliedChanged;
            WrappedFilter.Changed -= ForwardChanged;
            WrappedFilter.PropertyChanged -= ForwardPropertyChanged;
        }
        if (value != null)
        {
            value.IsAppliedChanged += ForwardIsAppliedChanged;
            value.Changed += ForwardChanged;
            value.PropertyChanged += ForwardPropertyChanged;
        }
    }

    private void ForwardIsAppliedChanged(object? sender, EventArgs e)
    {
        IsAppliedChanged?.Invoke(this, e);
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