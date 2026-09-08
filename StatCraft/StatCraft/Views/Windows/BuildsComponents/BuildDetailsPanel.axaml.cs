using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using StatCraft.Models.GameData.Attributes;
using Avalonia.VisualTree;
using StatCraft.Models.GameData.Builds;
using StatCraft.ViewModels.Windows;
using System.Collections.ObjectModel;

namespace StatCraft.Views.Windows.BuildsComponents;

public partial class BuildDetailsPanel : UserControl
{
    private BuildsPageViewModel _vm => (BuildsPageViewModel)DataContext!;
    public BuildDetailsPanel()
    {
        InitializeComponent();
    }

    private async void DetailDragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!(sender is Control control))
        {
            //TODO: log unexpected case
            return;
        }
        if (!(control.DataContext is AttributeDefinition definition))
        {
            //TODO: log unexpected case
            return;
        }
        DataFormat<AttributeDefinition> format = GetDragFormat();
        DataTransferItem item = new DataTransferItem();
        item.Set(format, definition);
        using (DataTransfer transfer = new DataTransfer())
        {
            transfer.Add(item);
            await DragDrop.DoDragDropAsync(e, transfer, DragDropEffects.Move);
        }
    }
    private void DetailDragDrop(object? sender, DragEventArgs e)
    {
        DataFormat<AttributeDefinition> format = GetDragFormat();
        AttributeDefinition? sourceAttribute = e.DataTransfer.TryGetValue(format);
        if (sourceAttribute != null)
        {
            var visualTarget = e.Source as Visual;
            var targetPanel = visualTarget?.FindAncestorOfType<StackPanel>(true);
            if (targetPanel != null && targetPanel.DataContext is AttributeDefinition targetAttribute) 
            {
                ObservableCollection<AttributeDefinition> details = _vm.SelectedBuild!.Details;
                int sourceIndex = details.IndexOf(sourceAttribute);
                int targetIndex = details.IndexOf(targetAttribute);
                if (sourceIndex >= 0 && targetIndex >= 0 && sourceIndex != targetIndex)
                {
                    _vm.ChangeDetailIndex(sourceIndex, targetIndex);
                    e.DragEffects = DragDropEffects.Move;
                }
            }
        }
    }
    private DataFormat<AttributeDefinition> GetDragFormat()
    {
        return DataFormat.CreateInProcessFormat<AttributeDefinition>("DraggedRow");
    }
}
