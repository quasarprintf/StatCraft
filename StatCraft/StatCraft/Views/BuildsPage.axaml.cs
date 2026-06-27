using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using StatCraft.Services;
using StatCraft.ViewModels;

namespace StatCraft.Views
{
    public partial class BuildsPage : UserControl
    {
        public BuildsPage()
        {
            InitializeComponent();
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StatCraft", "statcraft.db");
            var repository = new BuildRepository(dbPath);
            repository.Initialize();
            DataContext = new BuildsPageViewModel(repository);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && IsVisible && DataContext is BuildsPageViewModel vm)
                vm.SelectFirstBuild();
        }
    }
}
