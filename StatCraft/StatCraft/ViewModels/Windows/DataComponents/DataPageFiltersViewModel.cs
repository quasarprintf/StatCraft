using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Builds;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataFiltering;

namespace StatCraft.ViewModels
{
    // Owns every filter dimension on the Data tab's filter bar. Player profile and date range are
    // always visible; the other five are "extra filters" that can be added/removed via the bar's
    // dropdown, each remembering its own on/off state independently of whether it currently constrains
    // anything (an added-but-empty filter is inactive, same as a hidden one).
    public partial class DataPageFiltersViewModel : ViewModelBase
    {
        // Set while SetSingleActiveProfile is bulk-updating state on a session start, so that update
        // doesn't trigger its own reload — the caller (DataPageViewModel.SetActiveProfile) always issues
        // exactly one explicit reload right afterward.
        private bool _suppressChangeEvents;

        public ObservableCollection<CheckboxFilterOptionViewModel<Sc2Profile>> ProfileOptions { get; } = [];

        // DateTime (not DateTimeOffset) because CalendarDatePicker.SelectedDate is DateTime?.
        [ObservableProperty] private DateTime? _fromDate;
        [ObservableProperty] private DateTime? _toDate;

        public CheckboxFilterSlotViewModel MapSlot { get; }
        public CheckboxFilterSlotViewModel MatchupSlot { get; }
        public CheckboxFilterSlotViewModel OutcomeSlot { get; }
        public NumericRangeFilterSlotViewModel MmrSlot { get; }
        public CheckboxFilterSlotViewModel BuildSlot { get; }

        // Fixed display order for both the bar itself and the "+ Filters" add-dropdown.
        public IReadOnlyList<FilterSlotViewModel> ExtraFilterSlots { get; }
        public IEnumerable<FilterSlotViewModel> VisibleExtraFilterSlots => ExtraFilterSlots.Where(s => s.IsVisible);
        public IEnumerable<FilterSlotViewModel> HiddenExtraFilterSlots => ExtraFilterSlots.Where(s => !s.IsVisible);

        // Checking/unchecking a profile changes which games need to be loaded from the database at all;
        // every other filter change only needs to re-filter the already-loaded set in memory.
        public event Action? ProfileSelectionChanged;
        public event Action? OtherFiltersChanged;

        internal DataPageFiltersViewModel(BuildRepository buildRepository)
        {
            MapSlot = new CheckboxFilterSlotViewModel("Map", [], showSearch: true);
            MatchupSlot = new CheckboxFilterSlotViewModel("Matchup", BuildMatchupOptions());
            OutcomeSlot = new CheckboxFilterSlotViewModel("Outcome", BuildOutcomeOptions());
            MmrSlot = new NumericRangeFilterSlotViewModel("Opponent MMR");
            BuildSlot = new CheckboxFilterSlotViewModel("Build", BuildBuildOptions(buildRepository));

            ExtraFilterSlots = [MapSlot, MatchupSlot, OutcomeSlot, MmrSlot, BuildSlot];
            foreach (FilterSlotViewModel slot in ExtraFilterSlots)
            {
                slot.Changed += () =>
                {
                    OnPropertyChanged(nameof(VisibleExtraFilterSlots));
                    OnPropertyChanged(nameof(HiddenExtraFilterSlots));
                    if (!_suppressChangeEvents)
                        OtherFiltersChanged?.Invoke();
                };
            }
        }

        partial void OnFromDateChanged(DateTime? value)
        {
            if (!_suppressChangeEvents)
                OtherFiltersChanged?.Invoke();
        }

        partial void OnToDateChanged(DateTime? value)
        {
            if (!_suppressChangeEvents)
                OtherFiltersChanged?.Invoke();
        }

        // Rebuilds the profile checkbox list (e.g. after linking a new account), preserving checked
        // state by profile id across the rebuild.
        internal void RefreshProfileOptions(IReadOnlyList<Sc2Profile> profiles)
        {
            HashSet<int> previouslyChecked = ProfileOptions.Where(o => o.IsChecked).Select(o => o.Value.Id).ToHashSet();

            foreach (CheckboxFilterOptionViewModel<Sc2Profile> option in ProfileOptions)
                option.PropertyChanged -= OnProfileOptionChanged;
            ProfileOptions.Clear();

            foreach (Sc2Profile profile in profiles)
            {
                CheckboxFilterOptionViewModel<Sc2Profile> option = new(profile, profile.DisplayName) { IsChecked = previouslyChecked.Contains(profile.Id) };
                option.PropertyChanged += OnProfileOptionChanged;
                ProfileOptions.Add(option);
            }
        }

        private void OnProfileOptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CheckboxFilterOptionViewModel.IsChecked) && !_suppressChangeEvents)
                ProfileSelectionChanged?.Invoke();
        }

        // Rebuilds the map filter's option list from the currently-loaded games' distinct map names,
        // preserving checked state by map name across the rebuild.
        internal void RefreshMapOptions(IEnumerable<string> distinctMapNames)
        {
            HashSet<string> previouslyChecked = MapSlot.Options
                .Cast<CheckboxFilterOptionViewModel<string>>()
                .Where(o => o.IsChecked)
                .Select(o => o.Value)
                .ToHashSet();

            IEnumerable<CheckboxFilterOptionViewModel> newOptions = distinctMapNames
                .OrderBy(m => m)
                .Select(m => (CheckboxFilterOptionViewModel)new CheckboxFilterOptionViewModel<string>(m, m) { IsChecked = previouslyChecked.Contains(m) });
            MapSlot.ReplaceOptions(newOptions);
        }

        // Collapses the profile filter to just the given profile and resets the date range to today —
        // called every time a session starts. Deliberately silent: the caller always follows this with
        // its own single explicit reload, so no intermediate event should fire here.
        internal void SetSingleActiveProfile(Sc2Profile profile)
        {
            _suppressChangeEvents = true;
            try
            {
                if (ProfileOptions.All(o => o.Value.Id != profile.Id))
                {
                    CheckboxFilterOptionViewModel<Sc2Profile> option = new(profile, profile.DisplayName) { IsChecked = true };
                    option.PropertyChanged += OnProfileOptionChanged;
                    ProfileOptions.Add(option);
                }

                foreach (CheckboxFilterOptionViewModel<Sc2Profile> option in ProfileOptions)
                    option.IsChecked = option.Value.Id == profile.Id;

                DateTime today = DateTime.Today;
                FromDate = today;
                ToDate = today;
            }
            finally
            {
                _suppressChangeEvents = false;
            }
        }

        internal GameFilterCriteria BuildCriteria()
        {
            DateOnly? fromDate = FromDate.HasValue ? DateOnly.FromDateTime(FromDate.Value.Date) : null;
            DateOnly? toDate = ToDate.HasValue ? DateOnly.FromDateTime(ToDate.Value.Date) : null;

            return new GameFilterCriteria(
                fromDate,
                toDate,
                ToSet<string>(MapSlot),
                ToSet<(Race, Race)>(MatchupSlot),
                ToSet<GameOutcome>(OutcomeSlot),
                MmrSlot.Min,
                MmrSlot.Max,
                ToBuildIdSet(BuildSlot));
        }

        private static IReadOnlySet<T> ToSet<T>(CheckboxFilterSlotViewModel slot) =>
            slot.Options.Cast<CheckboxFilterOptionViewModel<T>>().Where(o => o.IsChecked).Select(o => o.Value).ToHashSet();

        private static IReadOnlySet<int> ToBuildIdSet(CheckboxFilterSlotViewModel slot) =>
            slot.Options
                .Cast<CheckboxFilterOptionViewModel<BuildNode>>()
                .Where(o => o.IsChecked)
                .SelectMany(o => GameDataFilter.CollectSubtreeIds(o.Value))
                .ToHashSet();

        private static List<CheckboxFilterOptionViewModel> BuildMatchupOptions()
        {
            List<CheckboxFilterOptionViewModel> options = new();
            foreach (Race playerRace in Enum.GetValues<Race>())
                foreach (Race opponentRace in Enum.GetValues<Race>())
                    options.Add(new CheckboxFilterOptionViewModel<(Race, Race)>((playerRace, opponentRace), $"{playerRace}v{opponentRace}"));
            return options;
        }

        private static List<CheckboxFilterOptionViewModel> BuildOutcomeOptions() =>
            Enum.GetValues<GameOutcome>()
                .Select(outcome => (CheckboxFilterOptionViewModel)new CheckboxFilterOptionViewModel<GameOutcome>(outcome, outcome.ToString()))
                .ToList();

        // Every build across every race, grouped by race (Z, T, P) and flattened depth-first with an
        // indentation prefix so the tree structure is still legible in a flat checkbox list.
        private static List<CheckboxFilterOptionViewModel> BuildBuildOptions(BuildRepository buildRepository)
        {
            List<BuildNode> allNodes = buildRepository.GetAllBuilds();
            List<CheckboxFilterOptionViewModel> options = new();
            foreach (Race race in Enum.GetValues<Race>())
                foreach (BuildNode root in allNodes.Where(n => n.PlayerRace == race))
                    AddBuildOption(root, 0, options);
            return options;
        }

        private static void AddBuildOption(BuildNode node, int depth, List<CheckboxFilterOptionViewModel> options)
        {
            string label = depth == 0 ? $"{node.PlayerRace} — {node.Name}" : new string(' ', depth * 2) + node.Name;
            options.Add(new CheckboxFilterOptionViewModel<BuildNode>(node, label));
            foreach (BuildNode child in node.Children)
                AddBuildOption(child, depth + 1, options);
        }
    }
}
