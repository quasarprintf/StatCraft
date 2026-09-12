using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Services.DataFiltering;
using StatCraft.Services.DataFiltering.SequentialFilters;

namespace StatCraft.ViewModels.Windows.Filters;

// Non-generic base is what the filter bar's shared extra-filter-row DataTemplate binds against
// (Title/ShowSearch/SearchText/RemoveCommand, plus a loosely-typed Options view) so Avalonia can
// render any checkbox filter (Map/Matchup/Outcome/Build/Profile) with one visual regardless of the
// option value type; every C# call site instead holds the generic subclass directly, so it never
// needs to cast Options back to CheckboxFilterOptionViewModel<T> to reach Value.
public interface ICheckboxFilterSlotViewModel : IFilterSlotViewModel
{
    IEnumerable<ICheckboxFilterOptionViewModel> Options { get; }

    // Shows a search box to filter the checkbox list.
    bool ShowSearch { get; }

    // How many columns the checkbox list lays out in — 1 for most filters, but e.g. the Matchup
    // filter's fixed 9 options read better as a 3x3 grid than one long column.
    int Columns { get; }

    string SearchText { get; set; }
}

// An extra filter whose criteria is a set of checked options (map, matchup, outcome, build, profile).
public sealed partial class CheckboxFilterSlotViewModel<T,F> : FilterSlotViewModel<T,IEnumerable<F>>, ICheckboxFilterSlotViewModel
{
    // Shows a search box to filter the checkbox list.
    public bool ShowSearch { get; }

    // How many columns the checkbox list lays out in — 1 for most filters, but e.g. the Matchup
    // filter's fixed 9 options read better as a 3x3 grid than one long column.
    public int Columns { get; }

    [ObservableProperty] private string _searchText = "";

    IEnumerable<ICheckboxFilterOptionViewModel> ICheckboxFilterSlotViewModel.Options => Options;
    public ObservableCollection<CheckboxFilterOptionViewModel<F>> Options { get; } = [];

    public CheckboxFilterSlotViewModel(string title, IEnumerable<CheckboxFilterOptionViewModel<F>> options, Func<T,IEnumerable<F>> filteredPropertyMap, bool showSearch = false, int columns = 1)
        : base(title, filteredPropertyMap)
    {
        ShowSearch = showSearch;
        Columns = columns;
        ReplaceOptions(options);
    }

    public override SequentialAnyFilter<T,F> GetFilter()
    {
        HashSet<F> selectedOptions = new HashSet<F>();
        foreach (var option in Options)
        {
            if (option.IsChecked)
                selectedOptions.Add(option.Value);
        }
        SetMemberFilter<F,F> memberFilter = new SetMemberFilter<F, F>(x => x)
        {
            AcceptNull = IncludeUnset,
            FilterValue = selectedOptions
        };
        return new SequentialAnyFilter<T, F>(memberFilter, FilteredPropertyMap)
        {
            AcceptNull = IncludeUnset
        };
    }

    // Rebuilds the option list (e.g. the map filter's options depend on which games are currently
    // loaded) — callers are responsible for preserving checked state across the rebuild themselves,
    // since only they know how to match "old" and "new" options for their particular value type.
    internal void ReplaceOptions(IEnumerable<CheckboxFilterOptionViewModel<F>> options)
    {
        foreach (CheckboxFilterOptionViewModel<F> option in Options)
            option.PropertyChanged -= OnOptionPropertyChanged;

        Options.Clear();
        foreach (CheckboxFilterOptionViewModel<F> option in options)
        {
            option.PropertyChanged += OnOptionPropertyChanged;
            Options.Add(option);
        }
    }

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ICheckboxFilterOptionViewModel.IsChecked))
            RaiseChanged();
    }

    public override void Clear()
    {
        foreach (CheckboxFilterOptionViewModel<F> option in Options)
            option.IsChecked = false;
    }
}
