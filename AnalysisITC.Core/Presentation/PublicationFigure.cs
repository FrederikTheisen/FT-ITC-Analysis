using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Presentation
{
    public enum PublicationPanelKind
    {
        Thermogram,
        Fit,
        Residual
    }

    public enum PublicationAxisPlacement
    {
        Top,
        Bottom,
        Left,
        Right
    }

    public enum PublicationInfoBoxPlacement
    {
        Auto = 0,
        Upper = 1,
        Lower = 2
    }

    public enum PublicationSymbolShape
    {
        Square = 0,
        Circle = 1
    }

    public enum PublicationBaselineStyle { Solid, Dashed }
    public enum PublicationBaselineLayer { UnderData, OverData }
    public enum PublicationIntegrationRegionStyle { Bar, Fill, Line }

    public enum PublicationSeriesRole
    {
        Thermogram,
        Fit,
        Baseline
    }

    public sealed class PublicationFigureOptions
    {
        public const double DefaultPointsPerCentimeter = 0.5 * 227 / 2.54;

        public double PlotWidthCentimeters { get; set; } = 6;
        public double PlotHeightCentimeters { get; set; } = 10;
        public double PointsPerCentimeter { get; set; } = DefaultPointsPerCentimeter;
        public double FontSize { get; set; } = 14;

        public EnergyUnit EnergyUnit { get; set; } = AppSettings.EnergyUnit;
        public TimeUnit TimeUnit { get; set; } = TimeUnit.Minute;

        public bool ShowThermogram { get; set; } = true;
        public bool ShowResiduals { get; set; } = true;
        public bool ShowErrorBars { get; set; } = true;
        public bool ShowConfidenceBand { get; set; } = true;
        public bool ShowExperimentDetails { get; set; } = true;
        public bool ShowFitParameters { get; set; } = true;
        public bool ShowAxisTitles { get; set; } = true;
        public bool ShowFitLine { get; set; } = true;
        public bool DrawFitOffsetCorrected { get; set; } = true;
        public bool ShowBadData { get; set; } = true;
        public bool ShowBadDataErrorBars { get; set; } = false;
        public bool AutoAxesIgnoresBadData { get; set; } = true;
        public bool IncludeResidualGraphGap { get; set; } = true;
        public bool SanitizeTicks { get; set; } = true;
        public bool DrawBaselineCorrected { get; set; } = true;
        public bool ShowBaseline { get; set; } = false;
        public PublicationBaselineStyle BaselineStyle { get; set; } = PublicationBaselineStyle.Solid;
        public PublicationBaselineLayer BaselineLayer { get; set; } = PublicationBaselineLayer.OverData;
        public double BaselineWidth { get; set; } = 2;
        public bool ShowIntegrationRegions { get; set; }
        public PublicationIntegrationRegionStyle IntegrationRegionStyle { get; set; } = PublicationIntegrationRegionStyle.Fill;
        public bool ShowZeroLine { get; set; } = true;

        public int DataXTickCount { get; set; } = 7;
        public int DataYTickCount { get; set; } = 7;
        public int FitXTickCount { get; set; } = 7;
        public int FitYTickCount { get; set; } = 7;
        public int ResidualYTickCount { get; set; } = 3;

        public double ResidualPanelFraction { get; set; } = 0.2;
        public PublicationInfoBoxPlacement InformationBoxPlacement { get; set; } = PublicationInfoBoxPlacement.Auto;
        public PublicationSymbolShape SymbolShape { get; set; } = PublicationSymbolShape.Square;
        public double SymbolSize { get; set; } = 8;
        public double FitLineWidth { get; set; } = 2;
        public LineSmoothness FitLineSmoothness { get; set; } = AppSettings.FitLineSmoothness;

        public string PowerAxisTitle { get; set; } = "Differential Power (<unit>)";
        public string TimeAxisTitle { get; set; } = "Time (<unit>)";
        public string EnthalpyAxisTitle { get; set; } = "<unit> of injectant";
        public string XAxisTitle { get; set; }

        public double? DataXAxisMinimum { get; set; }
        public double? DataXAxisMaximum { get; set; }
        public double? DataYAxisMinimum { get; set; }
        public double? DataYAxisMaximum { get; set; }
        public double? FitXAxisMinimum { get; set; }
        public double? FitXAxisMaximum { get; set; }
        public double? FitYAxisMinimum { get; set; }
        public double? FitYAxisMaximum { get; set; }
        public double? ResidualYAxisMinimum { get; set; }
        public double? ResidualYAxisMaximum { get; set; }

        public FinalFigureDisplayParameters DisplayParameters { get; set; } = FinalFigureDisplayParameters.Default;
        public DisplayAttributeOptions AttributeOptions { get; set; } = DisplayAttributeOptions.Default;
        public UncertaintyDisplayStyle TextUncertaintyStyle { get; set; } = AppSettings.UncertaintyDisplayStyle;

        public string CacheKey
        {
            get
            {
                return string.Join("|", new[]
                {
                    PlotWidthCentimeters.ToString("G17"),
                    PlotHeightCentimeters.ToString("G17"),
                    PointsPerCentimeter.ToString("G17"),
                    FontSize.ToString("G17"),
                    ((int)EnergyUnit).ToString(),
                    ((int)TimeUnit).ToString(),
                    ShowThermogram.ToString(),
                    ShowResiduals.ToString(),
                    ShowErrorBars.ToString(),
                    ShowConfidenceBand.ToString(),
                    ShowExperimentDetails.ToString(),
                    ShowFitParameters.ToString(),
                    ShowAxisTitles.ToString(),
                    ShowFitLine.ToString(),
                    DrawFitOffsetCorrected.ToString(),
                    ShowBadData.ToString(),
                    ShowBadDataErrorBars.ToString(),
                    AutoAxesIgnoresBadData.ToString(),
                    IncludeResidualGraphGap.ToString(),
                    SanitizeTicks.ToString(),
                    DrawBaselineCorrected.ToString(),
                    ShowBaseline.ToString(),
                    ((int)BaselineStyle).ToString(),
                    ((int)BaselineLayer).ToString(),
                    BaselineWidth.ToString("G17"),
                    ShowIntegrationRegions.ToString(),
                    ((int)IntegrationRegionStyle).ToString(),
                    ShowZeroLine.ToString(),
                    DataXTickCount.ToString(),
                    DataYTickCount.ToString(),
                    FitXTickCount.ToString(),
                    FitYTickCount.ToString(),
                    ResidualYTickCount.ToString(),
                    ResidualPanelFraction.ToString("G17"),
                    ((int)InformationBoxPlacement).ToString(),
                    ((int)SymbolShape).ToString(),
                    SymbolSize.ToString("G17"),
                    FitLineWidth.ToString("G17"),
                    ((int)FitLineSmoothness).ToString(),
                    PowerAxisTitle ?? "",
                    TimeAxisTitle ?? "",
                    EnthalpyAxisTitle ?? "",
                    XAxisTitle ?? "",
                    NullableKey(DataXAxisMinimum),
                    NullableKey(DataXAxisMaximum),
                    NullableKey(DataYAxisMinimum),
                    NullableKey(DataYAxisMaximum),
                    NullableKey(FitXAxisMinimum),
                    NullableKey(FitXAxisMaximum),
                    NullableKey(FitYAxisMinimum),
                    NullableKey(FitYAxisMaximum),
                    NullableKey(ResidualYAxisMinimum),
                    NullableKey(ResidualYAxisMaximum),
                    ((int)DisplayParameters).ToString(),
                    ((int)AttributeOptions).ToString(),
                    ((int)TextUncertaintyStyle).ToString()
                });
            }
        }

        static string NullableKey(double? value)
        {
            return value.HasValue ? value.Value.ToString("G17") : "";
        }
    }

    public sealed class PublicationFigureDocument
    {
        public PublicationFigureDocument(PublicationFigureOptions options)
        {
            Options = options;
        }

        public PublicationFigureOptions Options { get; private set; }
        public string Title { get; set; } = "";
        public string Subject { get; set; } = "ITC publication figure";
        public string Creator { get; set; } = MarkdownStrings.AppName;
        public string FileName { get; set; } = "";
        public double PlotWidth { get; set; }
        public double PlotHeight { get; set; }
        public PublicationFigurePanel ThermogramPanel { get; set; }
        public PublicationFigurePanel FitPanel { get; set; }
        public PublicationFigurePanel ResidualPanel { get; set; }
        public List<string> MetadataKeywords { get; private set; } = new List<string>();

        public IEnumerable<PublicationFigurePanel> Panels
        {
            get
            {
                if (ThermogramPanel != null) yield return ThermogramPanel;
                if (FitPanel != null) yield return FitPanel;
                if (ResidualPanel != null) yield return ResidualPanel;
            }
        }
    }

    public sealed class PublicationFigurePanel
    {
        public PublicationPanelKind Kind { get; set; }
        public PublicationAxis XAxis { get; set; }
        public PublicationAxis YAxis { get; set; }
        public bool DrawZeroLine { get; set; }
        public List<PublicationSeries> Series { get; set; } = new List<PublicationSeries>();
        public List<PublicationBand> Bands { get; set; } = new List<PublicationBand>();
        public List<PublicationErrorPoint> Points { get; set; } = new List<PublicationErrorPoint>();
        public List<PublicationMarker> Markers { get; set; } = new List<PublicationMarker>();
        public List<PublicationIntegrationRegion> IntegrationRegions { get; set; } = new List<PublicationIntegrationRegion>();
        public List<PublicationAnnotationBox> AnnotationBoxes { get; set; } = new List<PublicationAnnotationBox>();
    }

    public sealed class PublicationAxis
    {
        public PublicationAxis(string title, PublicationAxisPlacement placement, double minimum, double maximum, int maxTicks, bool preferCenteredTicks = false, bool sanitizeTicks = true)
        {
            Title = title ?? "";
            Placement = placement;
            Minimum = minimum;
            Maximum = maximum;
            MaxTicks = maxTicks;
            PreferCenteredTicks = preferCenteredTicks;
            SanitizeTicks = sanitizeTicks;

            NormalizeRange();
            BuildTicks();
        }

        public string Title { get; set; }
        public PublicationAxisPlacement Placement { get; set; }
        public double Minimum { get; private set; }
        public double Maximum { get; private set; }
        public int MaxTicks { get; private set; }
        public bool PreferCenteredTicks { get; private set; }
        public bool SanitizeTicks { get; private set; }
        public double TickSpacing { get; private set; }
        public int DecimalPlaces { get; private set; }
        public List<double> MajorTicks { get; private set; } = new List<double>();
        public List<double> MinorTicks { get; private set; } = new List<double>();

        public string FormatTick(double value)
        {
            if (Math.Abs(value) < 1E-12) value = 0;
            if (DecimalPlaces <= 0) return value.ToString("0");

            return value.ToString("0." + new string('0', DecimalPlaces));
        }

        void NormalizeRange()
        {
            if (!IsFinite(Minimum) || !IsFinite(Maximum))
            {
                Minimum = -1;
                Maximum = 1;
            }

            if (Maximum < Minimum)
            {
                var min = Minimum;
                Minimum = Maximum;
                Maximum = min;
            }

            if (Math.Abs(Maximum - Minimum) < double.Epsilon)
            {
                Minimum -= 0.5;
                Maximum += 0.5;
            }
        }

        void BuildTicks()
        {
            if (!SanitizeTicks)
            {
                BuildLinearTicks();
                return;
            }

            var ticks = NiceTicks(Minimum, Maximum, Math.Max(2, MaxTicks), out var spacing);
            TickSpacing = spacing;
            var minorTicks = BuildMinorTicks(ticks);

            ticks.RemoveAll(tick => tick < Minimum - spacing * 0.001 || tick > Maximum + spacing * 0.001);
            ticks = PreferThreeCenteredTicks(ticks);

            MajorTicks = ticks.Select(NormalizeZero).ToList();
            MinorTicks = minorTicks;
            DecimalPlaces = EstimateDecimalPlaces(MajorTicks, spacing);
        }

        void BuildLinearTicks()
        {
            var tickCount = Math.Max(2, MaxTicks);
            var spacing = (Maximum - Minimum) / Math.Max(1, tickCount - 1);
            if (!IsFinite(spacing) || Math.Abs(spacing) < double.Epsilon) spacing = 1;

            TickSpacing = spacing;
            MajorTicks = Enumerable.Range(0, tickCount)
                .Select(index => NormalizeZero(Minimum + spacing * index))
                .ToList();
            MinorTicks = BuildMinorTicks(MajorTicks);
            DecimalPlaces = EstimateDecimalPlaces(MajorTicks, spacing);
        }

        List<double> PreferThreeCenteredTicks(List<double> ticks)
        {
            if (!PreferCenteredTicks || ticks.Count <= 3 || !ContainsTick(ticks, 0)) return ticks;

            foreach (var positive in ticks.Where(value => value > 0).OrderByDescending(value => value))
            {
                if (ContainsTick(ticks, -positive))
                {
                    return new List<double> { -positive, 0, positive };
                }
            }

            return ticks;
        }

        List<double> BuildMinorTicks(List<double> majorTicks)
        {
            var minor = new List<double>();

            for (int i = 0; i < majorTicks.Count - 1; i++)
            {
                var tick = 0.5 * (majorTicks[i] + majorTicks[i + 1]);
                if (tick >= Minimum && tick <= Maximum) minor.Add(NormalizeZero(tick));
            }

            return minor;
        }

        static int EstimateDecimalPlaces(List<double> ticks, double spacing)
        {
            var places = RequiredDecimalPlaces(spacing);

            foreach (var tick in ticks)
            {
                places = Math.Max(places, RequiredDecimalPlaces(tick));
            }

            return places;
        }

        static int RequiredDecimalPlaces(double value)
        {
            if (!IsFinite(value)) return 0;

            value = Math.Abs(value);
            for (int places = 0; places <= 6; places++)
            {
                if (Math.Abs(value - Math.Round(value, places)) < 1E-9)
                    return places;
            }

            return 6;
        }

        static List<double> NiceTicks(double minimum, double maximum, int maxTicks, out double spacing)
        {
            var range = NiceNumber(maximum - minimum, false);
            spacing = NiceNumber(range / (maxTicks + 1), true);
            if (!IsFinite(spacing) || spacing <= 0) spacing = 1;

            var niceMin = Math.Floor(minimum / spacing) * spacing;
            var niceMax = Math.Ceiling(maximum / spacing) * spacing;
            var ticks = new List<double>();
            var guard = 0;

            for (var tick = niceMin; tick <= niceMax + spacing * 0.5 && guard++ < 1000; tick += spacing)
            {
                ticks.Add(tick);
            }

            return ticks;
        }

        static double NiceNumber(double value, bool round)
        {
            if (!IsFinite(value) || value <= 0) return 1;

            var exponent = Math.Floor(Math.Log10(value));
            var fraction = value / Math.Pow(10, exponent);
            double niceFraction;

            if (round)
            {
                if (fraction < 1.5) niceFraction = 1;
                else if (fraction < 3) niceFraction = 2;
                else if (fraction < 7) niceFraction = 5;
                else niceFraction = 10;
            }
            else
            {
                if (fraction <= 1) niceFraction = 1;
                else if (fraction <= 2) niceFraction = 2;
                else if (fraction <= 5) niceFraction = 5;
                else niceFraction = 10;
            }

            return niceFraction * Math.Pow(10, exponent);
        }

        static bool ContainsTick(IEnumerable<double> ticks, double target)
        {
            return ticks.Any(tick => Math.Abs(tick - target) < 1E-6);
        }

        static double NormalizeZero(double value)
        {
            return Math.Abs(value) < 1E-12 ? 0 : value;
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public sealed class PublicationSeries
    {
        public PublicationSeriesRole Role { get; set; }
        public List<PublicationPoint> Points { get; set; } = new List<PublicationPoint>();
    }

    public sealed class PublicationIntegrationRegion
    {
        public List<PublicationPoint> Data { get; set; } = new List<PublicationPoint>();
        public List<PublicationPoint> Baseline { get; set; } = new List<PublicationPoint>();
        public bool BarAtTop { get; set; }
    }

    public sealed class PublicationBand
    {
        public List<PublicationPoint> Upper { get; set; } = new List<PublicationPoint>();
        public List<PublicationPoint> Lower { get; set; } = new List<PublicationPoint>();
    }

    public sealed class PublicationPoint
    {
        public PublicationPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class PublicationErrorPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double LowerY { get; set; }
        public double UpperY { get; set; }
        public bool Included { get; set; } = true;
    }

    public sealed class PublicationMarker
    {
        public PublicationMarker(double x)
        {
            X = x;
        }

        public double X { get; private set; }
    }

    public sealed class PublicationAnnotationBox
    {
        public PublicationInfoBoxPlacement Placement { get; set; } = PublicationInfoBoxPlacement.Auto;
        public List<string> Lines { get; private set; } = new List<string>();
    }

    public sealed class PublicationFigureSource
    {
        public PublicationFigureSource(ExperimentData experiment, SolutionInterface solution = null)
        {
            Experiment = experiment ?? throw new ArgumentNullException(nameof(experiment));
            Solution = solution;
        }

        public ExperimentData Experiment { get; private set; }
        public SolutionInterface Solution { get; private set; }
    }

    public static class PublicationFigureBuilder
    {
        const double DataXAxisBuffer = 0.05;
        const double FitXAxisBuffer = 0.05;
        const double FitXAxisRoundPadding = 0.33;

        public static PublicationFigureDocument Build(ExperimentData data, PublicationFigureOptions options)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return Build(new PublicationFigureSource(data, data.Solution), options);
        }

        public static PublicationFigureDocument Build(PublicationFigureSource source, PublicationFigureOptions options)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            options = options ?? new PublicationFigureOptions();
            var data = source.Experiment;
            var solution = source.Solution;

            var document = new PublicationFigureDocument(options)
            {
                Title = data.Name ?? "",
                FileName = data.FileName ?? "",
                PlotWidth = options.PlotWidthCentimeters * options.PointsPerCentimeter,
                PlotHeight = options.PlotHeightCentimeters * options.PointsPerCentimeter,
            };

            if (options.ShowThermogram && data.HasThermogram)
            {
                document.ThermogramPanel = BuildThermogramPanel(data, solution, options);
            }

            document.FitPanel = BuildFitPanel(data, solution, options);

            if (options.ShowResiduals && solution != null)
            {
                document.ResidualPanel = BuildResidualPanel(data, solution, options, document.FitPanel.XAxis);
            }

            document.MetadataKeywords.AddRange(BuildMetadataKeywords(data, solution, options));
            return document;
        }

        static PublicationFigurePanel BuildThermogramPanel(ExperimentData data, SolutionInterface solution, PublicationFigureOptions options)
        {
            var powerScale = PowerScale(options.EnergyUnit);
            var timeScale = options.TimeUnit.GetProperties().Mod;
            var points = GetThermogramPoints(data);

            var yValues = points.Select(point => point.Y).ToList();
            if (options.DrawBaselineCorrected && yValues.Count > 0)
            {
                if (data.AverageHeatDirection == PeakHeatDirection.Exothermal)
                    yValues = new[] { yValues.Min(), 0.0 }.ToList();
                else if (data.AverageHeatDirection == PeakHeatDirection.Endothermal)
                    yValues = new[] { 0.0, yValues.Max() }.ToList();
            }

            var yRange = RangeWithBuffer(yValues, 0.1, includeZero: false);
            var xRange = RangeWithBuffer(points.Select(point => point.X), DataXAxisBuffer, includeZero: true);
            yRange = ApplyAxisOverrides(yRange[0], yRange[1], options.DataYAxisMinimum, options.DataYAxisMaximum);
            xRange = ApplyAxisOverrides(xRange[0], xRange[1], options.DataXAxisMinimum, options.DataXAxisMaximum);

            var panel = new PublicationFigurePanel
            {
                Kind = PublicationPanelKind.Thermogram,
                XAxis = new PublicationAxis(FormatAxisTitle(options.TimeAxisTitle, options.TimeUnit.GetProperties().Short), PublicationAxisPlacement.Top, xRange[0], xRange[1], options.DataXTickCount, sanitizeTicks: options.SanitizeTicks),
                YAxis = new PublicationAxis(FormatAxisTitle(options.PowerAxisTitle, options.EnergyUnit.IsSI() ? "µW" : "µcal/s"), PublicationAxisPlacement.Left, yRange[0], yRange[1], options.DataYTickCount, sanitizeTicks: options.SanitizeTicks)
            };

            var thermogram = new PublicationSeries
            {
                Role = PublicationSeriesRole.Thermogram,
                Points = points
            };
            var baseline = BuildBaselineSeries(data, options, powerScale, timeScale);
            if (baseline != null && options.BaselineLayer == PublicationBaselineLayer.UnderData)
                panel.Series.Add(baseline);
            panel.Series.Add(thermogram);
            if (baseline != null && options.BaselineLayer == PublicationBaselineLayer.OverData)
                panel.Series.Add(baseline);

            if (options.ShowIntegrationRegions)
                panel.IntegrationRegions.AddRange(BuildIntegrationRegions(data, options, points, powerScale, timeScale));

            // foreach (var injection in data.Injections)
            // {
            //     panel.Markers.Add(new PublicationMarker(injection.Time * timeScale));
            // }

            if (options.ShowExperimentDetails)
            {
                var box = BuildExperimentDetailsBox(data, solution, options);
                if (box.Lines.Count > 0) panel.AnnotationBoxes.Add(box);
            }

            return panel;

            List<PublicationPoint> GetThermogramPoints(ExperimentData experiment)
            {
                var source = options.DrawBaselineCorrected && experiment.BaseLineCorrectedDataPoints != null && experiment.BaseLineCorrectedDataPoints.Count > 0
                    ? experiment.BaseLineCorrectedDataPoints
                    : experiment.DataPoints;

                return source
                    .Where(point => IsFinite(point.Time) && IsFinite(point.Power))
                    .Select(point => new PublicationPoint(point.Time * timeScale, point.Power * powerScale))
                    .ToList();
            }
        }

        static PublicationSeries BuildBaselineSeries(ExperimentData data, PublicationFigureOptions options, double powerScale, double timeScale)
        {
            if (!options.ShowBaseline) return null;
            var points = BuildBaselinePoints(data, options, powerScale, timeScale);
            return points == null ? null : new PublicationSeries { Role = PublicationSeriesRole.Baseline, Points = points };
        }

        static List<PublicationPoint> BuildBaselinePoints(ExperimentData data, PublicationFigureOptions options, double powerScale, double timeScale)
        {
            if (data.DataPoints == null || data.DataPoints.Count < 2) return null;

            var points = new List<PublicationPoint>();
            if (options.DrawBaselineCorrected)
            {
                points.Add(new PublicationPoint(data.DataPoints.First().Time * timeScale, 0));
                points.Add(new PublicationPoint(data.DataPoints.Last().Time * timeScale, 0));
            }
            else
            {
                var baseline = data.Processor?.Interpolator?.Baseline;
                if (baseline == null || baseline.Count != data.DataPoints.Count) return null;
                for (var i = 0; i < data.DataPoints.Count; i++)
                    points.Add(new PublicationPoint(data.DataPoints[i].Time * timeScale, baseline[i].Value * powerScale));
            }

            return points;
        }

        static IEnumerable<PublicationIntegrationRegion> BuildIntegrationRegions(ExperimentData data, PublicationFigureOptions options, List<PublicationPoint> thermogram, double powerScale, double timeScale)
        {
            if (data.Injections == null || thermogram.Count < 2) yield break;
            var baseline = BuildBaselinePoints(data, options, powerScale, timeScale);
            if (baseline == null) yield break;

            foreach (var injection in data.Injections.Where(item => item.Include))
            {
                var start = injection.IntegrationStartTime * timeScale;
                var end = injection.IntegrationEndTime * timeScale;
                if (end <= start) continue;

                var times = thermogram.Where(point => point.X > start && point.X < end).Select(point => point.X).ToList();
                times.Insert(0, start);
                times.Add(end);

                yield return new PublicationIntegrationRegion
                {
                    Data = times.Select(time => new PublicationPoint(time, Interpolate(thermogram, time))).ToList(),
                    Baseline = times.Select(time => new PublicationPoint(time, Interpolate(baseline, time))).ToList(),
                    BarAtTop = data.AverageHeatDirection != PeakHeatDirection.Endothermal
                };
            }
        }

        static double Interpolate(List<PublicationPoint> points, double x)
        {
            if (x <= points[0].X) return points[0].Y;
            if (x >= points[points.Count - 1].X) return points[points.Count - 1].Y;

            for (var i = 1; i < points.Count; i++)
            {
                if (points[i].X < x) continue;
                var previous = points[i - 1];
                var next = points[i];
                if (Math.Abs(next.X - previous.X) < double.Epsilon) return previous.Y;
                return previous.Y + (x - previous.X) / (next.X - previous.X) * (next.Y - previous.Y);
            }

            return points[points.Count - 1].Y;
        }

        static PublicationFigurePanel BuildFitPanel(ExperimentData data, SolutionInterface solution, PublicationFigureOptions options)
        {
            var energyScale = Energy.ScaleFactor(options.EnergyUnit);
            var drawWithOffset = !options.DrawFitOffsetCorrected;
            var injectionPoints = new List<PublicationErrorPoint>();

            foreach (var injection in data.Injections)
            {
                if (!options.ShowBadData && !injection.Include) continue;

                var y = drawWithOffset || solution == null
                    ? injection.Enthalpy
                    : injection.Enthalpy - solution.Offset;
                var sd = options.ShowErrorBars && (options.ShowBadDataErrorBars || injection.Include) ? injection.SD : 0;
                var x = AnalysisXValue(data, injection);

                if (!IsFinite(x) || !IsFinite(y)) continue;

                injectionPoints.Add(new PublicationErrorPoint
                {
                    X = x,
                    Y = y * energyScale,
                    LowerY = (y - sd) * energyScale,
                    UpperY = (y + sd) * energyScale,
                    Included = injection.Include
                });
            }

            var fitPoints = new List<PublicationPoint>();
            if (solution?.Model != null)
            {
                foreach (var injection in data.Injections)
                {
                    var y = solution.Model.EvaluateEnthalpy(injection.ID, drawWithOffset);
                    var x = AnalysisXValue(data, injection);
                    if (IsFinite(x) && IsFinite(y))
                    {
                        fitPoints.Add(new PublicationPoint(x, y * energyScale));
                    }
                }
            }

            var scalingPoints = injectionPoints
                .Where(point => point.Included || !options.AutoAxesIgnoresBadData)
                .ToList();
            if (scalingPoints.Count == 0)
                scalingPoints = injectionPoints;

            var values = scalingPoints.Select(point => point.Y).ToList();
            values.AddRange(scalingPoints.Select(point => point.LowerY));
            values.AddRange(scalingPoints.Select(point => point.UpperY));
            values.AddRange(fitPoints.Select(point => point.Y));
            values.Add(0);

            var xValues = injectionPoints.Select(point => point.X).Concat(fitPoints.Select(point => point.X)).ToList();
            if (xValues.Count == 0) xValues.AddRange(new[] { 0.0, 1.0 });
            var xRange = FitXAxisRangeWithMacBuffer(xValues);
            var yRange = RangeWithBuffer(values, 0.1, includeZero: true);
            xRange = ApplyAxisOverrides(xRange[0], xRange[1], options.FitXAxisMinimum, options.FitXAxisMaximum);
            yRange = ApplyAxisOverrides(yRange[0], yRange[1], options.FitYAxisMinimum, options.FitYAxisMaximum);
            var xAxisTitle = string.IsNullOrWhiteSpace(options.XAxisTitle)
                ? data.AxisType.GetEnumDescription()
                : options.XAxisTitle;

            var panel = new PublicationFigurePanel
            {
                Kind = PublicationPanelKind.Fit,
                XAxis = new PublicationAxis(xAxisTitle, PublicationAxisPlacement.Bottom, xRange[0], xRange[1], options.FitXTickCount, sanitizeTicks: options.SanitizeTicks),
                YAxis = new PublicationAxis(FormatAxisTitle(options.EnthalpyAxisTitle, options.EnergyUnit.GetUnit() + "/mol"), PublicationAxisPlacement.Left, yRange[0], yRange[1], options.FitYTickCount, sanitizeTicks: options.SanitizeTicks),
                DrawZeroLine = options.ShowZeroLine
            };

            panel.Points.AddRange(injectionPoints);

            if (options.ShowFitLine && fitPoints.Count > 0)
            {
                panel.Series.Add(new PublicationSeries
                {
                    Role = PublicationSeriesRole.Fit,
                    Points = fitPoints.OrderBy(point => point.X).ToList()
                });
            }

            if (options.ShowConfidenceBand)
            {
                var band = BuildConfidenceBand(data, solution, options, drawWithOffset, energyScale);
                if (band.Upper.Count > 0 && band.Lower.Count > 0) panel.Bands.Add(band);
            }

            if (options.ShowFitParameters && solution != null)
            {
                var box = BuildFitParameterBox(solution, options);
                if (box.Lines.Count > 0) panel.AnnotationBoxes.Add(box);
            }

            return panel;
        }

        static PublicationFigurePanel BuildResidualPanel(ExperimentData data, SolutionInterface solution, PublicationFigureOptions options, PublicationAxis parentXAxis)
        {
            var energyScale = Energy.ScaleFactor(options.EnergyUnit);
            var points = new List<PublicationErrorPoint>();

            foreach (var injection in data.Injections)
            {
                if (!options.ShowBadData && !injection.Include) continue;

                var residual = solution?.Model == null || injection.InjectionMass == 0
                    ? 0
                    : solution.Model.Residual(injection) / injection.InjectionMass;
                var sd = options.ShowErrorBars && (options.ShowBadDataErrorBars || injection.Include) ? injection.SD : 0;
                var x = AnalysisXValue(data, injection);

                if (!IsFinite(x) || !IsFinite(residual)) continue;

                points.Add(new PublicationErrorPoint
                {
                    X = x,
                    Y = residual * energyScale,
                    LowerY = (residual - sd) * energyScale,
                    UpperY = (residual + sd) * energyScale,
                    Included = injection.Include
                });
            }

            var scalingPoints = points
                .Where(point => point.Included || !options.AutoAxesIgnoresBadData)
                .ToList();
            if (scalingPoints.Count == 0)
                scalingPoints = points;

            var max = scalingPoints.Count == 0
                ? 1
                : 1.5 * Math.Max(scalingPoints.SelectMany(point => new[]
                {
                    Math.Abs(point.Y),
                    Math.Abs(point.LowerY),
                    Math.Abs(point.UpperY)
                }).Max(), 1E-3);
            var yRange = ApplyAxisOverrides(-max, max, options.ResidualYAxisMinimum, options.ResidualYAxisMaximum);

            var panel = new PublicationFigurePanel
            {
                Kind = PublicationPanelKind.Residual,
                XAxis = new PublicationAxis(parentXAxis.Title, PublicationAxisPlacement.Bottom, parentXAxis.Minimum, parentXAxis.Maximum, options.FitXTickCount, sanitizeTicks: options.SanitizeTicks),
                YAxis = new PublicationAxis("", PublicationAxisPlacement.Left, yRange[0], yRange[1], options.ResidualYTickCount, preferCenteredTicks: true, sanitizeTicks: options.SanitizeTicks),
                DrawZeroLine = options.ShowZeroLine
            };

            panel.Points.AddRange(points);
            return panel;
        }

        static PublicationBand BuildConfidenceBand(ExperimentData data, SolutionInterface solution, PublicationFigureOptions options, bool drawWithOffset, double energyScale)
        {
            var band = new PublicationBand();

            if (solution?.BootstrapSolutions == null || solution.BootstrapSolutions.Count == 0 || solution.Model == null)
            {
                return band;
            }

            foreach (var injection in data.Injections)
            {
                var y = solution.Model.EvaluateBootstrap(injection.ID, drawWithOffset).WithConfidence();
                var x = AnalysisXValue(data, injection);
                if (!IsFinite(x) || y == null || y.Length < 2 || !IsFinite(y[0]) || !IsFinite(y[1])) continue;

                band.Upper.Add(new PublicationPoint(x, y[1] * energyScale));
                band.Lower.Add(new PublicationPoint(x, y[0] * energyScale));
            }

            band.Upper = band.Upper.OrderBy(point => point.X).ToList();
            band.Lower = band.Lower.OrderBy(point => point.X).ToList();
            return band;
        }

        static PublicationAnnotationBox BuildExperimentDetailsBox(ExperimentData data, SolutionInterface solution, PublicationFigureOptions options)
        {
            var box = new PublicationAnnotationBox
            {
                Placement = options.InformationBoxPlacement
            };

            if (options.DisplayParameters.HasFlag(FinalFigureDisplayParameters.Temperature))
            {
                box.Lines.Add(data.MeasuredTemperature.ToString("F1") + " °C");
            }

            if (options.DisplayParameters.HasFlag(FinalFigureDisplayParameters.Concentrations))
            {
                box.Lines.Add("[Syringe] = " + data.SyringeConcentration.AsFormattedConcentration(true));

                if (data.CellConcentration > float.Epsilon)
                {
                    box.Lines.Add("[Cell] = " + data.CellConcentration.AsFormattedConcentration(true));
                }
            }

            if (options.DisplayParameters.HasFlag(FinalFigureDisplayParameters.InjectionDelay))
            {
                var delay = data.GetInjectionDelayInfoString();
                if (!string.IsNullOrWhiteSpace(delay)) box.Lines.Add("Delay: " + delay);
            }

            if (options.DisplayParameters.HasFlag(FinalFigureDisplayParameters.Instrument))
            {
                var instrument = data.Instrument.GetProperties()?.Name;
                if (!string.IsNullOrWhiteSpace(instrument)) box.Lines.Add(instrument);
            }

            if (options.DisplayParameters.HasFlag(FinalFigureDisplayParameters.Attributes) && solution != null)
            {
                var attributes = solution.UIExperimentModelAttributes(options.AttributeOptions);
                foreach (var attribute in attributes)
                {
                    var line = attribute.Item1;
                    if (!string.IsNullOrEmpty(attribute.Item2)) line += " = " + attribute.Item2;
                    box.Lines.Add(line);
                }
            }

            return box;
        }

        static PublicationAnnotationBox BuildFitParameterBox(SolutionInterface solution, PublicationFigureOptions options)
        {
            var box = new PublicationAnnotationBox
            {
                Placement = options.InformationBoxPlacement
            };

            var previousStyle = AppSettings.UncertaintyDisplayStyle;
            AppSettings.UncertaintyDisplayStyle = options.TextUncertaintyStyle;

            try
            {
                foreach (var parameter in solution.UISolutionParameters(options.DisplayParameters))
                {
                    if (options.DisplayParameters.HasFlag(FinalFigureDisplayParameters.Model) && box.Lines.Count == 0)
                    {
                        box.Lines.Add($"{parameter.Item1} | RMSD = {parameter.Item2}");
                    }
                    else
                    {
                        box.Lines.Add($"{parameter.Item1} = {parameter.Item2}");
                    }
                }
            }
            finally
            {
                AppSettings.UncertaintyDisplayStyle = previousStyle;
            }

            return box;
        }

        static List<string> BuildMetadataKeywords(ExperimentData data, SolutionInterface solution, PublicationFigureOptions options)
        {
            var keywords = new List<string>
            {
                $"Name: {data.Name}",
                $"File: {data.FileName}",
                $"File Date: {data.Date:yyyy-MM-dd}",
                $"[Syringe]: {data.SyringeConcentration.AsConcentration(ConcentrationUnit.µM, true)}",
                $"[Cell]: {data.CellConcentration.AsConcentration(ConcentrationUnit.µM, true)}",
                $"Model: {(solution == null ? "" : solution.ModelType.GetProperties()?.Name ?? solution.ModelType.ToString())}",
                $"Solution: {(solution == null ? "" : solution.IsGlobalAnalysisSolution ? "Global" : "Single")}",
                $"Loss: {(solution == null ? "" : solution.Loss.ToString("G3"))}"
            };

            if (solution == null) return keywords.Select(keyword => keyword.Replace(",", "..")).ToList();

            foreach (var parameter in solution.UISolutionParameters(options.DisplayParameters))
            {
                keywords.Add($"{parameter.Item1} = {parameter.Item2}");
            }

            return keywords.Select(keyword => keyword.Replace(",", "..")).ToList();
        }

        static double AnalysisXValue(ExperimentData data, InjectionData injection)
        {
            return data.AxisType switch
            {
                AnalysisXAxisType.TitrantConcentration => injection.ActualTitrantConcentration * 1000000,
                AnalysisXAxisType.ID => injection.ID + 1,
                _ => injection.Ratio
            };
        }

        static double PowerScale(EnergyUnit energyUnit)
        {
            return energyUnit.IsSI() ? 1000000 : 1000000 * Energy.JouleToCalFactor;
        }

        static string FormatAxisTitle(string template, string unit)
        {
            if (string.IsNullOrWhiteSpace(template)) return "";

            return template.Contains("<unit>")
                ? template.Replace("<unit>", unit)
                : template;
        }

        static double[] RangeWithBuffer(IEnumerable<double> values, double buffer, bool includeZero)
        {
            var finite = values.Where(IsFinite).ToList();
            if (includeZero) finite.Add(0);
            if (finite.Count == 0) finite.AddRange(new[] { -1.0, 1.0 });

            var min = finite.Min();
            var max = finite.Max();
            var delta = max - min;
            if (!IsFinite(delta) || Math.Abs(delta) < double.Epsilon) delta = Math.Max(1, Math.Abs(max));

            return new[]
            {
                min - delta * buffer,
                max + delta * buffer
            };
        }

        static double[] FitXAxisRangeWithMacBuffer(IEnumerable<double> values)
        {
            var finite = values.Where(IsFinite).ToList();
            if (finite.Count == 0) finite.Add(1);

            var firstPass = RangeWithBuffer(new[] { 0, finite.Max() }, FitXAxisBuffer, includeZero: false);
            var roundedMaximum = Math.Max(Math.Floor(firstPass[1] + FitXAxisRoundPadding), firstPass[1]);

            return RangeWithBuffer(new[] { 0, roundedMaximum }, FitXAxisBuffer, includeZero: false);
        }

        static double[] ApplyAxisOverrides(double minimum, double maximum, double? overrideMinimum, double? overrideMaximum)
        {
            if (overrideMinimum.HasValue && IsFinite(overrideMinimum.Value))
                minimum = overrideMinimum.Value;

            if (overrideMaximum.HasValue && IsFinite(overrideMaximum.Value))
                maximum = overrideMaximum.Value;

            return new[] { minimum, maximum };
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
