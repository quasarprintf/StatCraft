using Avalonia.Media;

namespace StatCraft.Styles
{
    // A single character rendered with its own foreground color, e.g. one letter of a race/matchup
    // indicator. Rendered by the ColoredTextBlock UserControl.
    public record ColoredCharacter(string Text, IBrush Color);
}
