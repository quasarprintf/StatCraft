using Avalonia;
using Avalonia.Win32;
using System;

namespace StatCraft
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                // Renders popups (ComboBox dropdowns, Flyouts, ToolTips, context menus) as an overlay
                // within the main window's own surface instead of as separate top-level OS windows.
                // Without this, screen-capture tools that target a specific window (OBS's Window Capture,
                // for one — confirmed against real usage) never see them, since they're a different HWND
                // regardless of capture method.
                .With(new Win32PlatformOptions { OverlayPopups = true })
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();
    }
}
