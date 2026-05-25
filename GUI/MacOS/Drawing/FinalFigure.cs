using System;
using AnalysisITC.AppClasses.AnalysisClasses.Models;
using AppKit;
using CoreGraphics;
using static AnalysisITC.AppClasses.AnalysisClasses.Models.SolutionInterface;

namespace AnalysisITC
{
    public class FinalFigure
    {
        ExperimentData Data;
        BaselineDataGraph DataGraph;
        DataFittingGraph IntegrationGraph;
        NSView View;

        public CGSize PlotDimensions { get; set; } = new CGSize(6, 10);
        CGEdgeMargin Margin
        {
            get
            {
                if (DataGraph != null)
                {
                    var max_margin_left = (float)Math.Max(DataGraph.YAxis.EstimateLabelMargin(), IntegrationGraph.YAxis.EstimateLabelMargin());

                    return new CGEdgeMargin(
                        max_margin_left,
                        0.1f * CGGraph.PPcm,
                        (float)DataGraph.XAxis.EstimateLabelMargin(),
                        (float)IntegrationGraph.XAxis.EstimateLabelMargin());
                }
                else
                {
                    return new CGEdgeMargin(
                        (float)IntegrationGraph.YAxis.EstimateLabelMargin(),
                        0.1f * CGGraph.PPcm,
                        0.1f * CGGraph.PPcm,
                        (float)IntegrationGraph.XAxis.EstimateLabelMargin());
                }
            }
        }
        CGSize ActualPlotBackground => new CGSize(PlotDimensions.Width, DataGraph != null ? PlotDimensions.Height : PlotDimensions.Height / 2);
        CGRect PlotBox => new CGRect(IntegrationGraph.GetCompositeOrigin(), ActualPlotBackground.ScaleBy(CGGraph.PPcm));

        public CGRect PrintBox => PlotBox.WithMargin(Margin);

        #region Properties

        public bool ShowDataGraph { get; set; } = true;

        public bool AutoAxesIgnoresBadData
        {
            get => IntegrationGraph.AutoAxesFocusesIncludedOnly;
            set => IntegrationGraph.AutoAxesFocusesIncludedOnly = value;
        }

        public GraphBase.LineSmoothness LineSmoothness
        {
            get => IntegrationGraph.FitLineSmoothnessSetting;
            set => IntegrationGraph.FitLineSmoothnessSetting = value;
        }

        public bool IncludeResidualGraphGap
        {
            get => IntegrationGraph.ResidualDisplayOptions.GapGraphs;
            set => IntegrationGraph.ResidualDisplayOptions.GapGraphs = value;
        }

        public bool ShowResiduals
        {
            get => IntegrationGraph.ResidualDisplayOptions.ShowResidualGraph;
            set => IntegrationGraph.ResidualDisplayOptions.ShowResidualGraph = value;
        }

        public bool DrawConfidence
        {
            get => IntegrationGraph.DrawConfidenceBands;
            set => IntegrationGraph.DrawConfidenceBands = value;
        }

        public bool DrawFitOffsetCorrected
        {
            get => !IntegrationGraph.DrawWithOffset;
            set => IntegrationGraph.DrawWithOffset = !value;
        }

        public bool ShowBadDataPoints
        {
            get => IntegrationGraph.HideBadData;
            set => IntegrationGraph.HideBadData = !value;
        }

        public bool ShowErrorBars
        {
            get => IntegrationGraph.ShowErrorBars;
            set => IntegrationGraph.ShowErrorBars = value;
        }

        public bool ShowBadDataErrorBars
        {
            get => IntegrationGraph.HideBadDataErrorBars;
            set => IntegrationGraph.HideBadDataErrorBars = !value;
        }

        public bool DrawZeroLine
        {
            get => IntegrationGraph.ShowZero;
            set => IntegrationGraph.ShowZero = value;
        }

        public bool UnifiedEnthalpyAxis
        {
            get => IntegrationGraph.UnifiedEnthalpyAxis;
            set => IntegrationGraph.UnifiedEnthalpyAxis = value;
        }

        public bool UseUnifiedAnalysisAxes
        {
            get => IntegrationGraph.UnifiedMolarRatioAxis;
            set => IntegrationGraph.UnifiedMolarRatioAxis = value;
        }

        public bool UseUnifiedDataAxes
        {
            get => DataGraph != null && DataGraph.UseUnifiedAxes;
            set
            {
                if (DataGraph != null)
                {
                    DataGraph.UseUnifiedAxes = value;
                }
            }
        }

        public bool DrawFitParameters
        {
            get => IntegrationGraph.ShowFitParameters;
            set => IntegrationGraph.ShowFitParameters = value;
        }

        public bool DrawExpDetails
        {
            get => DataGraph != null && DataGraph.ShowExperimentDetails;
            set
            {
                if (DataGraph != null) { DataGraph.ShowExperimentDetails = value; }
            }
        }

        public FinalFigureDisplayParameters FinalFigureDisplayParameters
        {
            get => IntegrationGraph.FinalFigureDisplayParameters;
            set
            {
                IntegrationGraph.FinalFigureDisplayParameters = value;
                if (DataGraph != null) DataGraph.FinalFigureDisplayParameters = value;
            }
        }

        public InformationBoxPlacement InformationBoxPlacement
        {
            get => IntegrationGraph.InformationBoxPlacement;
            set
            {
                IntegrationGraph.InformationBoxPlacement = value;
                if (DataGraph != null) DataGraph.InformationBoxPlacement = value;
            }
        }

        public float SymbolSize
        {
            get => IntegrationGraph.InjectionSymbolSize;
            set => IntegrationGraph.InjectionSymbolSize = value;
        }

        public bool ShouldDrawBaseline
        {
            get => DataGraph != null && DataGraph.ShowBaseline;
            set
            {
                if (DataGraph != null)
                {
                    DataGraph.ShowBaseline = value;
                }
            }
        }

        public bool DrawBaselineCorrected
        {
            get => DataGraph != null && DataGraph.ShowBaselineCorrected;
            set
            {
                if (DataGraph != null)
                {
                    DataGraph.ShowBaselineCorrected = value;
                }
            }
        }

        public CGGraph.SymbolShape SymbolShape
        {
            get => IntegrationGraph.InjectionSymbolShape;
            set => IntegrationGraph.InjectionSymbolShape = value;
        }

        bool sanitizeticks = true;
        bool useNameAttributes = false;
        string syringeName = "";
        string cellName = "";
        FinalFigureNameDisplayMode nameDisplayMode = FinalFigureNameDisplayMode.Name;
        public bool SanitizeTicks
        {
            get => sanitizeticks;
            set
            {
                sanitizeticks = value;

                if (DataGraph != null) DataGraph.XAxis.HideUnwantedTicks = sanitizeticks;
                if (DataGraph != null) DataGraph.YAxis.HideUnwantedTicks = false; // Never hide these
                IntegrationGraph.XAxis.HideUnwantedTicks = sanitizeticks;
                IntegrationGraph.YAxis.HideUnwantedTicks = false; // Never hide these
            }
        }

        public string PowerAxisTitle { get; set; } = "Differential Power (<unit>)";
        public string TimeAxisTitle { get; set; } = "Time (<unit>)";
        public string EnthalpyAxisTitle { get; set; } = "<unit> of injectant";
        public string MolarRatioAxisTitle { get; set; } = "Molar Ratio";
        public bool ShowAxisTitles { get; set; } = true;
        public double? DataXAxisMin { get; set; }
        public double? DataXAxisMax { get; set; }
        public double? DataYAxisMin { get; set; }
        public double? DataYAxisMax { get; set; }
        public double? FitXAxisMin { get; set; }
        public double? FitXAxisMax { get; set; }
        public double? FitYAxisMin { get; set; }
        public double? FitYAxisMax { get; set; }

        public string SyringeName
        {
            get => syringeName;
            set
            {
                syringeName = value ?? "";
                if (DataGraph != null) DataGraph.SyringeName = syringeName;
            }
        }

        public string CellName
        {
            get => cellName;
            set
            {
                cellName = value ?? "";
                if (DataGraph != null) DataGraph.CellName = cellName;
            }
        }

        public bool UseNameAttributes
        {
            get => useNameAttributes;
            set
            {
                useNameAttributes = value;
                if (DataGraph != null) DataGraph.UseNameAttributes = useNameAttributes;
            }
        }

        public FinalFigureNameDisplayMode NameDisplayMode
        {
            get => nameDisplayMode;
            set
            {
                nameDisplayMode = value;
                if (DataGraph != null) DataGraph.NameDisplayMode = nameDisplayMode;
            }
        }

        public EnergyUnit EnergyUnit { get; set; } = EnergyUnit.KiloJoule;
        public TimeUnit TimeUnit { get; set; } = TimeUnit.Minute;

        public void SetTimeUnit(TimeUnit unit)
        {
            if (DataGraph != null) DataGraph.XAxis.ValueFactor = unit.GetProperties().Mod;

            TimeUnit = unit;
        }

        public void SetEnergyUnit(EnergyUnit unit)
        {
            EnergyUnit = unit;

            if (DataGraph != null) DataGraph.YAxis.ValueFactor = unit.IsSI() ? 1000000 : 1000000 * Energy.JouleToCalFactor;
            IntegrationGraph.YAxis.ValueFactor = unit.IsSI() ? 0.001 : 0.001 * Energy.JouleToCalFactor;
        }

        public void SetTickNumber(int datax, int datay, int fitx, int fity)
        {
            if (DataGraph != null) DataGraph.YAxis.SetMaxTicks(datay);
            if (DataGraph != null) DataGraph.XAxis.SetMaxTicks(datax);
            IntegrationGraph.YAxis.SetMaxTicks(fity);
            IntegrationGraph.XAxis.SetMaxTicks(fitx);
            IntegrationGraph.ResidualGraph.XAxis.SetMaxTicks(fitx);
        }

        public void SetShowDataGraph(bool show)
        {
            if (show && DataGraph == null) CreateDataGraph();
            else if (!show) DataGraph = null;
        }

        #endregion

        public FinalFigure(ExperimentData experiment, NSView view)
        {
            View = view;
            Data = experiment;

            if (experiment.HasThermogram && ShowDataGraph)
            {
                CreateDataGraph();
            }

            CreateIntegrationGraph();
        }

        void CreateDataGraph()
        {
            if (!Data.HasThermogram) return;

            DataGraph = new BaselineDataGraph(Data, View)
            {
                DrawOnWhite = true,
                ShowBaselineCorrected = true,
                SyringeName = syringeName,
                CellName = cellName,
                UseNameAttributes = useNameAttributes,
                NameDisplayMode = nameDisplayMode,
            };
            DataGraph.YAxis.Buffer = .1f;
            DataGraph.YAxis.MirrorTicks = true;
            DataGraph.YAxis.HideUnwantedTicks = false;
            DataGraph.XAxis.Buffer = .1f;
            DataGraph.XAxis.ValueFactor = 1.0 / 60;
            DataGraph.XAxis.LegendTitle = "Time (min)";
            DataGraph.XAxis.Position = AxisPosition.Top;
            DataGraph.ShowBaseline = ShouldDrawBaseline;
        }

        void CreateIntegrationGraph()
        {
            IntegrationGraph = new DataFittingGraph(Data, View)
            {
                DrawOnWhite = true,
                ShowGrid = false,
                ShowErrorBars = true,
                HideBadDataErrorBars = true,
                ShowPeakInfo = false,
                DrawWithOffset = false,
            };
            IntegrationGraph.YAxis.HideUnwantedTicks = false;
            IntegrationGraph.ResidualDisplayOptions.GapGraphs = true;
        }

        public void SetupFrames(nfloat width, nfloat height, CGPoint center)
        {
            var halfheight = height / 2;
            var x = Margin.Left;

            if (DataGraph != null)
            {
                DataGraph.PlotSize = new CGSize(width * CGGraph.PPcm, halfheight * CGGraph.PPcm);
                DataGraph.Origin = new CGPoint(x, center.Y);
            }

            IntegrationGraph.PlotSize = new CGSize(width * CGGraph.PPcm, halfheight * CGGraph.PPcm);

            var oriY = (DataGraph == null) ? Margin.Bottom : (center.Y - IntegrationGraph.Frame.Height);

            IntegrationGraph.SetFrame(
                new CGSize(width * CGGraph.PPcm, halfheight * CGGraph.PPcm),
                new CGPoint(x, oriY)
                );
        }

        public void UpdateAxisTitles()
        {
            if (!string.IsNullOrEmpty(TimeAxisTitle) && DataGraph != null)
                DataGraph.XAxis.LegendTitle = TimeAxisTitle.Replace("<unit>", TimeUnit.GetProperties().Short);

            if (!string.IsNullOrEmpty(PowerAxisTitle) && DataGraph != null)
                DataGraph.YAxis.LegendTitle = PowerAxisTitle.Replace("<unit>", EnergyUnit.IsSI() ? "µW" : "µcal/s");

            if (!string.IsNullOrEmpty(EnthalpyAxisTitle))
                IntegrationGraph.YAxis.LegendTitle = EnthalpyAxisTitle.Replace("<unit>", EnergyUnit.IsSI() ? "kJ/mol" : "kcal/mol");

            if (!string.IsNullOrEmpty(MolarRatioAxisTitle))
                IntegrationGraph.XAxis.LegendTitle = MolarRatioAxisTitle;
        }

        void ApplyAxisTitleVisibility()
        {
            SetAxisTitleVisibility(DataGraph?.XAxis);
            SetAxisTitleVisibility(DataGraph?.YAxis);
            SetAxisTitleVisibility(IntegrationGraph.XAxis);
            SetAxisTitleVisibility(IntegrationGraph.YAxis);
            SetAxisTitleVisibility(IntegrationGraph.ResidualGraph?.XAxis);
            SetAxisTitleVisibility(IntegrationGraph.ResidualGraph?.YAxis);
        }

        void SetAxisTitleVisibility(GraphAxis axis)
        {
            if (axis == null) return;

            axis.HideTitle = !ShowAxisTitles;
        }

        void ApplyManualAxisRanges()
        {
            if (DataGraph != null)
            {
                ApplyManualAxisRange(DataGraph.XAxis, DataXAxisMin, DataXAxisMax);
                ApplyManualAxisRange(DataGraph.YAxis, DataYAxisMin, DataYAxisMax);
            }

            ApplyManualAxisRange(IntegrationGraph.XAxis, FitXAxisMin, FitXAxisMax);
            ApplyManualAxisRange(IntegrationGraph.YAxis, FitYAxisMin, FitYAxisMax);
        }

        void ApplyManualAxisRange(GraphAxis axis, double? displayMin, double? displayMax)
        {
            if (axis == null || (!displayMin.HasValue && !displayMax.HasValue)) return;

            var min = displayMin.HasValue ? displayMin.Value / axis.ValueFactor : axis.Min;
            var max = displayMax.HasValue ? displayMax.Value / axis.ValueFactor : axis.Max;

            if (!IsFinite(min) || !IsFinite(max) || max <= min) return;

            axis.Set(min, max);
        }

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public void Draw(CGContext gc, CGPoint center)
        {
            if (DataGraph != null) DataGraph.Center = center;
            IntegrationGraph.Center = center;

            UpdateAxisTitles();
            ApplyAxisTitleVisibility();
            ApplyManualAxisRanges();
            SetupFrames(PlotDimensions.Width, PlotDimensions.Height, center);

            gc.SetFillColor(NSColor.White.CGColor);
            gc.FillRect(PlotBox.WithMargin(Margin));

            DataGraph?.SetupAxisScalingUnits();
            IntegrationGraph.SetupAxisScalingUnits();

            DataGraph?.Draw(gc);
            IntegrationGraph.Draw(gc);

            DataGraph?.DrawFrame(gc);
            IntegrationGraph.DrawFrame(gc);
        }
    }
}
