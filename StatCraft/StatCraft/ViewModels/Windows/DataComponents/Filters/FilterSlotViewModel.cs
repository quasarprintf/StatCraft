using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace StatCraft.ViewModels.Windows.DataComponents
{
    // One "extra filter" the Data tab's filter bar can show or hide. Two concrete subclasses (rather
    // than one class with a "kind" flag) so Avalonia's implicit per-x:DataType DataTemplate dispatch can
    // pick the right visual (checkbox dropdown vs. numeric range) automatically.
    public abstract partial class FilterSlotViewModel : ViewModelBase
    {
        // Mutable rather than the more usual get-only, so the Maps tab can rename a filter's attribute in
        // place (MapsPageViewModel.WireAttribute) without recreating the slot itself — recreating it would
        // drop whatever criteria the user already entered.
        [ObservableProperty] private string _title = "";

        [ObservableProperty] private bool _isVisible;

        // Whether entities with no value at all for this dimension still pass. Only the Maps tab binds
        // it: a newly defined map attribute is unset on every map, so without an opt-in an attribute
        // filter would hide the very maps the user most likely wants to find and fill in. The Data tab's
        // dimensions all come from the replay and are never unset, so its templates simply don't show it.
        [ObservableProperty] private bool _includeUnset;

        // Raised whenever this slot's own criteria changes in a way that should affect which games are
        // shown — visibility toggling here, or (in each concrete subclass) its own selection/bounds.
        public event Action? Changed;
        protected void RaiseChanged() => Changed?.Invoke();

        // Raised only when IsVisible itself toggles — deliberately separate from Changed so that typing
        // into a numeric range or checking an option (which also raises Changed) doesn't make the filter
        // bar's own ItemsControl think the set of visible slots changed and rebuild its item containers,
        // which would tear down and recreate whatever control the user is actively focused on/typing in.
        public event Action? VisibilityChanged;

        protected FilterSlotViewModel(string title)
        {
            Title = title;
        }

        // Resets this filter's own selection/bounds back to "inactive" — called when removed, so a
        // hidden filter never silently keeps constraining results.
        public abstract void Clear();

        partial void OnIsVisibleChanged(bool value)
        {
            VisibilityChanged?.Invoke();
            RaiseChanged();
        }

        partial void OnIncludeUnsetChanged(bool value) => RaiseChanged();

        [RelayCommand]
        private void Add() => IsVisible = true;

        [RelayCommand]
        private void Remove()
        {
            IsVisible = false;
            IncludeUnset = false;
            Clear();
        }
    }
}
