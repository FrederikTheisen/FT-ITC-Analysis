using System;
using AppKit;
using CoreGraphics;

namespace AnalysisITC
{
    public class FinalFigure
    {
        BaselineDataGraph DataGraph;
        DataFittingGraph IntegrationGraph;

        public CGSize PlotDimensions { get; set; } = new CGSize(6, 10);
        CGEdgeMargin Margin
        {
            get
            {
                var max_margin_left = (float)Math.Max(DataGraph.YAxis.EstimateLabelMargin(), IntegrationGraph.YAxis.EstimateLabelMargin());

                return new CGEdgeMargin(max_margin_left, 0.1f * CGGraph.PPcm, (float)DataGraph.XAxis.EstimateLabelMargin(), (float)IntegrationGraph.XAxis.EstimateLabelMargin());
            }
        }
        CGRect PlotBox => new CGRect(IntegrationGraph.Origin, PlotDimensions.ScaleBy(CGGraph.PPcm));
        CGPoint UnadjustedGraphOrigin => new CGPoint(DataGraph.Center.X - DataGraph.PlotSize.Width * 0.5f, DataGraph.Center.Y - DataGraph.PlotSize.Height * 0.5f);

        public CGRect PrintBox => PlotBox.WithMargin(Margin);

        #region Properties

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

        public bool UseUnifiedAnalysisAxes
        {
            get => IntegrationGraph.UseUnifiedAxes;
            set => IntegrationGraph.UseUnifiedAxes = value;
        }

        public bool UseUnifiedDataAxes
        {
            get => DataGraph.UseUnifiedAxes;
            set => DataGraph.UseUnifiedAxes = value;
        }

        public bool DrawFitParameters
        {
            get => IntegrationGraph.ShowFitParameters;
            set => IntegrationGraph.ShowFitParameters = value;
        }

        public float SymbolSize
        {
            get => CGGraph.SymbolSize;
            set => CGGraph.SymbolSize = value;
        }

        public bool ShouldDrawBaseline
        {
            get => DataGraph.ShowBaseline;
            set => DataGraph.ShowBaseline = value;
        }

        public CGGraph.SymbolShape SymbolShape
        {
            get => IntegrationGraph.SymbolShape;
            set => IntegrationGraph.SymbolShape = value;
        }

        bool sanitizeticks = true;
        public bool SanitizeTicks
        {
            get => sanitizeticks;
            set
            {
                sanitizeticks = value;

                DataGraph.XAxis.HideUnwantedTicks = sanitizeticks;
                DataGraph.YAxis.HideUnwantedTicks = sanitizeticks;
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
            DataGraph.XAxis.ValueFactor = unit.GetProperties().Mod;

            TimeUnit = unit;
        }

        public void SetEnergyUnit(EnergyUnit unit)
        {
            EnergyUnit = unit;

            DataGraph.YAxis.ValueFactor = unit.IsSI() ? 1000000 : 1000000 * Energy.JouleToCalFactor;
            IntegrationGraph.YAxis.ValueFactor = unit.IsSI() ? 0.001 : 0.001 * Energy.JouleToCalFactor;
        }

        public void SetTickNumber(int datax, int datay, int fitx, int fity)
        {
            DataGraph.YAxis.SetMaxTicks(datay);
            DataGraph.XAxis.SetMaxTicks(datax);
            IntegrationGraph.YAxis.SetMaxTicks(fity);
            IntegrationGraph.XAxis.SetMaxTicks(fitx);
        }

        #endregion

        public FinalFigure(ExperimentData experiment, NSView view)
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

            IntegrationGraph = new DataFittingGraph(experiment, view)
            {
                DrawOnWhite = true,
                ShowGrid = false,
                ShowErrorBars = true,
                HideBadDataErrorBars = true,
                ShowPeakInfo = false,
            };
            IntegrationGraph.YAxis.MirrorTicks = true;
            IntegrationGraph.XAxis.MirrorTicks = true;
        }

        public void SetupFrames(nfloat width, nfloat height)
        {
            var halfheight = height / 2;

            var x = DataGraph.Center.X - PlotBox.Width * 0.5f + Margin.Left * 0.5f - Margin.Right * 0.5f;

            //DataGraph.AutoSetFrame((float)width, (float)halfheight);
            DataGraph.PlotSize = new CGSize(width * CGGraph.PPcm, halfheight * CGGraph.PPcm);
            DataGraph.Origin = new CGPoint(DataGraph.Center.X - DataGraph.PlotSize.Width * 0.5f, DataGraph.Center.Y - DataGraph.PlotSize.Height * 0.5f);
            DataGraph.Origin.Y += DataGraph.Frame.Height / 2;
            DataGraph.Origin.X = x;

            //IntegrationGraph.AutoSetFrame((float)width, (float)halfheight);
            IntegrationGraph.PlotSize = new CGSize(width * CGGraph.PPcm, halfheight * CGGraph.PPcm);
            IntegrationGraph.Origin = new CGPoint(IntegrationGraph.Center.X - IntegrationGraph.PlotSize.Width * 0.5f, DataGraph.Center.Y - DataGraph.PlotSize.Height * 0.5f);
            IntegrationGraph.Origin.Y -= IntegrationGraph.Frame.Height / 2;
            IntegrationGraph.Origin.X = x;
        }

        public void UpdateAxisTitles()
        {
            if (!string.IsNullOrEmpty(TimeAxisTitle)) DataGraph.XAxis.LegendTitle = TimeAxisTitle.Replace("<unit>", TimeUnit.GetProperties().Short);
            if (!string.IsNullOrEmpty(PowerAxisTitle)) DataGraph.YAxis.LegendTitle = PowerAxisTitle.Replace("<unit>", EnergyUnit.IsSI() ? "µW" : "µCal/s");
            if (!string.IsNullOrEmpty(EnthalpyAxisTitle)) IntegrationGraph.YAxis.LegendTitle = EnthalpyAxisTitle.Replace("<unit>", EnergyUnit.GetUnit() + " mol⁻¹");
            if (!string.IsNullOrEmpty(MolarRatioAxisTitle)) IntegrationGraph.XAxis.LegendTitle = MolarRatioAxisTitle;
        }

        public void Draw(CGContext gc, CGPoint center)
        {
            DataGraph.Center = center;
            IntegrationGraph.Center = center;

            SetupFrames(PlotDimensions.Width, PlotDimensions.Height);
            UpdateAxisTitles();

            gc.SetFillColor(NSColor.White.CGColor);
            gc.FillRect(PlotBox.WithMargin(Margin));

            DataGraph.SetupAxisScalingUnits();
            IntegrationGraph.SetupAxisScalingUnits();

            DataGraph.Draw(gc);
            IntegrationGraph.Draw(gc);

            DataGraph.DrawFrame(gc);
            IntegrationGraph.DrawFrame(gc);
        }
    }
}
