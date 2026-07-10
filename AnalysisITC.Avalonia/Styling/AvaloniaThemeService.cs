using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AnalysisITC.Avalonia.Styling;

internal static class AvaloniaThemeService
{
    static bool registered;

    public static void Register()
    {
        if (registered) return;
        registered = true;

        var app = Application.Current;
        if (app == null) return;

        app.RequestedThemeVariant = ThemeVariant.Default;
        app.ActualThemeVariantChanged += (_, _) => ApplyTheme(app.ActualThemeVariant);
        ApplyTheme(app.ActualThemeVariant);
    }

    static void ApplyTheme(ThemeVariant theme)
    {
        if (theme == ThemeVariant.Dark)
            AvaloniaGraphSettings.UseDarkTheme();
        else
            AvaloniaGraphSettings.UseLightTheme();

        Dispatcher.UIThread.Post(InvalidateOpenWindows);
    }

    static void InvalidateOpenWindows()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return;

        foreach (var window in desktop.Windows)
            InvalidateVisualTree(window);
    }

    static void InvalidateVisualTree(Control control)
    {
        control.InvalidateVisual();

        foreach (var child in control.GetVisualChildren().OfType<Control>())
            InvalidateVisualTree(child);
    }
}
