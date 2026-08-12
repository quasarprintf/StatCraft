using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
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
            // GamesGrid in DataPage.axaml). Three earlier attempts at closing the details before that can
            // bite: (1) polling the row's position after each scrollbar Value change — too coarse, the
            // scrollbar doesn't fire on every scroll increment (especially with smooth/inertial scrolling),
            // so the row could travel well past the trigger point between checks; (2) reacting to
            // UnloadingRow (the row container actually being recycled) — DataGridRowsPresenter keeps an
            // off-screen realised buffer that isn't sized for a row this tall, so recycling lagged well
            // behind the row visually leaving the viewport; (3) DataGridRow.EffectiveViewportChanged never
            // fires at all — DataGridRowsPresenter is a fully custom virtualizer, not built on the
            // ScrollViewer/ScrollContentPresenter infrastructure that event depends on. LayoutUpdated fires
            // after every layout pass regardless of what caused it, so checking the details row's actual
            // position there catches the exact frame its top crosses the viewport edge, confirmed against
            // a headless render tracking the row's position tick-by-tick during a scroll — measured against
            // the rows area rather than the DataGrid control itself, so it also collapses as soon as the
            // row starts disappearing behind the column header, not only once it's fully gone.
            GamesGrid.LayoutUpdated += (_, _) => CollapseBuildDetailsIfScrolledOffTop();
        }

        private void CollapseBuildDetailsIfScrolledOffTop()
        {
            if (_buildDetailsItem == null) return;

            DataGridRow? detailsRow = GamesGrid.GetVisualDescendants().OfType<DataGridRow>()
                .FirstOrDefault(r => ReferenceEquals(r.DataContext, _buildDetailsItem));

            // Recycled out of the realised range entirely — definitely gone, not just partly clipped.
            if (detailsRow == null)
            {
                SetBuildDetailsItem(null);
                return;
            }

            // Measured against the rows area specifically, not the DataGrid control as a whole — the
            // column header sits above it and isn't part of the scrollable viewport, so a row can already
            // be hidden behind the header while still reading as on-screen relative to the grid itself.
            DataGridRowsPresenter? rowsPresenter = GamesGrid.GetVisualDescendants().OfType<DataGridRowsPresenter>().FirstOrDefault();
            if (rowsPresenter == null) return;

            Point topLeft = detailsRow.TranslatePoint(new Point(0, 0), rowsPresenter) ?? default;
            if (topLeft.Y < 0)
                SetBuildDetailsItem(null);
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

            _buildDetailsItem = item;
            foreach (DataGridRow row in GamesGrid.GetVisualDescendants().OfType<DataGridRow>())
                ApplyBuildDetailsVisibility(row);
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
