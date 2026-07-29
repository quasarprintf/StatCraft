using Avalonia.Media;

namespace StatCraft.Styles
{
    // Shared brushes for use anywhere in the app, in place of the (often too muted) default Brushes.*
    // colors. Usable from C# directly, or from XAML via {x:Static vm:AppBrushes.VibrantGreen}.
    public static class Colors
    {
        public static readonly IBrush VibrantGreen = new SolidColorBrush(Color.Parse("#00CC1B"));
    }
}
