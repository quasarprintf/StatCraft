using Avalonia.Media;

namespace StatCraft.Styles
{
    public static class Colors
    {
        public static readonly IBrush ProtossGreen = new SolidColorBrush(Color.Parse("#00CC1B"));
        public static readonly IBrush TerranBlue = Brushes.Blue;
        public static readonly IBrush ZergRed = Brushes.Red;

        public static readonly IBrush AllyYellow = new SolidColorBrush(Color.Parse("#E0C82C"));
        public static readonly IBrush OpponentRed = Brushes.OrangeRed;
    }
}
