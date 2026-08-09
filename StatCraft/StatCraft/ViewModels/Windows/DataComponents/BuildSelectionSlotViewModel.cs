using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StatCraft.Models.GameData.Builds;
using StatCraft.Services.BackgroundService;
using StatCraft.Services.DataParsing;

namespace StatCraft.ViewModels
{
    // One BuildPathPicker's worth of state: which build (if any) is selected in this slot. A
    // GameDataRowViewModel owns a growable list of these so a single game can reference multiple builds.
    public partial class BuildSelectionSlotViewModel : ObservableObject
    {
        // Sentinel meaning "no build in this slot", selectable from the menu like any other leaf node.
        // Recognized by reference identity in SelectBuild below.
        internal static readonly BuildNode NoneOption = new() { Id = -1, Name = "(none)" };

        private static readonly string DEFAULT_BUILD_TEXT = "";

        public ObservableCollection<BuildNode> BuildTree { get; }

        // NoneOption followed by BuildTree's contents, kept in sync since BuildTree is a shared, live
        // collection that DataPageViewModel mutates in place when the Builds tab changes.
        public ObservableCollection<BuildNode> MenuOptions { get; } = [];

        public bool IsBuildPickerEnabled { get; }

        [ObservableProperty] private BuildNode? _selectedBuildNode;
        [ObservableProperty] private string _selectedBuildLabel = DEFAULT_BUILD_TEXT;

        // Raised whenever SelectedBuildNode changes, passing the value it changed from. Callers that need
        // to hydrate a saved selection without triggering side effects should set SelectedBuildNode via
        // object initializer before subscribing here. The old value lets a subscriber revert a rejected
        // selection (e.g. a duplicate) without needing to track prior state itself.
        public event Action<BuildSelectionSlotViewModel, BuildNode?>? SelectionChanged;

        private readonly ILogger _logger;

        internal BuildSelectionSlotViewModel(ObservableCollection<BuildNode>? buildTree, ILogger logger)
        {
            BuildTree = buildTree ?? [];
            IsBuildPickerEnabled = buildTree != null;
            _logger = logger;

            RebuildMenuOptions();
            BuildTree.CollectionChanged += OnBuildTreeChanged;
        }

        private void OnBuildTreeChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildMenuOptions();

        private void RebuildMenuOptions()
        {
            MenuOptions.Clear();
            MenuOptions.Add(NoneOption);
            foreach (BuildNode node in BuildTree)
                MenuOptions.Add(node);
        }

        [RelayCommand]
        private void SelectBuild(BuildNode? node) =>
            SelectedBuildNode = ReferenceEquals(node, NoneOption) ? null : node;

        partial void OnSelectedBuildNodeChanged(BuildNode? oldValue, BuildNode? newValue)
        {
            List<BuildNode>? path = newValue == null ? null : BuildPathHelper.FindPath(BuildTree, newValue.Id);

            // Shouldn't happen — newValue always comes from this same BuildTree, whether from the menu
            // (MenuOptions mirrors BuildTree) or from hydrating a saved selection (also looked up in
            // BuildTree). Logged rather than crashing the whole app over a label that just stays blank.
            if (newValue != null && path == null)
                _logger.LogError($"Selected build \"{newValue.Name}\" (Id={newValue.Id}) was not found in its own BuildTree.");

            SelectedBuildLabel = path == null ? DEFAULT_BUILD_TEXT : string.Join(" > ", path.Select(n => n.Name));
            SelectionChanged?.Invoke(this, oldValue);
        }
    }
}
