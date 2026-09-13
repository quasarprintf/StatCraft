using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Attributes;
using System;
using System.Collections.Generic;
using System.Text;

namespace StatCraft.ViewModels.Windows.Filters;

public interface IWrappedFilterSlotViewModel : IFilterSlotViewModel
{
    IFilterSlotViewModel WrappedFilter { get; }
}

public abstract partial class WrappedFilterSlotViewModel<T> : ObservableObject, IWrappedFilterSlotViewModel
{
    public event Action? Changed;
    public event EventHandler? IsAppliedChanged;


    IFilterSlotViewModel IWrappedFilterSlotViewModel.WrappedFilter => WrappedFilter;
    [ObservableProperty] protected IFilterSlotViewModel<T> _wrappedFilter;

    public string Title
    {
        get => WrappedFilter.Title;
        set => WrappedFilter.Title = value;
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

    protected void BindWrapped() //must be called in constructor of implementing classes
    {
        WrappedFilter.IsAppliedChanged += IsAppliedChanged;
        WrappedFilter.Changed += Changed;
    }
}