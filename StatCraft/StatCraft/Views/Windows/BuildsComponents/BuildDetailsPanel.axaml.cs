using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using StatCraft.Models.GameData.Attributes;
using StatCraft.Services.BackgroundService;
using StatCraft.ViewModels.Windows;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;

namespace StatCraft.Views.Windows.BuildsComponents;

public partial class BuildDetailsPanel : UserControl
{
    private BuildsPageViewModel _vm => (BuildsPageViewModel)DataContext!;
    private static ILogger Logger => App.Services.GetRequiredService<ILogger>();
    public BuildDetailsPanel()
    {
        InitializeComponent();
    }

    private Control? VisibleDragIndicator { get; set; }

    private async void DetailDragPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!(sender is Control control))
        {
            Logger.LogWarning($"Build detail drag started from a {sender?.GetType().Name ?? "null"} rather than a Control; drag ignored.");
            return;
        }
        if (!(control.DataContext is AttributeDefinition definition))
        {
            Logger.LogWarning($"Build detail drag started from a control holding a {control.DataContext?.GetType().Name ?? "null"} rather than an AttributeDefinition; drag ignored.");
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
        HideDragIndicator();
        (int? sourceIndex, int? targetIndex, bool roundUp) = GetDragTarget(e);
        if (sourceIndex == null || targetIndex == null)
            return;
        int? trueTargetIndex = RoundDragTarget(sourceIndex.Value, targetIndex.Value, roundUp);
        if (trueTargetIndex == null) 
            return;
        if (trueTargetIndex > sourceIndex)
            trueTargetIndex--;

        _vm.ChangeDetailIndex(sourceIndex.Value, trueTargetIndex.Value);
        e.DragEffects = DragDropEffects.Move;
    }
    private void DetailDragOver(object? sender, DragEventArgs e)
    {
        (int? sourceIndex, int? targetIndex, bool roundUp) = GetDragTarget(e);
        if (sourceIndex == null || targetIndex == null)
        {
            e.DragEffects = DragDropEffects.None;
            HideDragIndicator();
            return;
        }
        int? trueTargetIndex = RoundDragTarget(sourceIndex.Value, targetIndex.Value, roundUp);
        if (trueTargetIndex == null)
        {
            HideDragIndicator();
            return;
        }

        Control? newIndicator = null;
        if (roundUp && trueTargetIndex == _vm.SelectedBuild!.Details.Count)
        {
            newIndicator = DragLineBottom;
        }
        else
        {
            Control container = DetailsList.ContainerFromIndex(trueTargetIndex.Value)!;
            Control stackPanel = container.FindDescendantOfType<StackPanel>()!;
            newIndicator = stackPanel.GetVisualChildren().FirstOrDefault(c => c.Name == "DragLineTop") as Control;
        }

        if (newIndicator == VisibleDragIndicator)
            return;

        HideDragIndicator();
        VisibleDragIndicator = newIndicator;

        if (VisibleDragIndicator != null)
            VisibleDragIndicator.Opacity = 100;
    }
    private void DetailDragLeave(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Source is ScrollContentPresenter container)
        {
            //avalonia is stupid and raises this when not actually leaving the control
            //so check mouse region because fml
            Point position = e.GetPosition(container);
            if (new Rect(container.Bounds.Size).Contains(position))
                return;

            HideDragIndicator();
        }
    }
    private void HideDragIndicator()
    {
        if (VisibleDragIndicator != null)
        {
            VisibleDragIndicator.Opacity = 0;
            VisibleDragIndicator = null;
        }
    }
    private DataFormat<AttributeDefinition> GetDragFormat()
    {
        return DataFormat.CreateInProcessFormat<AttributeDefinition>("DraggedRow");
    }

    private (int? source, int? target, bool belowCenter) GetDragTarget(DragEventArgs e)
    {
        DataFormat<AttributeDefinition> format = GetDragFormat();
        AttributeDefinition? sourceAttribute = e.DataTransfer.TryGetValue(format);
        if (sourceAttribute == null)
            return (null, null, false);

        ObservableCollection<AttributeDefinition> details = _vm.SelectedBuild!.Details;

        Control? targetRow = GetRowByY(e.GetPosition(DetailsList).Y, DetailsList, out bool belowCenter);
        if (targetRow == null)
        {
            Logger.LogWarning($"Build detail dropped at y={e.GetPosition(DetailsList).Y:0.#}, which matched no detail row; drop ignored.");
            return (null, null, false);
        }

        if (!(targetRow?.DataContext is AttributeDefinition targetAttribute))
        {
            Logger.LogWarning($"Build detail dropped on a row holding a {targetRow?.DataContext?.GetType().Name ?? "null"} rather than an AttributeDefinition; drop ignored.");
            return (null, null, false);
        }

        int sourceIndex = details.IndexOf(sourceAttribute);
        int targetIndex = details.IndexOf(targetAttribute);

        if (sourceIndex == -1 || targetIndex == -1)
        {
            Logger.LogWarning($"Build detail drag from \"{sourceAttribute.Name}\" to \"{targetAttribute.Name}\" referenced a detail the selected build no longer has (source index {sourceIndex}, target index {targetIndex}); drop ignored.");
            return (null, null, false);
        }

        return (sourceIndex, targetIndex, belowCenter);
    }
    private Control? GetRowByY(double yCoordinate, ItemsControl itemList, out bool belowCenter)
    {
        //NOTE: linear scan works here because there will not be many detail rows.
        //If this logic is needed somewhere else, it should be extracted to a common location and converted to binary search

        belowCenter = false;

        if (!(itemList.ItemsSource is ICollection source))
        {
            Logger.LogWarning($"Detail list is bound to a {itemList.ItemsSource?.GetType().Name ?? "null"}, which has no count to scan for the row under the pointer.");
            return null;
        }
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
    private int? RoundDragTarget(int sourceIndex, int targetIndex, bool roundUp)
    {
        if (roundUp)
            targetIndex++;
        if (sourceIndex == targetIndex || sourceIndex + 1 == targetIndex)
            return null;

        return targetIndex;
    }
}
