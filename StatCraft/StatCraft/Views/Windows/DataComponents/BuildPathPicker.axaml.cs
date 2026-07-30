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

            // A MenuFlyout's Popup is its own separate visual root — pointer events raised inside it
            // never bubble up to PickerButton (the flyout's placement target), so a handler attached there
            // (even with handledEventsToo) never sees clicks on the menu at all. The only reliable way to
            // catch every click on every MenuItem, at every nesting depth (submenus are further separate
            // Popups of their own), is to attach directly to each MenuItem instance once it exists.
            if (PickerButton.Flyout is PopupFlyoutBase { Popup: Popup popup })
                popup.Opened += (_, _) => { if (popup.Child is ItemsControl root) WireMenuItems(root); };
        }

        // Wires every currently-realized MenuItem under this ItemsControl (whatever GetVisualDescendants
        // finds right now) AND subscribes ContainerPrepared to catch any that get realized afterward —
        // needed because exactly when a MenuItem's children actually become walkable relative to
        // SubmenuOpened/Popup.Opened firing isn't reliable (that mismatch is what left nested items
        // unwired before: every click on one bubbled, unhandled, all the way up to the one MenuItem that
        // WAS wired in time — the root — which is why every selection landed on the root instead of the
        // clicked node). Between the immediate walk and the event subscription, every item is covered
        // regardless of which one actually catches it first.
        private void WireMenuItems(ItemsControl itemsControl)
        {
            foreach (MenuItem item in itemsControl.GetVisualDescendants().OfType<MenuItem>())
                WireMenuItem(item);

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
