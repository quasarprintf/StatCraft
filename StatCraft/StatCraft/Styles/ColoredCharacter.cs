using Avalonia.Media;

namespace StatCraft.Styles
{
    // A run of text rendered with its own foreground color, e.g. one letter of a race/matchup
    // indicator. Rendered by the ColoredTextBlock UserControl.
    //
    // A null Color means "leave it alone": ColoredTextBlock falls back to its own inherited Foreground,
    // so the run picks up whatever colour the surrounding context uses. That matters because the app
    // follows the system light/dark theme, so there is no fixed brush that reads as "normal text".
    public record ColoredCharacter(string Text, IBrush? Color = null);
}
