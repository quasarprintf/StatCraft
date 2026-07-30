using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using StatCraft.Models.GameData.Builds;
using StatCraft.ViewModels;

namespace StatCraft.Views
{
    public partial class BuildPathPicker : UserControl
    {
        private GameDataRowViewModel ViewModel => (GameDataRowViewModel)DataContext!;

        public BuildPathPicker()
        {
            InitializeComponent();

            //manually bind on-click handlers for menu items, to allow selecting non-leaf items
            try
            {
                if (PickerButton.Flyout is PopupFlyoutBase popupBase)
                    popupBase.Popup.Opened += (_, _) => { WireMenuItems((ItemsControl)popupBase.Popup.Child!); };
            }
            catch (Exception ex)
            {
                //TODO: log and display error
            }
        }

        private void WireMenuItems(ItemsControl itemsControl)
        {
            //TODO: the order of these looks backwards to me. Possible race condition of an item rendering between the GetVisualDescendants call and the ContainerPrepared bindings

            //bind already-rendered items
            foreach (MenuItem item in itemsControl.GetVisualDescendants().OfType<MenuItem>())
                WireMenuItem(item);

            //bind items that haven't rendered yet
            itemsControl.ContainerPrepared -= OnContainerPrepared;
            itemsControl.ContainerPrepared += OnContainerPrepared;
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
