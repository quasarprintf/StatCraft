using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using StatCraft.ViewModels;

namespace StatCraft.Views
{
    public partial class SettingsPromptWindow : Window
    {
        private readonly SettingsPromptViewModel? _vm;

        // Parameterless constructor required by the Avalonia XAML designer to create a design-time instance.
        public SettingsPromptWindow()
        {
            InitializeComponent();
        }

        public SettingsPromptWindow(SettingsPromptViewModel vm) : this()
        {
            _vm = vm;
            DataContext = vm;
            vm.Completed += Close;
        }

        private async void OnBrowseClick(object? sender, RoutedEventArgs e)
        {
            if (_vm == null) return;

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
