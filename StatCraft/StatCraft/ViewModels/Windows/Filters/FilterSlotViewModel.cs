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
    bool IncludeUnset { get; set; }
    bool AllowIncludeUnset { get; set; }
    event Action? Changed;
    event EventHandler? Removed;
    void Clear();
    IRelayCommand RemoveCommand { get; }
}

public interface IFilterSlotViewModel<T> : IFilterSlotViewModel
{
    IFilter<T> GetFilter();
}

public abstract partial class FilterSlotViewModel<T,F> : ViewModelBase, IFilterSlotViewModel<T>
{
    public Func<T,F> FilteredPropertyMap { get; set; }

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial bool Mandatory { get; set; }
    [ObservableProperty] public partial bool IncludeUnset { get; set; }

    public bool AllowIncludeUnset { get; set; }

    public event Action? Changed;
    protected void RaiseChanged() => Changed?.Invoke();

    public event EventHandler? Removed;

    protected FilterSlotViewModel(string title, Func<T,F> filteredPropertyMap)
    {
        Title = title;
        FilteredPropertyMap = filteredPropertyMap;
    }

    public abstract IFilter<T> GetFilter();

    // Resets this filter's own selection/bounds back to "inactive" — called when removed, so a
    // hidden filter never silently keeps constraining results.
    public abstract void Clear();

    partial void OnIncludeUnsetChanged(bool value) => RaiseChanged();

    [RelayCommand]
    private void Remove()
    {
        if (Mandatory)
        {
            IncludeUnset = false;
            Clear();
        }
        else
        {
            Removed?.Invoke(this, EventArgs.Empty);
        }
    }
}
