using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using StatCraft.Models.GameData.Builds;
using StatCraft.Services.BackgroundService;
using StatCraft.ViewModels.Windows.DataComponents;

namespace StatCraft.Views
{
    public partial class BuildPathPicker : UserControl
    {
        private BuildSelectionSlotViewModel ViewModel => (BuildSelectionSlotViewModel)DataContext!;

        public BuildPathPicker()
        {
            InitializeComponent();

            // Manually bind on-click handlers for menu items, to allow selecting non-leaf items.
            if (PickerButton.Flyout is PopupFlyoutBase popupBase)
            {
                popupBase.Popup.Opened += (_, _) =>
                {
                    try
                    {
                        WireMenuItems((ItemsControl)popupBase.Popup.Child!);
                    }
                    catch (Exception ex)
                    {
                        //should never happen, log and fail loudly
                        ILogger logger = App.Services.GetRequiredService<ILogger>();
                        logger.LogError($"BuildPathPicker: failed to wire menu item selection handlers: {ex}", ViewModel);
                        throw;
                    }
                };
            }
        }

        private void WireMenuItems(ItemsControl itemsControl)
        {
            //bind items that haven't rendered yet
            itemsControl.ContainerPrepared -= OnContainerPrepared;
            itemsControl.ContainerPrepared += OnContainerPrepared;

            //bind items that already rendered
            foreach (MenuItem item in itemsControl.GetVisualDescendants().OfType<MenuItem>())
                WireMenuItem(item);
        }

        private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
        {
            if (e.Container is MenuItem item)
                WireMenuItem(item);
        }

        private void WireMenuItem(MenuItem item)
        {
            item.RemoveHandler(InputElement.PointerPressedEvent, OnMenuItemPointerPressed);
            item.AddHandler(InputElement.PointerPressedEvent, OnMenuItemPointerPressed, RoutingStrategies.Bubble);

            item.SubmenuOpened -= OnSubmenuOpened;
            item.SubmenuOpened += OnSubmenuOpened;
        }

        private void OnSubmenuOpened(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
                WireMenuItems(menuItem);
        }

        // Handling PointerPressed directly on the MenuItem — rather than Tapped, a gesture that requires a
        // full press+release cycle to complete — means we run before DefaultMenuInteractionHandler (which
        // is subscribed on an ancestor "Menu" element, so bubble order reaches us first regardless of
        // routing flags). Marking the event Handled here does two things: it stops the SAME event from
        // also bubbling into every ANCESTOR MenuItem's own handler (which it otherwise would, since every
        // MenuItem in the tree is wired the same way — without this, clicking a nested item would select
        // it, then immediately re-select every ancestor up to the root as the event kept bubbling, with the
        // root's selection winning last); and it suppresses the ancestor Menu's own native handling for
        // this click (toggling a submenu for an item with children, or auto-closing without selecting
        // anything for a leaf item, since MenuItem.Command is intentionally left unbound here).
        private void OnMenuItemPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not MenuItem { DataContext: BuildNode node } menuItem) return;
            if (!e.GetCurrentPoint(menuItem).Properties.IsLeftButtonPressed) return;

            e.Handled = true;
            ViewModel.SelectBuildCommand.Execute(node);
            PickerButton.Flyout?.Hide();
        }
    }
}
