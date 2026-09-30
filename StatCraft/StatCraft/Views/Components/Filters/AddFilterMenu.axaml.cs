using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using StatCraft.ViewModels.Windows.Filters;
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
            {
                popup.Placement = PlacementMode.LeftEdgeAlignedTop;
                popup.HorizontalOffset = 0;
            }
        });
    }

    public AddFilterMenu()
    {
        InitializeComponent();
    }
}
