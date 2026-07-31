using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace StatCraft.ViewModels
{
    // One "extra filter" the Data tab's filter bar can show or hide. Two concrete subclasses (rather
    // than one class with a "kind" flag) so Avalonia's implicit per-x:DataType DataTemplate dispatch can
    // pick the right visual (checkbox dropdown vs. numeric range) automatically.
    public abstract partial class FilterSlotViewModel : ViewModelBase
    {
        public string Title { get; }

        [ObservableProperty] private bool _isVisible;

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

        [RelayCommand]
        private void Add() => IsVisible = true;

        [RelayCommand]
        private void Remove()
        {
            IsVisible = false;
            Clear();
        }
    }
}
