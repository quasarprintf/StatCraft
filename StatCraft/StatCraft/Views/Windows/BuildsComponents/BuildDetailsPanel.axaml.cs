using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Models.GameData.Builds;
using StatCraft.ViewModels.Windows;
using System.Collections;
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
            ObservableCollection<AttributeDefinition> details = _vm.SelectedBuild!.Details;

            Control? targetRow = GetDragTarget(e.GetPosition(DetailsList).Y, DetailsList, out bool roundUp);
            if (targetRow == null)
                return; //TODO: log this, shouldn't happen

            if (!(targetRow?.DataContext is AttributeDefinition targetAttribute))
                return; //TODO: log this, shouldn't happen

            int sourceIndex = details.IndexOf(sourceAttribute);
            int targetIndex = details.IndexOf(targetAttribute);

            if (sourceIndex == -1 || targetIndex == -1)
                return; //TODO: log this, shouldn't happen

            int? trueTargetIndex = GetTrueDragTargetIndex(sourceIndex, targetIndex, roundUp);
            if (trueTargetIndex == null)
                return;

            _vm.ChangeDetailIndex(sourceIndex, trueTargetIndex.Value);
            e.DragEffects = DragDropEffects.Move;
        }
    }
    private void DetailDragOver(object? sender, DragEventArgs e)
    {
        DataFormat<AttributeDefinition> format = GetDragFormat();
        if (e.DataTransfer.Contains(format))
        {
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }
    private DataFormat<AttributeDefinition> GetDragFormat()
    {
        return DataFormat.CreateInProcessFormat<AttributeDefinition>("DraggedRow");
    }

    private Control? GetDragTarget(double yCoordinate, ItemsControl itemList, out bool belowCenter)
    {
        //NOTE: linear scan works here because there will not be many detail rows.
        //If this logic is needed somewhere else, it should be extracted to a common location and converted to binary search

        belowCenter = false;

        if (!(itemList.ItemsSource is ICollection source))
            return null; //TODO: log this, shouldn't happen
        int itemsCount = source.Count;

        Control? foundControl = null;
        for (int i = 0; i < itemsCount; i++)
        {
            Control container = itemList.ContainerFromIndex(i)!;
            double rowTop = container.TranslatePoint(new Point(0,0), itemList)!.Value.Y;
            if (yCoordinate < rowTop)
                break;
            foundControl = container;
            belowCenter = yCoordinate > rowTop + (container.Bounds.Height / 2);
        }

        return foundControl;
    }
    private int? GetTrueDragTargetIndex(int sourceIndex, int targetIndex, bool roundUp)
    {
        if (roundUp)
            targetIndex++;
        if (sourceIndex < targetIndex)
            targetIndex--;
        if (sourceIndex == targetIndex)
            return null;

        return targetIndex;
    }
}
