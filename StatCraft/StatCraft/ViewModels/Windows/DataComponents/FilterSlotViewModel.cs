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

        // Raised whenever this slot's own state changes in a way that should affect which games are
        // shown — visibility toggling here, or (in each concrete subclass) its own selection/bounds.
        public event Action? Changed;
        protected void RaiseChanged() => Changed?.Invoke();

        protected FilterSlotViewModel(string title)
        {
            Title = title;
        }

        // Resets this filter's own selection/bounds back to "inactive" — called when removed, so a
        // hidden filter never silently keeps constraining results.
        public abstract void Clear();

        partial void OnIsVisibleChanged(bool value) => RaiseChanged();

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
