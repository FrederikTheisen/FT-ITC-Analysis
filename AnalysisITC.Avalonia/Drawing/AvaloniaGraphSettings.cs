using Avalonia.Media;

public static class AvaloniaGraphSettings
{
    public static AvaloniaGraphTheme Current { get; private set; } = AvaloniaGraphTheme.Light;

    public static double GraphMarginLeftMinimum => 30;
    public static double GraphMarginLeftTickBuffer => 20;
    public static double GraphMarginBottom => 58;
    public static double GraphMarginTop => 38;
    public static double GraphMarginRight => 22;

    public static double TickLength => 5;
    public static double TickLabelOffset => 9;
    public static double YTickLabelYOffset => 7;
    public static double XAxisTitleOffset => 35;
    public static double AxisTitleOffset => 24;
    public static double YLabelFallbackWidth => 44;

    public static double TickLabelFontSize => 11;
    public static double AxisTitleFontSize => 12;
    public static double EmptyTitleFontSize => 14;
    public static double EmptyBodyFontSize => 12;
    public static double HoverFontSize => 11;
    public static double InjectionLabelFontSize => 9;
    public static double PointLabelFontSize => 10;

    public static double EmptyStateXOffset => 18;
    public static double EmptyStateTitleYOffset => 18;
    public static double EmptyStateBodyYOffset => 43;

    public static double HoverPaddingX => 9;
    public static double HoverPaddingY => 7;
    public static double HoverLineGap => 3;
    public static double HoverAnchorXOffset => 12;
    public static double HoverAnchorYOffset => 10;
    public static double HoverPlotInset => 8;
    public static double HoverCornerRadius => 4;
    public static double HoverMarkerRadius => 4;

    public static double DefaultXPaddingFraction => 0.015;
    public static double DefaultYPaddingFraction => 0.08;
    public static double AnalysisXPaddingFraction => 0.06;
    public static double AnalysisYPaddingFraction => 0.12;
    public static double ThermogramXTickDivisor => 135;
    public static double ThermogramYTickDivisor => 85;
    public static double AnalysisXTickDivisor => 145;
    public static double AnalysisYTickDivisor => 105;

    public static double FrameStroke => 1;
    public static double AxisStroke => 1;
    public static double MajorGridStroke => 1;
    public static double MinorGridStroke => 1;
    public static double HoverStroke => 1;
    public static double DataStroke => 1.2;
    public static double OverviewDataStroke => 1.25;
    public static double InjectionStroke => 0.8;
    public static double BaselineStroke => 1.8;
    public static double FitStroke => 1.8;
    public static double PointStroke => 1.2;
    public static double GuideStroke => 1;
    public static double ZeroStroke => 1;
    public static double ZoomStroke => 1;

    public static double ProcessingMarkerHitWidth => 8;
    public static double ProcessingDragThreshold => 5;
    public static double ProcessingSplinePointRadius => 4.5;
    public static double ProcessingSplinePointInnerRadius => 1.7;
    public static double ProcessingSplinePointHitRadius => 8;
    public static double ProcessingSplineHandleRadius => 3.5;
    public static double ProcessingSplineHandleHitRadius => 7;
    public static double ProcessingSplineHandleStroke => 1;
    public static double ProcessingSelectedRegionOpacity => 0.16;
    public static double ProcessingMutedRegionOpacity => 0.08;
    public static double ProcessingSelectedRegionLineOpacity => 0.9;
    public static double ProcessingMutedRegionLineOpacity => 0.45;
    public static double ProcessingSelectedRegionStroke => 1.25;
    public static double ProcessingMutedRegionStroke => 1;

    public static double AnalysisSymbolSize => 7;
    public static double AnalysisHitSize => 12;
    public static double AnalysisResidualFraction => 0.22;
    public static double AnalysisResidualGap => 8;
    public static double AnalysisResidualMinimumHeight => 70;
    public static double AnalysisPointLabelYOffset => 19;
    public static double AnalysisHoverMarkerSize => 14;
    public static double AnalysisHoverMarkerCornerRadius => 2;

    public static void UseLightTheme() => Current = AvaloniaGraphTheme.Light;

    public static void UseDarkTheme() => Current = AvaloniaGraphTheme.Dark;

    public static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    public static IBrush Brush(string color, double opacity) => new SolidColorBrush(Color.Parse(color), opacity);

    public static Pen Pen(IBrush brush, double thickness) => new Pen(brush, thickness);
}

public sealed class AvaloniaGraphTheme
{
    AvaloniaGraphTheme(
        string canvas,
        string plot,
        string text,
        string mutedText,
        string frame,
        string axis,
        string majorGrid,
        string minorGrid,
        string data,
        string correctedData,
        string baseline,
        string region,
        string mutedRegion,
        string injection,
        string point,
        string excluded,
        string fit,
        string guide,
        string zero,
        string hover,
        string hoverBackground,
        string hoverBorder,
        string confidenceBand,
        string zoomFill,
        string zoomStroke)
    {
        CanvasBrush = AvaloniaGraphSettings.Brush(canvas);
        PlotBrush = AvaloniaGraphSettings.Brush(plot);
        TextBrush = AvaloniaGraphSettings.Brush(text);
        MutedTextBrush = AvaloniaGraphSettings.Brush(mutedText);
        FrameBrush = AvaloniaGraphSettings.Brush(frame);
        AxisBrush = AvaloniaGraphSettings.Brush(axis);
        MajorGridBrush = AvaloniaGraphSettings.Brush(majorGrid);
        MinorGridBrush = AvaloniaGraphSettings.Brush(minorGrid);
        DataBrush = AvaloniaGraphSettings.Brush(data);
        CorrectedDataBrush = AvaloniaGraphSettings.Brush(correctedData);
        BaselineBrush = AvaloniaGraphSettings.Brush(baseline);
        SplinePointBrush = BaselineBrush;
        RegionBrush = AvaloniaGraphSettings.Brush(region);
        MutedRegionBrush = AvaloniaGraphSettings.Brush(mutedRegion);
        InjectionBrush = AvaloniaGraphSettings.Brush(injection);
        PointBrush = AvaloniaGraphSettings.Brush(point);
        ExcludedBrush = AvaloniaGraphSettings.Brush(excluded);
        FitBrush = AvaloniaGraphSettings.Brush(fit);
        GuideBrush = AvaloniaGraphSettings.Brush(guide);
        ZeroBrush = AvaloniaGraphSettings.Brush(zero);
        HoverBrush = AvaloniaGraphSettings.Brush(hover);
        HoverBackgroundBrush = AvaloniaGraphSettings.Brush(hoverBackground);
        HoverBorderBrush = AvaloniaGraphSettings.Brush(hoverBorder);
        ConfidenceBandBrush = AvaloniaGraphSettings.Brush(confidenceBand);
        ZoomBrush = AvaloniaGraphSettings.Brush(zoomFill);
        ZoomStrokeBrush = AvaloniaGraphSettings.Brush(zoomStroke);

        FramePen = AvaloniaGraphSettings.Pen(FrameBrush, AvaloniaGraphSettings.FrameStroke);
        AxisPen = AvaloniaGraphSettings.Pen(AxisBrush, AvaloniaGraphSettings.AxisStroke);
        MajorGridPen = AvaloniaGraphSettings.Pen(MajorGridBrush, AvaloniaGraphSettings.MajorGridStroke);
        MinorGridPen = AvaloniaGraphSettings.Pen(MinorGridBrush, AvaloniaGraphSettings.MinorGridStroke);
        DataPen = AvaloniaGraphSettings.Pen(DataBrush, AvaloniaGraphSettings.DataStroke);
        OverviewDataPen = AvaloniaGraphSettings.Pen(DataBrush, AvaloniaGraphSettings.OverviewDataStroke);
        CorrectedDataPen = AvaloniaGraphSettings.Pen(CorrectedDataBrush, AvaloniaGraphSettings.DataStroke);
        InjectionPen = AvaloniaGraphSettings.Pen(InjectionBrush, AvaloniaGraphSettings.InjectionStroke);
        BaselinePen = AvaloniaGraphSettings.Pen(BaselineBrush, AvaloniaGraphSettings.BaselineStroke);
        PointPen = AvaloniaGraphSettings.Pen(PointBrush, AvaloniaGraphSettings.PointStroke);
        ExcludedPen = AvaloniaGraphSettings.Pen(ExcludedBrush, AvaloniaGraphSettings.PointStroke);
        FitPen = AvaloniaGraphSettings.Pen(FitBrush, AvaloniaGraphSettings.FitStroke);
        GuidePen = new Pen(GuideBrush, AvaloniaGraphSettings.GuideStroke) { DashStyle = DashStyle.Dash };
        ZeroPen = AvaloniaGraphSettings.Pen(ZeroBrush, AvaloniaGraphSettings.ZeroStroke);
        HoverPen = AvaloniaGraphSettings.Pen(HoverBrush, AvaloniaGraphSettings.HoverStroke);
        HoverBorderPen = AvaloniaGraphSettings.Pen(HoverBorderBrush, AvaloniaGraphSettings.HoverStroke);
        ZoomPen = AvaloniaGraphSettings.Pen(ZoomStrokeBrush, AvaloniaGraphSettings.ZoomStroke);
    }

    public static AvaloniaGraphTheme Light { get; } = new(
        canvas: "#F8FAFC",
        plot: "#FFFFFF",
        text: "#202832",
        mutedText: "#202832",
        frame: "#26323D",
        axis: "#26323D",
        majorGrid: "#D8DEE6",
        minorGrid: "#EEF2F6",
        data: "#1E5F84",
        correctedData: "#365D41",
        baseline: "#BE3A34",
        region: "#2563EB",
        mutedRegion: "#7B8794",
        injection: "#A46A2A",
        point: "#202832",
        excluded: "#7B8794",
        fit: "#111827",
        guide: "#8A96A3",
        zero: "#6B7682",
        hover: "#26323D",
        hoverBackground: "#FFFFFF",
        hoverBorder: "#B6C0CA",
        confidenceBand: "#3E5A6473",
        zoomFill: "#322563EB",
        zoomStroke: "#2563EB");

    public static AvaloniaGraphTheme Dark { get; } = new(
        canvas: "#101820",
        plot: "#17212B",
        text: "#E7EDF4",
        mutedText: "#E7EDF4",
        frame: "#CBD5DF",
        axis: "#CBD5DF",
        majorGrid: "#34404C",
        minorGrid: "#24303B",
        data: "#7CB7D8",
        correctedData: "#89B891",
        baseline: "#E17872",
        region: "#7EA8FF",
        mutedRegion: "#8D99A6",
        injection: "#D6A05C",
        point: "#E7EDF4",
        excluded: "#8D99A6",
        fit: "#F3F6FA",
        guide: "#A8B4C0",
        zero: "#9AA6B2",
        hover: "#E7EDF4",
        hoverBackground: "#1F2A35",
        hoverBorder: "#65717D",
        confidenceBand: "#4A7EA8D8",
        zoomFill: "#407EA8FF",
        zoomStroke: "#7EA8FF");

    public IBrush CanvasBrush { get; }
    public IBrush PlotBrush { get; }
    public IBrush TextBrush { get; }
    public IBrush MutedTextBrush { get; }
    public IBrush FrameBrush { get; }
    public IBrush AxisBrush { get; }
    public IBrush MajorGridBrush { get; }
    public IBrush MinorGridBrush { get; }
    public IBrush DataBrush { get; }
    public IBrush CorrectedDataBrush { get; }
    public IBrush BaselineBrush { get; }
    public IBrush SplinePointBrush { get; }
    public IBrush RegionBrush { get; }
    public IBrush MutedRegionBrush { get; }
    public IBrush InjectionBrush { get; }
    public IBrush PointBrush { get; }
    public IBrush ExcludedBrush { get; }
    public IBrush FitBrush { get; }
    public IBrush GuideBrush { get; }
    public IBrush ZeroBrush { get; }
    public IBrush HoverBrush { get; }
    public IBrush HoverBackgroundBrush { get; }
    public IBrush HoverBorderBrush { get; }
    public IBrush ConfidenceBandBrush { get; }
    public IBrush ZoomBrush { get; }
    public IBrush ZoomStrokeBrush { get; }

    public Pen FramePen { get; }
    public Pen AxisPen { get; }
    public Pen MajorGridPen { get; }
    public Pen MinorGridPen { get; }
    public Pen DataPen { get; }
    public Pen OverviewDataPen { get; }
    public Pen CorrectedDataPen { get; }
    public Pen InjectionPen { get; }
    public Pen BaselinePen { get; }
    public Pen PointPen { get; }
    public Pen ExcludedPen { get; }
    public Pen FitPen { get; }
    public Pen GuidePen { get; }
    public Pen ZeroPen { get; }
    public Pen HoverPen { get; }
    public Pen HoverBorderPen { get; }
    public Pen ZoomPen { get; }
}
