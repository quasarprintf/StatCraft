using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
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
            DataContext = vm;

            // A single click on the Notes cell of a not-yet-selected row only selects the row by
            // default — entering edit mode (and focusing the editor) normally needs a second click on
            // an already-current cell. Force both to happen on the very first click instead.
            GamesGrid.CellPointerPressed += OnGamesGridCellPointerPressed;
        }

        private static bool IsNotesColumn(DataGridColumn column) => column.Header as string == "Notes";

        private void OnGamesGridCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
        {
            if (!IsNotesColumn(e.Column)) return;

            GamesGrid.SelectedItem = e.Row.DataContext;
            GamesGrid.CurrentColumn = e.Column;
            GamesGrid.BeginEdit();
        }

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

        private static bool IsDirectlyInFolder(string filePath, string folderPath)
        {
            string? fileDirectory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            string normalizedFolder = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fileDirectory != null &&
                string.Equals(fileDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), normalizedFolder, StringComparison.OrdinalIgnoreCase);
        }
    }
}
