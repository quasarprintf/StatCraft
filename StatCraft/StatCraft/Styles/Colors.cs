using Avalonia.Media;

namespace StatCraft.Styles
{
    public static class Colors
    {
        public static readonly IBrush ProtossGreen = new SolidColorBrush(Color.Parse("#00CC1B"));
        public static readonly IBrush TerranBlue = Brushes.Blue;
        public static readonly IBrush ZergRed = Brushes.Red;

        public static readonly IBrush WinGreen = Brushes.ForestGreen;
        public static readonly IBrush LossRed = Brushes.DarkRed;
        public static readonly IBrush DrawBlue = Brushes.DarkBlue;

        // For the Data tab's "Use Team Colors" setting — colors a tab by side rather than the player's
        // actual in-game color.
        public static readonly IBrush AllyColor = Brushes.DodgerBlue;
        public static readonly IBrush OpponentColor = Brushes.OrangeRed;

        // For a player's actual in-game color, packed 0xAARRGGBB — see GamePlayer.ColorArgb.
        public static IBrush FromArgb(int argb) => new SolidColorBrush(Color.FromUInt32(unchecked((uint)argb)));
    }
}
