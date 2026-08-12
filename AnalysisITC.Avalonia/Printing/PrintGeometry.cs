using System;

namespace AnalysisITC.Avalonia.Printing;

internal readonly record struct PrintSize(double Width, double Height)
{
    public bool IsValid => Width > 0 && Height > 0 && double.IsFinite(Width) && double.IsFinite(Height);
}

internal readonly record struct PrintRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

internal static class PrintGeometry
{
    public static PrintRect Fit(PrintSize source, PrintRect available)
    {
        if (!source.IsValid || available.Width <= 0 || available.Height <= 0)
            return new PrintRect(available.X, available.Y, 0, 0);

        var scale = Math.Min(available.Width / source.Width, available.Height / source.Height);
        var width = source.Width * scale;
        var height = source.Height * scale;
        return new PrintRect(
            available.X + (available.Width - width) / 2,
            available.Y + (available.Height - height) / 2,
            width,
            height);
    }
}
