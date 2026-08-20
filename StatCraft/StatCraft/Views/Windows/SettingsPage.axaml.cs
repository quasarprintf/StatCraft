using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using StatCraft.ViewModels.Windows;

namespace StatCraft.Views
{
    public partial class SettingsPage : UserControl
    {
        private SettingsPageViewModel ViewModel => (SettingsPageViewModel)DataContext!;

        public SettingsPage()
        {
            InitializeComponent();
            DataContext = App.Services.GetRequiredService<SettingsPageViewModel>();
        }

        private async void OnBrowseClick(object? sender, RoutedEventArgs e)
        {
            if (!(TopLevel.GetTopLevel(this) is Window owner)) return;

            IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Replay Folder",
                AllowMultiple = false,
            });

            if (folders.Count > 0)
            {
                string? path = folders[0].TryGetLocalPath();
                if (path != null)
                    ViewModel.BaseReplayFolderPath = path;
            }
        }
    }
}
