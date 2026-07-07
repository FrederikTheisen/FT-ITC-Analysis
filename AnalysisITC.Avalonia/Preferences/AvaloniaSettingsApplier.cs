using System;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Processing;

namespace AnalysisITC.Avalonia.Preferences;

internal static class AvaloniaSettingsApplier
{
    static bool registered;

    public static void Register()
    {
        if (registered) return;

        AppSettings.SettingsApplied += Apply;
        Apply(null, EventArgs.Empty);
        registered = true;
    }

    static void Apply(object? sender, EventArgs e)
    {
        SplineInterpolator.DefaultPointDensity = AppSettings.DefaultSplinePointDensity;
        SplineInterpolator.DefaultHandleMode = AppSettings.DefaultSplineHandleMode;
        SplineInterpolator.DefaultAllowPointTimeDragging = AppSettings.DefaultSplinePointTimeDragging;
    }
}
