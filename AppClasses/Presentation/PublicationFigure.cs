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

        public EnergyUnit EnergyUnit { get; set; } = AppSettings.EnergyUnit;
        public TimeUnit TimeUnit { get; set; } = TimeUnit.Minute;

        public bool ShowThermogram { get; set; } = true;
        public bool ShowResiduals { get; set; } = true;
        public bool ShowErrorBars { get; set; } = true;
        public bool ShowConfidenceBand { get; set; } = true;
        public bool ShowExperimentDetails { get; set; } = true;
        public bool ShowFitParameters { get; set; } = true;
        public bool ShowAxisTitles { get; set; } = true;
        public bool DrawFitOffsetCorrected { get; set; } = true;
        public bool ShowBadData { get; set; } = true;
        public bool ShowBadDataErrorBars { get; set; } = false;
        public bool IncludeResidualGraphGap { get; set; } = true;

        public int DataXTickCount { get; set; } = 7;
        public int DataYTickCount { get; set; } = 7;
        public int FitXTickCount { get; set; } = 7;
        public int FitYTickCount { get; set; } = 7;
        public int ResidualYTickCount { get; set; } = 3;

        public double ResidualPanelFraction { get; set; } = 0.2;
        public PublicationInfoBoxPlacement InformationBoxPlacement { get; set; } = PublicationInfoBoxPlacement.Auto;
        public PublicationSymbolShape SymbolShape { get; set; } = PublicationSymbolShape.Square;
        public double SymbolSize { get; set; } = 6;

        public string PowerAxisTitle { get; set; } = "Differential Power (<unit>)";
        public string TimeAxisTitle { get; set; } = "Time (<unit>)";
        public string EnthalpyAxisTitle { get; set; } = "<unit> of injectant";
        public string XAxisTitle { get; set; }

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
                    ((int)EnergyUnit).ToString(),
                    ((int)TimeUnit).ToString(),
                    ShowThermogram.ToString(),
                    ShowResiduals.ToString(),
                    ShowErrorBars.ToString(),
                    ShowConfidenceBand.ToString(),
                    ShowExperimentDetails.ToString(),
                    ShowFitParameters.ToString(),
                    ShowAxisTitles.ToString(),
                    DrawFitOffsetCorrected.ToString(),
                    ShowBadData.ToString(),
                    ShowBadDataErrorBars.ToString(),
                    IncludeResidualGraphGap.ToString(),
                    DataXTickCount.ToString(),
                    DataYTickCount.ToString(),
                    FitXTickCount.ToString(),
                    FitYTickCount.ToString(),
                    ResidualYTickCount.ToString(),
                    ResidualPanelFraction.ToString("G17"),
                    ((int)InformationBoxPlacement).ToString(),
                    ((int)SymbolShape).ToString(),
                    SymbolSize.ToString("G17"),
                    PowerAxisTitle ?? "",
                    TimeAxisTitle ?? "",
                    EnthalpyAxisTitle ?? "",
                    XAxisTitle ?? "",
                    ((int)DisplayParameters).ToString(),
                    ((int)AttributeOptions).ToString(),
                    ((int)TextUncertaintyStyle).ToString()
                });
            }
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
        public List<PublicationAnnotationBox> AnnotationBoxes { get; set; } = new List<PublicationAnnotationBox>();
    }

    public sealed class PublicationAxis
    {
        public PublicationAxis(string title, PublicationAxisPlacement placement, double minimum, double maximum, int maxTicks, bool preferCenteredTicks = false)
        {
            Title = title ?? "";
            Placement = placement;
            Minimum = minimum;
            Maximum = maximum;
            MaxTicks = maxTicks;
            PreferCenteredTicks = preferCenteredTicks;

            NormalizeRange();
            BuildTicks();
        }

        public string Title { get; set; }
        public PublicationAxisPlacement Placement { get; set; }
        public double Minimum { get; private set; }
        public double Maximum { get; private set; }
        public int MaxTicks { get; private set; }
        public bool PreferCenteredTicks { get; private set; }
        public double TickSpacing { get; private set; }
        public int DecimalPlaces { get; private set; }
        public List<double> MajorTicks { get; private set; } = new List<double>();
        public List<double> MinorTicks { get; private set; } = new List<double>();

        public string FormatTick(double value)
        {
            if (Math.Abs(value) < 1E-12) value = 0;
            if (DecimalPlaces <= 0) return value.ToString("0");

            return value.ToString("0." + new string('#', DecimalPlaces));
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
            var ticks = NiceTicks(Minimum, Maximum, Math.Max(2, MaxTicks), out var spacing);
            TickSpacing = spacing;

            ticks.RemoveAll(tick => tick < Minimum - spacing * 0.001 || tick > Maximum + spacing * 0.001);
            ticks = PreferThreeCenteredTicks(ticks);

            MajorTicks = ticks.Select(NormalizeZero).ToList();
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
            var places = 0;
            var absSpacing = Math.Abs(spacing);

            if (absSpacing > 0 && absSpacing < 1)
            {
                places = Math.Min(6, Math.Max(1, (int)Math.Ceiling(-Math.Log10(absSpacing)) + 1));
            }

            foreach (var tick in ticks)
            {
                var text = tick.ToString("G12");
                var decimalIndex = text.IndexOf('.');
                if (decimalIndex < 0) continue;

                places = Math.Max(places, Math.Min(6, text.Length - decimalIndex - 1));
            }

            return places;
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

    public static class PublicationFigureBuilder
    {
        public static PublicationFigureDocument Build(ExperimentData data, PublicationFigureOptions options)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            options = options ?? new PublicationFigureOptions();

            var document = new PublicationFigureDocument(options)
            {
                Title = data.Name ?? "",
                FileName = data.FileName ?? "",
                PlotWidth = options.PlotWidthCentimeters * options.PointsPerCentimeter,
                PlotHeight = options.PlotHeightCentimeters * options.PointsPerCentimeter,
            };

            if (options.ShowThermogram && data.HasThermogram)
            {
                document.ThermogramPanel = BuildThermogramPanel(data, options);
            }

            document.FitPanel = BuildFitPanel(data, options);

            if (options.ShowResiduals && data.Solution != null)
            {
                document.ResidualPanel = BuildResidualPanel(data, options, document.FitPanel.XAxis);
            }

            document.MetadataKeywords.AddRange(BuildMetadataKeywords(data, options));
            return document;
        }

        static PublicationFigurePanel BuildThermogramPanel(ExperimentData data, PublicationFigureOptions options)
        {
            var powerScale = PowerScale(options.EnergyUnit);
            var timeScale = options.TimeUnit.GetProperties().Mod;
            var points = GetThermogramPoints(data);

            var xMin = 0;
            var xMax = points.Count > 0 ? points.Max(point => point.X) : 1;
            var yRange = RangeWithBuffer(points.Select(point => point.Y), 0.1, includeZero: false);

            var panel = new PublicationFigurePanel
            {
                Kind = PublicationPanelKind.Thermogram,
                XAxis = new PublicationAxis(FormatAxisTitle(options.TimeAxisTitle, options.TimeUnit.GetProperties().Short), PublicationAxisPlacement.Top, xMin, xMax, options.DataXTickCount),
                YAxis = new PublicationAxis(FormatAxisTitle(options.PowerAxisTitle, options.EnergyUnit.IsSI() ? "µW" : "µcal/s"), PublicationAxisPlacement.Left, yRange[0], yRange[1], options.DataYTickCount)
            };

            panel.Series.Add(new PublicationSeries
            {
                Role = PublicationSeriesRole.Thermogram,
                Points = points
            });

            foreach (var injection in data.Injections)
            {
                panel.Markers.Add(new PublicationMarker(injection.Time * timeScale));
            }

            if (options.ShowExperimentDetails)
            {
                var box = BuildExperimentDetailsBox(data, options);
                if (box.Lines.Count > 0) panel.AnnotationBoxes.Add(box);
            }

            return panel;

            List<PublicationPoint> GetThermogramPoints(ExperimentData experiment)
            {
                var source = experiment.BaseLineCorrectedDataPoints != null && experiment.BaseLineCorrectedDataPoints.Count > 0
                    ? experiment.BaseLineCorrectedDataPoints
                    : experiment.DataPoints;

                return source
                    .Where(point => IsFinite(point.Time) && IsFinite(point.Power))
                    .Select(point => new PublicationPoint(point.Time * timeScale, point.Power * powerScale))
                    .ToList();
            }
        }

        static PublicationFigurePanel BuildFitPanel(ExperimentData data, PublicationFigureOptions options)
        {
            var xScale = GetAnalysisXAxisScale(data);
            var energyScale = Energy.ScaleFactor(options.EnergyUnit);
            var drawWithOffset = !options.DrawFitOffsetCorrected;
            var injectionPoints = new List<PublicationErrorPoint>();

            foreach (var injection in data.Injections)
            {
                if (!options.ShowBadData && !injection.Include) continue;

                var y = drawWithOffset ? injection.Enthalpy : injection.OffsetEnthalpy;
                var sd = options.ShowErrorBars && (options.ShowBadDataErrorBars || injection.Include) ? injection.SD : 0;

                if (!IsFinite(injection.Ratio) || !IsFinite(y)) continue;

                injectionPoints.Add(new PublicationErrorPoint
                {
                    X = injection.Ratio * xScale,
                    Y = y * energyScale,
                    LowerY = (y - sd) * energyScale,
                    UpperY = (y + sd) * energyScale,
                    Included = injection.Include
                });
            }

            var fitPoints = new List<PublicationPoint>();
            if (data.Solution != null && data.Model != null)
            {
                foreach (var injection in data.Injections)
                {
                    var y = data.Model.EvaluateEnthalpy(injection.ID, drawWithOffset);
                    if (IsFinite(injection.Ratio) && IsFinite(y))
                    {
                        fitPoints.Add(new PublicationPoint(injection.Ratio * xScale, y * energyScale));
                    }
                }
            }

            var values = injectionPoints.Select(point => point.Y).ToList();
            values.AddRange(injectionPoints.Select(point => point.LowerY));
            values.AddRange(injectionPoints.Select(point => point.UpperY));
            values.AddRange(fitPoints.Select(point => point.Y));
            values.Add(0);

            var xMax = data.Injections.Count > 0 ? data.Injections.Last().Ratio * xScale : 1;
            xMax = Math.Max(xMax, 1);
            var xRange = RangeWithBuffer(new[] { 0, xMax }, 0.05, includeZero: true);
            var yRange = RangeWithBuffer(values, 0.1, includeZero: true);
            var xAxisTitle = string.IsNullOrWhiteSpace(options.XAxisTitle)
                ? data.AxisType.GetEnumDescription()
                : options.XAxisTitle;

            var panel = new PublicationFigurePanel
            {
                Kind = PublicationPanelKind.Fit,
                XAxis = new PublicationAxis(xAxisTitle, PublicationAxisPlacement.Bottom, xRange[0], xRange[1], options.FitXTickCount),
                YAxis = new PublicationAxis(FormatAxisTitle(options.EnthalpyAxisTitle, options.EnergyUnit.GetUnit() + "/mol"), PublicationAxisPlacement.Left, yRange[0], yRange[1], options.FitYTickCount),
                DrawZeroLine = true
            };

            panel.Points.AddRange(injectionPoints);

            if (fitPoints.Count > 0)
            {
                panel.Series.Add(new PublicationSeries
                {
                    Role = PublicationSeriesRole.Fit,
                    Points = fitPoints.OrderBy(point => point.X).ToList()
                });
            }

            if (options.ShowConfidenceBand)
            {
                var band = BuildConfidenceBand(data, options, drawWithOffset, xScale, energyScale);
                if (band.Upper.Count > 0 && band.Lower.Count > 0) panel.Bands.Add(band);
            }

            if (options.ShowFitParameters && data.Solution != null)
            {
                var box = BuildFitParameterBox(data, options);
                if (box.Lines.Count > 0) panel.AnnotationBoxes.Add(box);
            }

            return panel;
        }

        static PublicationFigurePanel BuildResidualPanel(ExperimentData data, PublicationFigureOptions options, PublicationAxis parentXAxis)
        {
            var xScale = GetAnalysisXAxisScale(data);
            var energyScale = Energy.ScaleFactor(options.EnergyUnit);
            var points = new List<PublicationErrorPoint>();

            foreach (var injection in data.Injections)
            {
                if (!options.ShowBadData && !injection.Include) continue;

                var residual = injection.ResidualEnthalpy;
                var sd = options.ShowErrorBars && (options.ShowBadDataErrorBars || injection.Include) ? injection.SD : 0;

                if (!IsFinite(injection.Ratio) || !IsFinite(residual)) continue;

                points.Add(new PublicationErrorPoint
                {
                    X = injection.Ratio * xScale,
                    Y = residual * energyScale,
                    LowerY = (residual - sd) * energyScale,
                    UpperY = (residual + sd) * energyScale,
                    Included = injection.Include
                });
            }

            var max = points.Count == 0
                ? 1
                : 1.5 * Math.Max(points.SelectMany(point => new[]
                {
                    Math.Abs(point.Y),
                    Math.Abs(point.LowerY),
                    Math.Abs(point.UpperY)
                }).Max(), 1E-3);

            var panel = new PublicationFigurePanel
            {
                Kind = PublicationPanelKind.Residual,
                XAxis = new PublicationAxis(parentXAxis.Title, PublicationAxisPlacement.Bottom, parentXAxis.Minimum, parentXAxis.Maximum, options.FitXTickCount),
                YAxis = new PublicationAxis("", PublicationAxisPlacement.Left, -max, max, options.ResidualYTickCount, preferCenteredTicks: true),
                DrawZeroLine = true
            };

            panel.Points.AddRange(points);
            return panel;
        }

        static PublicationBand BuildConfidenceBand(ExperimentData data, PublicationFigureOptions options, bool drawWithOffset, double xScale, double energyScale)
        {
            var band = new PublicationBand();

            if (data.Solution?.BootstrapSolutions == null || data.Solution.BootstrapSolutions.Count == 0 || data.Model == null)
            {
                return band;
            }

            foreach (var injection in data.Injections)
            {
                var y = data.Model.EvaluateBootstrap(injection.ID, drawWithOffset).WithConfidence();
                if (!IsFinite(injection.Ratio) || y == null || y.Length < 2 || !IsFinite(y[0]) || !IsFinite(y[1])) continue;

                band.Upper.Add(new PublicationPoint(injection.Ratio * xScale, y[1] * energyScale));
                band.Lower.Add(new PublicationPoint(injection.Ratio * xScale, y[0] * energyScale));
            }

            band.Upper = band.Upper.OrderBy(point => point.X).ToList();
            band.Lower = band.Lower.OrderBy(point => point.X).ToList();
            return band;
        }

        static PublicationAnnotationBox BuildExperimentDetailsBox(ExperimentData data, PublicationFigureOptions options)
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

            if (options.DisplayParameters.HasFlag(FinalFigureDisplayParameters.Attributes) && data.Solution != null)
            {
                var attributes = data.Solution.UIExperimentModelAttributes(options.AttributeOptions);
                foreach (var attribute in attributes)
                {
                    var line = attribute.Item1;
                    if (!string.IsNullOrEmpty(attribute.Item2)) line += " = " + attribute.Item2;
                    box.Lines.Add(line);
                }
            }

            return box;
        }

        static PublicationAnnotationBox BuildFitParameterBox(ExperimentData data, PublicationFigureOptions options)
        {
            var box = new PublicationAnnotationBox
            {
                Placement = options.InformationBoxPlacement
            };

            var previousStyle = AppSettings.UncertaintyDisplayStyle;
            AppSettings.UncertaintyDisplayStyle = options.TextUncertaintyStyle;

            try
            {
                foreach (var parameter in data.Solution.UISolutionParameters(options.DisplayParameters))
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

        static List<string> BuildMetadataKeywords(ExperimentData data, PublicationFigureOptions options)
        {
            var keywords = new List<string>
            {
                $"Name: {data.Name}",
                $"File: {data.FileName}",
                $"File Date: {data.Date:yyyy-MM-dd}",
                $"[Syringe]: {data.SyringeConcentration.AsConcentration(ConcentrationUnit.µM, true)}",
                $"[Cell]: {data.CellConcentration.AsConcentration(ConcentrationUnit.µM, true)}",
                $"Model: {(data.Solution == null ? "" : data.Solution.ModelType.GetProperties()?.Name ?? data.Solution.ModelType.ToString())}",
                $"Solution: {(data.Solution == null ? "" : data.Solution.IsGlobalAnalysisSolution ? "Global" : "Single")}",
                $"Loss: {(data.Solution == null ? "" : data.Solution.Loss.ToString("G3"))}"
            };

            if (data.Solution == null) return keywords.Select(keyword => keyword.Replace(",", "..")).ToList();

            foreach (var parameter in data.Solution.UISolutionParameters(options.DisplayParameters))
            {
                keywords.Add($"{parameter.Item1} = {parameter.Item2}");
            }

            return keywords.Select(keyword => keyword.Replace(",", "..")).ToList();
        }

        static double GetAnalysisXAxisScale(ExperimentData data)
        {
            return data.AxisType == AnalysisXAxisType.TitrantConcentration ? 1000000 : 1;
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

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
