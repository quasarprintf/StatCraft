using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StatCraft.ViewModels
{
    // An extra filter whose criteria is a set of checked options (map, matchup, outcome, build).
    public sealed partial class CheckboxFilterSlotViewModel : FilterSlotViewModel
    {
        public ObservableCollection<CheckboxFilterOptionViewModel> Options { get; } = [];

        // Only the map filter shows a search box to narrow its own (potentially long) checkbox list.
        public bool ShowSearch { get; }

        [ObservableProperty] private string _searchText = "";

        internal CheckboxFilterSlotViewModel(string title, IEnumerable<CheckboxFilterOptionViewModel> options, bool showSearch = false)
            : base(title)
        {
            ShowSearch = showSearch;
            ReplaceOptions(options);
        }

        // Rebuilds the option list (e.g. the map filter's options depend on which games are currently
        // loaded) — callers are responsible for preserving checked state across the rebuild themselves,
        // since only they know how to match "old" and "new" options for their particular value type.
        internal void ReplaceOptions(IEnumerable<CheckboxFilterOptionViewModel> options)
        {
            foreach (CheckboxFilterOptionViewModel option in Options)
                option.PropertyChanged -= OnOptionPropertyChanged;

            Options.Clear();
            foreach (CheckboxFilterOptionViewModel option in options)
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
            foreach (CheckboxFilterOptionViewModel option in Options)
                option.IsChecked = false;
        }
    }
}
