using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Services.DataFiltering;
using System;
using System.ComponentModel;

namespace StatCraft.ViewModels.Windows.Filters;

public interface IFilterSlotViewModel : INotifyPropertyChanged
{
    string Title { get; set; }
    bool Mandatory { get; set; }
    bool IsApplied { get; set; }
    bool IncludeUnset { get; set; }
    bool AllowIncludeUnset { get; set; }
    event Action? Changed;
    event EventHandler? IsAppliedChanged;
    void Clear();
    IRelayCommand AddCommand { get; }
    IRelayCommand RemoveCommand { get; }
}

public interface IFilterSlotViewModel<T> : IFilterSlotViewModel
{
    IFilter<T> GetFilter();
}

// One "extra filter" the Data tab's filter bar can show or hide. Two concrete subclasses (rather
// than one class with a "kind" flag) so Avalonia's implicit per-x:DataType DataTemplate dispatch can
// pick the right visual (checkbox dropdown vs. numeric range) automatically.
public abstract partial class FilterSlotViewModel<T,F> : ViewModelBase, IFilterSlotViewModel<T>
{
    public Func<T,F> FilteredPropertyMap { get; set; }

    // Mutable rather than the more usual get-only, so the Maps tab can rename a filter's attribute in
    // place (MapsPageViewModel.WireAttribute) without recreating the slot itself — recreating it would
    // drop whatever criteria the user already entered.
    [ObservableProperty] private string _title = "";

    [ObservableProperty] private bool _mandatory;
    [ObservableProperty] private bool _isApplied;

    // Whether entities with no value at all for this dimension still pass. Only the Maps tab binds
    // it: a newly defined map attribute is unset on every map, so without an opt-in an attribute
    // filter would hide the very maps the user most likely wants to find and fill in. The Data tab's
    // dimensions all come from the replay and are never unset, so its templates simply don't show it.
    [ObservableProperty] private bool _includeUnset;

    public bool AllowIncludeUnset { get; set; }

    // Raised whenever this slot's own criteria changes in a way that should affect which games are
    // shown — visibility toggling here, or (in each concrete subclass) its own selection/bounds.
    public event Action? Changed;
    protected void RaiseChanged() => Changed?.Invoke();

    public event EventHandler? IsAppliedChanged;

    protected FilterSlotViewModel(string title, Func<T,F> filteredPropertyMap)
    {
        Title = title;
        FilteredPropertyMap = filteredPropertyMap;
    }

    public abstract IFilter<T> GetFilter();

    // Resets this filter's own selection/bounds back to "inactive" — called when removed, so a
    // hidden filter never silently keeps constraining results.
    public abstract void Clear();

    partial void OnIsAppliedChanged(bool value)
    {
        IsAppliedChanged?.Invoke(this, EventArgs.Empty);
        RaiseChanged();
    }

    partial void OnIncludeUnsetChanged(bool value) => RaiseChanged();

    [RelayCommand]
    private void Add() => IsApplied = true;

    [RelayCommand]
    private void Remove()
    {
        IsApplied = false;
        IncludeUnset = false;
        Clear();
    }
}
