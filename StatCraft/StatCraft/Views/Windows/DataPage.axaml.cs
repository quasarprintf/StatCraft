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

            // A single click on the Notes cell of a not-yet-selected row only selects the row by
            // default — entering edit mode (and focusing the editor) normally needs a second click on
            // an already-current cell. Force both to happen on the very first click instead.
            GamesGrid.CellPointerPressed += OnGamesGridCellPointerPressed;

            // Rows are recycled as the grid scrolls, so a newly realised row has to be told whether it
            // is the one currently showing its build details.
            GamesGrid.LoadingRow += (_, e) => ApplyBuildDetailsVisibility(e.Row);

            // Avalonia.Controls.DataGrid's virtualization/scroll-offset handling breaks down on variable
            // row height, which an expanded details row is the only source of (see the comment above
            // GamesGrid in DataPage.axaml) — and not just the bookkeeping: how far a single wheel notch
            // scrolls gets corrupted too, unpredictably (observed jumps from small up to 400+px on one
            // tick). Several reactive and pre-emptive "close before it goes too far" approaches were tried
            // and all eventually lost to a jump bigger than whatever margin was picked, since the jump
            // size itself is the thing that's broken and out of this code's control. Instead: whenever
            // details open, the owning row is scrolled to the very top of the viewport and the main
            // table's own scrolling (wheel + scrollbar) is locked entirely until details close again. With
            // the row pinned at the top and the table unable to scroll at all, the buggy virtualization
            // math never gets a scroll to act on in the first place — the details' own content can still
            // scroll internally (see GamesGrid.LoadingRowDetails below).
            // DataGrid re-enables its own scrollbar on layout passes whenever there's scrollable content,
            // fighting a one-time IsEnabled=false — reasserting it here each pass while locked is what
            // actually keeps it disabled. Skipped while a scroll-to-top is actively in progress: reasserting
            // IsEnabled=false on every pass appears to reset the scrollbar's own position back to 0 as a
            // side effect (observed via diagnostic logging: sbValue stayed pinned at 0 while sbMax kept
            // growing across attempts, meaning ScrollIntoView's own repositioning was being clobbered before
            // it could ever take hold) — so it's held off until the row has actually settled into place.
            GamesGrid.LayoutUpdated += (_, _) =>
            {
                if (_mainTableScrollLocked && !_scrollToTopPending)
                    SetMainTableScrollLocked(true);
            };
            GamesGrid.AddHandler(InputElement.PointerWheelChangedEvent, OnGamesGridWheelChangedWhileLocked, RoutingStrategies.Tunnel);

            // Lets the details' own ScrollViewer handle its own scrolling internally, rather than the
            // wheel input reaching GamesGrid's own handling above at all (which would otherwise swallow
            // it outright while locked). ScrollViewer.IsScrollChainingEnabled does not achieve this on its
            // own — confirmed via a headless probe that DataGrid still receives and acts on the same wheel
            // input regardless of that flag, because the details section sits outside the normal cells-
            // presenter hit-test region a ScrollViewer would otherwise chain through. Handling the event
            // here at the Tunnel stage — on the details element itself, further down the tunnel than
            // GamesGrid's own handler above — and unconditionally marking it Handled does work.
            GamesGrid.LoadingRowDetails += (_, e) =>
            {
                if (e.DetailsElement is ScrollViewer detailsScrollViewer)
                {
                    detailsScrollViewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnRowDetailsScrollViewerWheelChanged);
                    detailsScrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, OnRowDetailsScrollViewerWheelChanged,
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

            // Only swallow wheel input over the plain rows — over the details area itself, this must fall
            // through so the details' own ScrollViewer (its handler is registered further down the tunnel,
            // on the details element itself) still gets a chance to scroll normally.
            bool overCells = (e.Source as Visual)?.GetVisualAncestors().OfType<DataGridCell>().Any() == true;
            if (overCells)
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

        // ScrollIntoView runs synchronously, right when details are opened — but AreDetailsVisible=true
        // (set just before it) only *invalidates* layout; the row doesn't actually grow taller until the
        // next measure/arrange pass, which happens after this call returns. So a one-shot call aligns the
        // row correctly for its *old*, un-expanded height, and the subsequent expansion can knock it back
        // out of place — worst for a row near the end of the list, where there isn't much content below it
        // to absorb the newly-added height, so the panel pulls the scroll position back down to avoid
        // showing empty space past the last item, leaving the row not at the top after all. Retrying,
        // re-checking the row's actual position each time rather than trusting the first attempt, is what
        // actually lands it and keeps it at the top.
        //
        // Each retry step is scheduled via Dispatcher.UIThread.Post at Background priority rather than
        // driven off GamesGrid.LayoutUpdated. LayoutUpdated is not a reliable proxy for "the previous
        // ScrollIntoView call's effect has actually been rendered" — confirmed by breakpoint testing, where
        // it fired several times back-to-back before the details section had rendered at all. Comparing
        // position readings taken across those firings was unreliable: two readings could look "unchanged"
        // simply because no real settling had happened between them yet, not because the scroll genuinely
        // had nowhere further to go — causing the row to intermittently give up and stay unscrolled.
        // DispatcherPriority.Background only runs once nothing higher-priority is left pending, including
        // the actual render pass, so every reading here reflects a real rendered frame.
        //
        // A row near the end of the list can never reach top=0 at all (there's a hard floor: the list
        // can't scroll past its own last item), so its position stabilizes at some nonzero residual and
        // then never changes again. Against real row content each step costs real layout/render time, so
        // blindly running all MaxScrollToTopAttempts in that case turned a single click into a multi-second
        // stall (measured ~1.6s in the app) — stopping once two consecutive (now reliably-spaced) readings
        // agree avoids paying for further steps that can't help.
        //
        // "Agree" has to cover the row's height too, not just its on-screen position: when the whole table
        // already fits within the viewport before any row's details are open (a short, filtered game
        // history, say 18 rows), there is no scrollable range at all until the details row has actually
        // grown — ScrollIntoView correctly does nothing on those early attempts, since as far as it's
        // concerned nothing is out of view yet. That looked identical to a genuine stall (position
        // unchanged twice in a row) and made the loop give up before the row had even finished growing,
        // i.e. before scrolling could possibly do anything. Only treating it as stalled once the row's
        // height has also stopped changing means growth alone is enough to keep the loop going.
        //
        // The scrollbar's own Maximum has to agree too: observed via diagnostic logging, a case existed
        // where the row's own position and height had both already settled, yet PART_VerticalScrollbar's
        // Maximum was STILL increasing attempt over attempt (DataGrid's own notion of the list's scrollable
        // extent hadn't finished catching up with the newly-grown row) — meaning ScrollIntoView still had
        // more information to work with on a later attempt even though nothing we were watching had budged
        // yet. Requiring Maximum to have stopped changing too, before calling it a stall, gives that catch-up
        // process room to finish instead of giving up mid-way through it.
        //
        // That same Maximum can also blow up instead of settling: on a short list with one row expanded far
        // taller than the rest, diagnostic logging caught it growing without bound, attempt after attempt —
        // 371, then 784, then 1243, accelerating all the way to over 24000 by the 20th attempt on a list
        // whose real extent was a few thousand pixels at most — DataGrid's own bookkeeping compounding on
        // itself from repeatedly re-measuring the one wildly-outsized row, worse each time. This is not
        // "still catching up" (which can also grow substantially, just not endlessly) — it never stops.
        //
        // Distinguishing the two isn't about how big the growth gets, but how *consistent* it is: real
        // catch-up growth is uneven — it stalls, jumps, and often drops back down as DataGrid's estimate
        // corrects itself — while the runaway pattern grows on every single attempt without exception. An
        // earlier version compared against a fixed multiple of the first-observed value, which caught the
        // runaway case but also misfired on legitimate multi-attempt convergence that happened to grow a
        // lot along the way, breaking previously-working rows. Requiring several CONSECUTIVE growing
        // readings — not just cumulative growth from the start — means any single non-growing reading
        // (a stall, a correction, anything but growth) resets it, so only sustained, uninterrupted growth
        // ever trips it.
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
                _scrollToTopPending = false;

                // Giving up without the row actually at the top means the details panel — up to 600px tall
                // — is left extending past the bottom of the window, with the main table still locked. The
                // lock only earns its keep by getting the row genuinely out of DataGrid's buggy variable-
                // height-scroll territory; if it hasn't done that, keeping it locked leaves the user with no
                // way to reach the rest of the panel at all — the details' own internal ScrollViewer can't
                // help either, since its bottom edge is off-screen regardless of its own scroll position.
                // Unlocking accepts the pre-existing risk of DataGrid's scroll corruption over leaving the
                // content completely unreachable.
                if (!atTop)
                    SetMainTableScrollLocked(false);

                return;
            }

            ScrollDetailsRowToTop();
            Dispatcher.UIThread.Post(AdvanceScrollToTopIfPending, DispatcherPriority.Background);
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

        // Comfortably more rows than the details panel could ever push past (it's capped at 600px, well
        // under 25 rows' worth), so this is still guaranteed to land the anchor below the whole expanded
        // row — it just doesn't need to be the literal last row in the list to do that. Only used when
        // the list DIDN'T already fit the viewport before this row expanded — see _useSmallAnchor.
        private const int ScrollAnchorRowsBelowTarget = 25;

        // When the whole list already fits the viewport before any row expands (a short, filtered game
        // history), a distant anchor is what corrupts DataGrid's own extent bookkeeping — confirmed via a
        // headless probe: forcing the anchor down to the immediately-next row instead of a distant one
        // turned an unbounded runaway (scrollbar Maximum climbing into the tens of thousands, never
        // settling) into either a clean top=0 or a small, stable residual — both bounded, neither
        // corrupting. The same probe confirmed a distant anchor is still needed for a genuinely-long list
        // (a list already too tall for the viewport before this row's own expansion): forcing distance=1
        // there left a legitimately-reachable row stuck partway, since a single adjacent row often isn't
        // enough distance to force DataGrid to treat the target as approached "from below". So this is
        // decided once, at the moment details are opened, from whether the list already filled the
        // viewport BEFORE this specific row grew — see the capture in SetBuildDetailsItem.
        private bool _useSmallAnchor;

        // DataGrid.ScrollIntoView aligns minimally — bringing an item into view from *above* the current
        // viewport aligns it to the bottom edge, and from *below* aligns it to the top edge. Scrolling an
        // item comfortably below the target into view first, then scrolling the target itself into view,
        // means it's always approached from below, landing it at the top. This uses DataGrid's own real
        // scroll mechanism; poking PART_VerticalScrollbar's Value directly does not actually reposition
        // content at all (confirmed via a headless probe — only genuine scroll input does that), which is
        // why an earlier version of this that tried exactly that never worked. An even earlier version
        // used the literal last row in the list as that anchor rather than a nearby one — correct, but a
        // visibly multi-second delay opening details on anything not already near the end of a long game
        // history, since ScrollIntoView-ing across a huge distance (and doing it again on every retry
        // attempt below) is itself expensive, unlike a small bounded jump that costs the same regardless
        // of how long the list has grown.
        private void ScrollDetailsRowToTop()
        {
            if (_buildDetailsItem == null) return;
            if (GamesGrid.ItemsSource is not IList items) return;

            int targetIndex = items.IndexOf(_buildDetailsItem);
            if (targetIndex < 0) return;

            // Forces a synchronous layout pass before asking DataGrid to scroll: ScrollIntoView's own
            // notion of the list's scrollable extent appeared to be lagging behind the target row's
            // already-rendered height (observed via diagnostic logging — the row's own measured height had
            // fully settled while ScrollIntoView still computed no scroll was needed), so this rules out
            // that particular staleness before each attempt.
            GamesGrid.UpdateLayout();

            int anchorDistance = _useSmallAnchor ? 1 : ScrollAnchorRowsBelowTarget;
            int anchorIndex = Math.Min(targetIndex + anchorDistance, items.Count - 1);
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
            // Build details open only from the Build cell, and close again as soon as any other cell is
            // clicked. Clicking inside the details themselves never reaches here — those controls sit
            // outside the cells presenter — so interacting with them leaves the panel open.
            SetBuildDetailsItem(IsBuildColumn(e.Column) ? e.Row.DataContext : null);

            if (!IsNotesColumn(e.Column)) return;

            GamesGrid.SelectedItem = e.Row.DataContext;
            GamesGrid.CurrentColumn = e.Column;
            GamesGrid.BeginEdit();
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

            // Deliberately not calling ScrollDetailsRowToTop() synchronously here: this method runs inside
            // the click handler, before Avalonia has had any chance to render the row's now-taller,
            // details-visible state at all. Kicking off the (non-trivial, real-layout-cost) scroll work
            // here would hold up that very first render, so the first step is posted at Background priority
            // instead — see AdvanceScrollToTopIfPending below for why that specific priority matters — which
            // lets the details appear first, with the scroll-into-place following a moment later.
            if (item != null)
                Dispatcher.UIThread.Post(AdvanceScrollToTopIfPending, DispatcherPriority.Background);
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
