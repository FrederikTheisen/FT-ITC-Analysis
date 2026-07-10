using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;

namespace AnalysisITC.Avalonia.Styling;

internal static class AppTheme
{
    public const string WorkspaceBackground = nameof(WorkspaceBackground);
    public const string PanelBackground = nameof(PanelBackground);
    public const string PanelBorder = nameof(PanelBorder);
    public const string SectionBorder = nameof(SectionBorder);
    public const string PrimaryText = nameof(PrimaryText);
    public const string SecondaryText = nameof(SecondaryText);
    public const string MutedText = nameof(MutedText);
    public const string DisabledText = nameof(DisabledText);
    public const string TableHeaderBackground = nameof(TableHeaderBackground);
    public const string TableAlternateRow = nameof(TableAlternateRow);
    public const string SelectionBackground = nameof(SelectionBackground);
    public const string PreviewBackground = nameof(PreviewBackground);
    public const string StatusValid = nameof(StatusValid);
    public const string StatusWarning = nameof(StatusWarning);
    public const string StatusError = nameof(StatusError);

    public static IBrush Brush(string key)
    {
        var app = Application.Current;
        if (app != null)
        {
            if (app.TryGetResource(key, app.ActualThemeVariant, out var resource) && resource is IBrush brush)
                return brush;

            if (app.TryGetResource(key, ThemeVariant.Light, out resource) && resource is IBrush fallbackBrush)
                return fallbackBrush;
        }

        return Brushes.Transparent;
    }

    public static void Bind(AvaloniaObject target, AvaloniaProperty property, string key)
    {
        if (target is IResourceHost host)
        {
            target.Bind(property, host.GetResourceObservable(key), BindingPriority.Style);
            return;
        }

        target.SetValue(property, Brush(key));
    }
}
