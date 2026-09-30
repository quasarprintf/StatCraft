using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using StatCraft.Services.BackgroundService;
using StatCraft.ViewModels.Windows.Filters;
using System;
using System.Linq;

namespace StatCraft.Views.Components.Filters;

public partial class AddFilterMenu : UserControl
{
    // Every submenu in this menu opens to the left. The menu sits at the right edge of the filter bar, so
    // the first submenu already flips left for want of room, but each deeper one has room to open right —
    // back over the menu it came from.
    //
    // This is done here rather than with a Placement setter in the item theme because the Fluent MenuItem
    // template sets Placement on its own popup, and a value from a template outranks a plain style setter
    // (the VerticalOffset setter next to it only works because the template leaves that one alone).
    // Setting it on the popup itself, as each item is realised and before it can open, beats both.
    static AddFilterMenu()
    {
        MenuItem.LoadedEvent.AddClassHandler<MenuItem>((item, _) =>
        {
            // Class handlers see every MenuItem in the app, so only touch the ones showing this menu.
            if (item.DataContext is not IFilterMenuItemViewModel)
                return;

            Popup? popup = item.GetVisualDescendants().OfType<Popup>().FirstOrDefault();
            if (popup != null)
                popup.Placement = PlacementMode.LeftEdgeAlignedTop;
        });
    }

    public AddFilterMenu()
    {
        InitializeComponent();

        // Manually bind on-click handlers for menu items, to allow adding a filter from an entry that also
        // carries a submenu — a MenuItem with children opens that submenu on click instead of invoking its
        // Command, so a build that has builds nested under it could never be added. Same approach as
        // BuildPathPicker, which has the same problem with non-leaf builds.
        if (FilterButton.Flyout is PopupFlyoutBase popupBase)
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
                    logger.LogError($"AddFilterMenu: failed to wire menu item selection handlers: {ex}");
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

    // Handling PointerPressed directly on the MenuItem — rather than letting Command fire — means we run
    // before DefaultMenuInteractionHandler, which would otherwise just toggle the submenu. Marking the
    // event Handled stops the same click bubbling into every ANCESTOR MenuItem's handler, which would
    // otherwise add that build's filter too (every item here is wired the same way), and suppresses the
    // menu's own handling of the click. An entry with no filter of its own — the "Builds" root, a race,
    // "Game Attributes" — is left alone, so clicking it still opens its submenu as before.
    private void OnMenuItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: IFilterMenuItemViewModel { Filter: { } filter } } menuItem) return;
        if (!e.GetCurrentPoint(menuItem).Properties.IsLeftButtonPressed) return;

        e.Handled = true;
        filter.AddCommand.Execute(null);
        FilterButton.Flyout?.Hide();
    }
}
