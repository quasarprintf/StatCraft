using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using StatCraft.ViewModels;

namespace StatCraft.Views
{
    public partial class SettingsPromptWindow : Window
    {
        private readonly SettingsPromptViewModel _vm;

        public SettingsPromptWindow(SettingsPromptViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            vm.Completed += Close;
        }

        private async void OnBrowseClick(object? sender, RoutedEventArgs e)
        {
            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Replay Folder",
                AllowMultiple = false,
            });

            if (folders.Count > 0)
            {
                string? path = folders[0].TryGetLocalPath();
                if (path != null)
                    _vm.BaseReplayFolderPath = path;
            }
        }
    }
}
