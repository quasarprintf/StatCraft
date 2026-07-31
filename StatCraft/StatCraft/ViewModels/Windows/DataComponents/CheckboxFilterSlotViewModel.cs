using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StatCraft.ViewModels
{
    // Non-generic base is what the filter bar's shared extra-filter-row DataTemplate binds against
    // (Title/ShowSearch/SearchText/RemoveCommand, plus a loosely-typed Options view) so Avalonia can
    // render any checkbox filter (Map/Matchup/Outcome/Build/Profile) with one visual regardless of the
    // option value type; every C# call site instead holds the generic subclass directly, so it never
    // needs to cast Options back to CheckboxFilterOptionViewModel<T> to reach Value.
    public abstract partial class CheckboxFilterSlotViewModel : FilterSlotViewModel
    {
        public abstract IEnumerable<CheckboxFilterOptionViewModel> Options { get; }

        // Shows a search box to filter the checkbox list.
        public bool ShowSearch { get; }

        // How many columns the checkbox list lays out in — 1 for most filters, but e.g. the Matchup
        // filter's fixed 9 options read better as a 3x3 grid than one long column.
        public int Columns { get; }

        [ObservableProperty] private string _searchText = "";

        protected CheckboxFilterSlotViewModel(string title, bool showSearch, int columns) : base(title)
        {
            ShowSearch = showSearch;
            Columns = columns;
        }
    }

    // An extra filter whose criteria is a set of checked options (map, matchup, outcome, build, profile).
    public sealed partial class CheckboxFilterSlotViewModel<T> : CheckboxFilterSlotViewModel
    {
        public override ObservableCollection<CheckboxFilterOptionViewModel<T>> Options { get; } = [];

        internal CheckboxFilterSlotViewModel(string title, IEnumerable<CheckboxFilterOptionViewModel<T>> options, bool showSearch = false, int columns = 1)
            : base(title, showSearch, columns)
        {
            ReplaceOptions(options);
        }

        // Rebuilds the option list (e.g. the map filter's options depend on which games are currently
        // loaded) — callers are responsible for preserving checked state across the rebuild themselves,
        // since only they know how to match "old" and "new" options for their particular value type.
        internal void ReplaceOptions(IEnumerable<CheckboxFilterOptionViewModel<T>> options)
        {
            foreach (CheckboxFilterOptionViewModel<T> option in Options)
                option.PropertyChanged -= OnOptionPropertyChanged;

            Options.Clear();
            foreach (CheckboxFilterOptionViewModel<T> option in options)
            {
                option.PropertyChanged += OnOptionPropertyChanged;
                Options.Add(option);
            }
        }

        private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CheckboxFilterOptionViewModel.IsChecked))
                RaiseChanged();
        }

        public override void Clear()
        {
            foreach (CheckboxFilterOptionViewModel<T> option in Options)
                option.IsChecked = false;
        }
    }
}
