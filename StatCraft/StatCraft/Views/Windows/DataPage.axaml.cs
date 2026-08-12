using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
            // GamesGrid in DataPage.axaml) — worst right as that row's container gets recycled for reuse.
            // UnloadingRow fires at that exact moment, which is what actually needs to be caught. Two
            // earlier attempts (polling the row's on-screen position after each scrollbar change, then
            // trying to track scroll direction to only react to a top-exit) both lagged behind the real
            // recycle point — UnloadingRow fires mid-layout-pass, before either of those signals had
            // caught up — and still let the bug through. No direction check is needed, though: a row only
            // unloads once it has genuinely left the realised range, not merely while it's still partly
            // visible (e.g. clipped at the bottom with its top still on-screen), so reacting unconditionally
            // here already only fires for a real "this row is gone" exit.
            GamesGrid.UnloadingRow += (_, e) =>
            {
                if (ReferenceEquals(e.Row.DataContext, _buildDetailsItem))
                    SetBuildDetailsItem(null);
            };
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
