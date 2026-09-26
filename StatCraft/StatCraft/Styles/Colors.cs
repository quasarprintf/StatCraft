using Avalonia.Media;

namespace StatCraft.Styles;

public static class Colors
{
    public static readonly IBrush ProtossGreen = new SolidColorBrush(Color.Parse("#00CC1B"));
    public static readonly IBrush TerranBlue = Brushes.Blue;
    public static readonly IBrush ZergRed = Brushes.Red;

    public static readonly IBrush WinGreen = Brushes.ForestGreen;
    public static readonly IBrush LossRed = Brushes.DarkRed;
    public static readonly IBrush DrawBlue = Brushes.DarkBlue;

    // Backs an MMR that OpponentMmrEstimator inferred rather than the replay reporting it, so an
    // estimate is visibly not real data (see the opponent MMR field in DataPage.axaml).
    public static readonly IBrush EstimatedMmrOrange = Brushes.Orange;

    public static readonly IBrush AllyYellow = new SolidColorBrush(Color.Parse("#E0C82C"));
    public static readonly IBrush OpponentRed = Brushes.OrangeRed;

    // For a player's actual in-game color, packed 0xAARRGGBB — see GamePlayer.ColorArgb.
    public static IBrush FromArgb(int argb) => new SolidColorBrush(Color.FromUInt32(unchecked((uint)argb)));
}
