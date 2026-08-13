using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using StatCraft.Models.Battlenet;
using StatCraft.Services.BackgroundService;
using StatCraft.ViewModels;
using StatCraft.Views.Components;

namespace StatCraft.Views
{
    public partial class DataPage : UserControl
    {
        private DataPageViewModel ViewModel => (DataPageViewModel)DataContext!;

        public DataPage()
        {
            InitializeComponent();

            DataPageViewModel vm = App.Services.GetRequiredService<DataPageViewModel>();
            vm.SessionRequested += async () => await OnSessionRequestedAsync();
            vm.DeleteGameConfirmationRequested += async row => await OnDeleteGameConfirmationRequestedAsync(row);
            vm.ImportReplayRequested += async () => await OnImportReplayRequestedAsync();
            vm.LaunchReplayFailed += async message => await OnLaunchReplayFailedAsync(message);
            DataContext = vm;

            GamesGrid.CellPointerPressed += OnGamesGridCellPointerPressed;

            // Rows are recycled as the grid scrolls, so a newly realised row has to be told whether it
            // is the one currently showing its build details.
            GamesGrid.LoadingRow += (_, e) => ApplyBuildDetailsVisibility(e.Row);

            //avalonia DataGrid scrolling breaks when build details are visible. It randomly jumps back to the top sometimes
            //this is a convoluted workaround to make it usable.
            //when build details are opened, snap that row to the top of the screen and lock scrolling.
            GamesGrid.LayoutUpdated += (_, _) =>
            {
                if (_mainTableScrollLocked && !_scrollToTopPending)
                    SetMainTableScrollLocked(true);
            };

            //For some reason IsScrollChainingEnabled being set on the build details ScrollViewer doesn't prevent the PointerWheelChangedEvent from propagating to the DataGrid
            //so need to handle it manually
            GamesGrid.AddHandler(PointerWheelChangedEvent, OnGamesGridWheelChangedWhileLocked, RoutingStrategies.Tunnel);
            GamesGrid.LoadingRowDetails += (_, e) =>
            {
                if (e.DetailsElement is ScrollViewer detailsScrollViewer)
                {
                    detailsScrollViewer.RemoveHandler(PointerWheelChangedEvent, OnRowDetailsScrollViewerWheelChanged);
                    detailsScrollViewer.AddHandler(PointerWheelChangedEvent, OnRowDetailsScrollViewerWheelChanged,
                        RoutingStrategies.Tunnel, handledEventsToo: true);
                }
            };
        }

        private static void OnRowDetailsScrollViewerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            var scrollViewer = (ScrollViewer)sender!;
            double maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            double newY = Math.Clamp(scrollViewer.Offset.Y - e.Delta.Y * 50, 0, maxY);
            scrollViewer.Offset = scrollViewer.Offset.WithY(newY);
            e.Handled = true;
        }

        private bool _mainTableScrollLocked;

        private void SetMainTableScrollLocked(bool locked)
        {
            _mainTableScrollLocked = locked;

            ScrollBar? verticalScrollBar = GamesGrid.GetVisualDescendants().OfType<ScrollBar>()
                .FirstOrDefault(s => s.Name == "PART_VerticalScrollbar");
            if (verticalScrollBar != null)
                verticalScrollBar.IsEnabled = !locked;
        }

        private void OnGamesGridWheelChangedWhileLocked(object? sender, PointerWheelEventArgs e)
        {
            if (!_mainTableScrollLocked) return;

            //block scrolling on the main DataGrid. Allow it on the build details ScrollViewer
            bool? targetingDataGrid = (e.Source as Visual)?.GetVisualAncestors().OfType<DataGridCell>().Any();
            if (targetingDataGrid ?? false)
                e.Handled = true;
        }

        private bool _scrollToTopPending;
        private int _scrollToTopAttempts;
        private const int MaxScrollToTopAttempts = 20;
        private double? _lastMeasuredScrollToTopValue;
        private double? _lastMeasuredRowHeight;
        private double? _lastMeasuredScrollBarMaximum;
        private int _consecutiveStalledReadings;
        private int _consecutiveGrowingScrollBarMaximum;
        private int _walkStepIndex;
        private double? _walkStepSizeEstimate;
        private const int RunawayConsecutiveGrowingReadings = 5;

        private void AdvanceScrollToTopIfPending()
        {
            if (!_scrollToTopPending) return;

            (double Top, double Height, double? ScrollBarMaximum)? metrics = GetBuildDetailsRowMetrics();
            bool atTop = metrics.HasValue && Math.Abs(metrics.Value.Top) < 1;
            bool unchanged = metrics.HasValue &&
                _lastMeasuredScrollToTopValue.HasValue && _lastMeasuredRowHeight.HasValue && _lastMeasuredScrollBarMaximum.HasValue &&
                Math.Abs(metrics.Value.Top - _lastMeasuredScrollToTopValue.Value) < 0.5 &&
                Math.Abs(metrics.Value.Height - _lastMeasuredRowHeight.Value) < 0.5 &&
                Math.Abs((metrics.Value.ScrollBarMaximum ?? 0) - _lastMeasuredScrollBarMaximum.Value) < 0.5;
            _consecutiveStalledReadings = unchanged ? _consecutiveStalledReadings + 1 : 0;
            bool growing = metrics?.ScrollBarMaximum is { } curMax && _lastMeasuredScrollBarMaximum is { } prevMax &&
                prevMax > 0 && curMax > prevMax * 1.1;
            _consecutiveGrowingScrollBarMaximum = growing ? _consecutiveGrowingScrollBarMaximum + 1 : 0;
            _lastMeasuredScrollToTopValue = metrics?.Top;
            _lastMeasuredRowHeight = metrics?.Height;
            _lastMeasuredScrollBarMaximum = metrics?.ScrollBarMaximum ?? 0;
            bool stalled = _consecutiveStalledReadings >= 2;
            bool runaway = _consecutiveGrowingScrollBarMaximum >= RunawayConsecutiveGrowingReadings;

            if (atTop || stalled || runaway || ++_scrollToTopAttempts >= MaxScrollToTopAttempts)
            {
                SettleScrollToTopAttempt(atTop);
                return;
            }

            ScrollDetailsRowToTop();
            Dispatcher.UIThread.Post(AdvanceScrollToTopIfPending, DispatcherPriority.Background);
        }

        //DataGrid has a seizure sometimes if you try to scroll too far
        //so iteratively scroll one row at a time instead of jumping straight to our destination
        private void AdvanceIterativeWalk()
        {
            if (!_scrollToTopPending) return;

            (double Top, double Height, double? ScrollBarMaximum)? metrics = GetBuildDetailsRowMetrics();
            bool atTop = metrics.HasValue && Math.Abs(metrics.Value.Top) < 1;

            if (metrics.HasValue && _lastMeasuredScrollToTopValue.HasValue)
                _walkStepSizeEstimate = _lastMeasuredScrollToTopValue.Value - metrics.Value.Top;
            _lastMeasuredScrollToTopValue = metrics?.Top;

            bool wouldOvershoot = !atTop && metrics.HasValue && metrics.Value.Top > 0 &&
                _walkStepSizeEstimate.HasValue && metrics.Value.Top < _walkStepSizeEstimate.Value;

            IList? items = GamesGrid.ItemsSource as IList;
            bool haveNextRow = items != null && _walkStepIndex <= items.Count - 1;

            if (atTop || wouldOvershoot || !haveNextRow || ++_scrollToTopAttempts >= MaxScrollToTopAttempts)
            {
                // No row below to walk to yet at all (the target is the list's own last row, or close
                // enough that this is the very first check) means the walk never got a chance to move
                // anything — unlike an overshoot, nothing has been touched yet, so a single direct
                // ScrollIntoView on the target itself is still safe to try here, and is what the degenerate
                // "target is the last row" case relies on (there's no anchor to walk to below it at all).
                if (!atTop && !haveNextRow && _buildDetailsItem != null && _scrollToTopAttempts == 0)
                {
                    GamesGrid.UpdateLayout();
                    GamesGrid.ScrollIntoView(_buildDetailsItem, null);
                    metrics = GetBuildDetailsRowMetrics();
                    atTop = metrics.HasValue && Math.Abs(metrics.Value.Top) < 1;
                }

                SettleScrollToTopAttempt(atTop);
                return;
            }

            GamesGrid.UpdateLayout();
            GamesGrid.ScrollIntoView(items![_walkStepIndex]!, null);
            _walkStepIndex++;
            Dispatcher.UIThread.Post(AdvanceIterativeWalk, DispatcherPriority.Background);
        }

        private void SettleScrollToTopAttempt(bool atTop)
        {
            _scrollToTopPending = false;

            //failed to scroll the target row to top of screen.
            //fallback to unlocking scroll to avoid being trapped in a broken state.
            if (!atTop)
                SetMainTableScrollLocked(false);
        }

        private (double Top, double Height, double? ScrollBarMaximum)? GetBuildDetailsRowMetrics()
        {
            if (_buildDetailsItem == null) return (0, 0, 0);

            DataGridRow? detailsRow = GamesGrid.GetVisualDescendants().OfType<DataGridRow>()
                .FirstOrDefault(r => ReferenceEquals(r.DataContext, _buildDetailsItem));
            if (detailsRow == null) return null;

            DataGridRowsPresenter? rowsPresenter = GamesGrid.GetVisualDescendants().OfType<DataGridRowsPresenter>().FirstOrDefault();
            if (rowsPresenter == null) return null;

            double top = (detailsRow.TranslatePoint(new Point(0, 0), rowsPresenter) ?? default).Y;
            ScrollBar? verticalScrollBar = GamesGrid.GetVisualDescendants().OfType<ScrollBar>()
                .FirstOrDefault(s => s.Name == "PART_VerticalScrollbar");
            return (top, detailsRow.Bounds.Height, verticalScrollBar?.Maximum);
        }

        private const int ScrollAnchorRowsBelowTarget = 25; //should be more than one screen's worth of rows
        private bool _useSmallAnchor; //step one row at a time instead of jumping to destination

        private void ScrollDetailsRowToTop()
        {
            if (_buildDetailsItem == null) return;
            if (GamesGrid.ItemsSource is not IList items) return;

            int targetIndex = items.IndexOf(_buildDetailsItem);
            if (targetIndex < 0) return;

            GamesGrid.UpdateLayout(); //refresh internal state so ScrollIntoView works

            int anchorIndex = Math.Min(targetIndex + ScrollAnchorRowsBelowTarget, items.Count - 1);
            if (anchorIndex > targetIndex)
                GamesGrid.ScrollIntoView(items[anchorIndex]!, null);

            GamesGrid.ScrollIntoView(_buildDetailsItem, null);
        }

        // The row whose build-selection details are currently open, or null when none are. Held as the
        // row's view model rather than a DataGridRow because the containers are recycled.
        private object? _buildDetailsItem;

        private static bool IsNotesColumn(DataGridColumn column) => column.Header as string == "Notes";
        private static bool IsBuildColumn(DataGridColumn column) => column.Header as string == "Build";

        private void OnGamesGridCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
        {
            //show build details when you select the build column, hide it when you select a different column
            SetBuildDetailsItem(IsBuildColumn(e.Column) ? e.Row.DataContext : null);

            if (IsNotesColumn(e.Column))
            {
                //notes column is a template column with a textbox. Clicking into the textbox doesn't select the row automatically
                //manually set the row as selected so it gets highlighted as selected
                GamesGrid.SelectedItem = e.Row.DataContext;
                GamesGrid.CurrentColumn = e.Column;
                GamesGrid.BeginEdit();
            }
        }

        private void SetBuildDetailsItem(object? item)
        {
            if (ReferenceEquals(_buildDetailsItem, item))
                return;

            // Captured before this row's own AreDetailsVisible flip below, so it reflects the list's
            // scrollable range as it stood BEFORE this row grew at all — the only reliable way to tell
            // whether the whole list already filled the viewport unexpanded (see _useSmallAnchor).
            ScrollBar? scrollBarBeforeExpanding = GamesGrid.GetVisualDescendants().OfType<ScrollBar>()
                .FirstOrDefault(s => s.Name == "PART_VerticalScrollbar");
            _useSmallAnchor = item != null && (scrollBarBeforeExpanding == null || scrollBarBeforeExpanding.Maximum < 50);

            _buildDetailsItem = item;
            foreach (DataGridRow row in GamesGrid.GetVisualDescendants().OfType<DataGridRow>())
                ApplyBuildDetailsVisibility(row);

            SetMainTableScrollLocked(item != null);

            _scrollToTopPending = item != null;
            _scrollToTopAttempts = 0;
            _lastMeasuredScrollToTopValue = null;
            _lastMeasuredRowHeight = null;
            _lastMeasuredScrollBarMaximum = null;
            _consecutiveStalledReadings = 0;
            _consecutiveGrowingScrollBarMaximum = 0;
            _walkStepSizeEstimate = null;

            if (item == null) return;

            //dispatch scrolling so the build details section renders immediately.
            //scrolling has to be done iteratively and can take a few hundred milliseconds, which is a noticeable delay
            if (_useSmallAnchor)
            {
                _walkStepIndex = GamesGrid.ItemsSource is IList items ? items.IndexOf(item) + 1 : 0;
                Dispatcher.UIThread.Post(AdvanceIterativeWalk, DispatcherPriority.Background);
            }
            else
            {
                Dispatcher.UIThread.Post(AdvanceScrollToTopIfPending, DispatcherPriority.Background);
            }
        }

        private void ApplyBuildDetailsVisibility(DataGridRow row) =>
            row.AreDetailsVisible = _buildDetailsItem != null && ReferenceEquals(row.DataContext, _buildDetailsItem);

        // TabControl detaches an inactive tab's content from the visual tree rather than just hiding
        // it, so IsVisible never actually toggles on an existing instance when switching tabs.
        // OnAttachedToVisualTree is the correct lifecycle hook for "this page just became active again."
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            ViewModel.NotifyActivated();
        }

        private async Task OnSessionRequestedAsync()
        {
            if (!(TopLevel.GetTopLevel(this) is Window owner)) return;

            AccountPickerViewModel pickerVm = App.Services.GetRequiredService<AccountPickerViewModel>();
            AccountPickerResult? pickerResult = await new AccountPickerWindow(pickerVm).ShowDialog<AccountPickerResult?>(owner);

            if (pickerResult?.Outcome == AccountPickerOutcome.AccountSelected)
            {
                await ViewModel.SetActiveProfile(pickerResult.Profile);
            }
            else if (pickerResult?.Outcome == AccountPickerOutcome.LinkNew)
            {
                LinkAccountViewModel linkVm = App.Services.GetRequiredService<LinkAccountViewModel>();
                Sc2Profile? linkedProfile = await new LinkAccountWindow(linkVm).ShowDialog<Sc2Profile?>(owner);
                if (linkedProfile != null)
                    await ViewModel.SetActiveProfile(linkedProfile);
            }
        }

        private async Task OnDeleteGameConfirmationRequestedAsync(GameDataRowViewModel row)
        {
            if (!(TopLevel.GetTopLevel(this) is Window owner)) return;

            string message = $"Delete this recorded game ({row.MapName}, {row.PlayedAt})? This cannot be undone.";
            bool confirmed = await new ConfirmationWindow(message).ShowDialog<bool>(owner);

            if (confirmed)
                ViewModel.ConfirmDeleteGame(row);
        }

        private async Task OnImportReplayRequestedAsync()
        {
            if (!(TopLevel.GetTopLevel(this) is Window owner)) return;

            string? replayFolderPath = ViewModel.ReplayFolderPath;
            IStorageFolder? suggestedFolder = replayFolderPath != null
                ? await owner.StorageProvider.TryGetFolderFromPathAsync(replayFolderPath)
                : null;

            IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select a replay to import",
                AllowMultiple = false,
                SuggestedStartLocation = suggestedFolder,
                FileTypeFilter = [new FilePickerFileType("StarCraft II Replay") { Patterns = ["*.SC2Replay"] }],
            });

            if (files.Count == 0) return;

            string? path = files[0].TryGetLocalPath();
            if (path == null) return;

            // The folder watcher only ever looks directly inside the watched folder (no subfolders), so a
            // manual import is held to the same boundary rather than letting the user reach outside it.
            if (replayFolderPath == null || !IsDirectlyInFolder(path, replayFolderPath))
            {
                string rejectionMessage = replayFolderPath == null
                    ? "No replay folder is currently being watched."
                    : $"\"{Path.GetFileName(path)}\" is not inside the current replay folder:\n{replayFolderPath}";
                App.Services.GetRequiredService<ILogger>()
                    .LogWarning($"Rejected replay import: \"{path}\" is not directly inside the watched replay folder \"{replayFolderPath}\".");
                await new MessageWindow("Import Failed", rejectionMessage).ShowDialog(owner);
                return;
            }

            string? error = await ViewModel.ImportReplayFile(path);
            if (error != null)
                await new MessageWindow("Import Failed", error).ShowDialog(owner);
        }

        private async Task OnLaunchReplayFailedAsync(string message)
        {
            if (!(TopLevel.GetTopLevel(this) is Window owner)) return;

            await new MessageWindow("Launch Failed", message).ShowDialog(owner);
        }

        private static bool IsDirectlyInFolder(string filePath, string folderPath)
        {
            string? fileDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            string normalizedFolder = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fileDirectory != null &&
                string.Equals(fileDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), normalizedFolder, StringComparison.OrdinalIgnoreCase);
        }
    }
}
