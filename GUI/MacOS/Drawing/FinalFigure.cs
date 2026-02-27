using System;
using AppKit;
using CoreGraphics;
using static AnalysisITC.AppClasses.Analysis2.Models.SolutionInterface;

namespace AnalysisITC
{
    public class FinalFigure
    {
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

        public GraphBase.LineSmoothness LineSmoothness
        {
            get => IntegrationGraph.FitLineSmoothnessSetting;
            set => IntegrationGraph.FitLineSmoothnessSetting = value;
        }

        public bool MirrorDataGraphAxisUnification
        {
            get => IntegrationGraph.ResidualDisplayOptions.GapGraphs;
            set => IntegrationGraph.ResidualDisplayOptions.GapGraphs = value;
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

        public float SymbolSize
        {
            get => CGGraph.SymbolSize;
            set => CGGraph.SymbolSize = value;
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
        public bool SanitizeTicks
        {
            get => sanitizeticks;
            set
            {
                sanitizeticks = value;

                if (DataGraph != null) DataGraph.XAxis.HideUnwantedTicks = sanitizeticks;
                if (DataGraph != null) DataGraph.YAxis.HideUnwantedTicks = sanitizeticks;
                IntegrationGraph.XAxis.HideUnwantedTicks = sanitizeticks;
                IntegrationGraph.YAxis.HideUnwantedTicks = sanitizeticks;
            }
        }

        public string PowerAxisTitle { get; set; } = "Differential Power (<unit>)";
        public string TimeAxisTitle { get; set; } = "Time (<unit>)";
        public string EnthalpyAxisTitle { get; set; } = "<unit> of injectant";
        public string MolarRatioAxisTitle { get; set; } = "Molar Ratio";

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

        #endregion

        public FinalFigure(ExperimentData experiment, NSView view)
        {
            View = view;

            if (experiment.HasThermogram)
            {
                DataGraph = new BaselineDataGraph(experiment, view)
                {
                    DrawOnWhite = true,
                    ShowBaselineCorrected = true
                };
                DataGraph.YAxis.Buffer = .1f;
                DataGraph.YAxis.MirrorTicks = true;
                DataGraph.XAxis.Buffer = .1f;
                DataGraph.XAxis.ValueFactor = 1.0 / 60;
                DataGraph.XAxis.LegendTitle = "Time (min)";
                DataGraph.XAxis.Position = AxisPosition.Top;
                DataGraph.ShowBaseline = ShouldDrawBaseline;
            }

            IntegrationGraph = new DataFittingGraph(experiment, view)
            {
                DrawOnWhite = true,
                ShowGrid = false,
                ShowErrorBars = true,
                HideBadDataErrorBars = true,
                ShowPeakInfo = false,
                DrawWithOffset = false,
            };
            IntegrationGraph.YAxis.MirrorTicks = true;
            IntegrationGraph.XAxis.MirrorTicks = true;
            IntegrationGraph.ResidualDisplayOptions.GapGraphs = true;
            IntegrationGraph.ResidualGraph.MirrorAxisUnification = MirrorDataGraphAxisUnification;
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

        public void Draw(CGContext gc, CGPoint center)
        {
            if (DataGraph != null) DataGraph.Center = center;
            IntegrationGraph.Center = center;

            SetupFrames(PlotDimensions.Width, PlotDimensions.Height, center);
            UpdateAxisTitles();

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