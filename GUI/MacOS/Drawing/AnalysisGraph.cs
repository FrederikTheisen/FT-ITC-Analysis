using System;
using AppKit;
using CoreGraphics;
using Utilities;

namespace AnalysisITC.GUI.MacOS.Drawing
{
    public class AnalysisGraph : CGGraph
    {
        bool sanitizeticks = true;

        public DataFittingGraph FitGraph;
        public ResidualGraph ResidualGraph;

        public bool ShouldShowResidualGraph { get; set; } = true;
        public float ResidualPlotFraction { get; set; } = 0.2f;
        public bool IncludeResidualGap { get; set; } = true;

        public EnergyUnit EnergyUnit { get; set; } = EnergyUnit.KiloJoule;

        public bool DrawConfidenceBands
        {
            get => FitGraph.DrawConfidenceBands;
            set => FitGraph.DrawConfidenceBands = value;
        }
        public bool HideBadData
        {
            get => FitGraph.HideBadData;
            set => FitGraph.HideBadData = !value;
        }
        public bool ShowErrorBars
        {
            get => FitGraph.ShowErrorBars;
            set => FitGraph.ShowErrorBars = value;
        }
        public bool ShowBadDataErrorBars
        {
            get => FitGraph.HideBadDataErrorBars;
            set => FitGraph.HideBadDataErrorBars = !value;
        }
        public bool DrawZeroLine
        {
            get => FitGraph.ShowZero;
            set => FitGraph.ShowZero = value;
        }
        public bool UnifiedEnthalpyAxis
        {
            get => FitGraph.UnifiedEnthalpyAxis;
            set => FitGraph.UnifiedEnthalpyAxis = value;
        }
        public bool UnifiedMolarRatioAxis
        {
            get => FitGraph.UnifiedMolarRatioAxis;
            set => FitGraph.UnifiedMolarRatioAxis = value;
        }
        public bool ShowFitParameters
        {
            get => FitGraph.ShowFitParameters;
            set => FitGraph.ShowFitParameters = value;
        }
        public SymbolShape InjectionSymbolShape
        {
            get => FitGraph.InjectionSymbolShape;
            set => FitGraph.InjectionSymbolShape = value;
        }
        public bool SanitizeTicks
        {
            get => sanitizeticks;
            set
            {
                sanitizeticks = value;

                FitGraph.XAxis.HideUnwantedTicks = sanitizeticks;
                FitGraph.YAxis.HideUnwantedTicks = sanitizeticks;
                ResidualGraph.XAxis.HideUnwantedTicks = sanitizeticks;
                ResidualGraph.YAxis.HideUnwantedTicks = sanitizeticks;
            }
        }
        public bool ShowPeakInfo
        {
            get => FitGraph.ShowPeakInfo;
            set
            {
                // Requires setting for both graphs perhaps, or I inherit from the attached fit graph???
                FitGraph.ShowPeakInfo = value;
            }
        }
        public bool DrawOnWhite => FitGraph.DrawOnWhite;

        public override void AutoSetFrame()
        {
            FitGraph.AutoSetFrame();
            ResidualGraph.AutoSetFrame();
        }

        public override void PrepareDraw(CGContext gc, CGPoint center)
        {
            FitGraph.PrepareDraw(gc, center);
            ResidualGraph.PrepareDraw(gc, center);

            // Clamp residual fraction to sensible limits
            float frac = ResidualPlotFraction;
            if (frac < 0.05f) frac = 0.05f;
            if (frac > 0.5f) frac = 0.5f;
            if (!ShouldShowResidualGraph) frac = 0.0f;

            // Compute heights in pixels for the residual and fitting panels. A small gap may be
            // inserted between panels if requested. The gap height is a percentage of the total
            // available height, capped to a reasonable range.
            float totalHeight = (float)Frame.Height;
            float totalWidth = (float)Frame.Width;
            float gapHeight = 0f;
            if (IncludeResidualGap && ShouldShowResidualGraph)
            {
                gapHeight = 10f;
            }
            float availableHeight = totalHeight - gapHeight;
            float residualHeight = availableHeight * frac;
            float fitHeight = availableHeight - residualHeight;

            if (!ShouldShowResidualGraph) fitHeight = totalHeight;

            // Update the fitting graph's frame. Hide its X-axis to avoid duplication; the
            // residual graph will draw the shared X-axis.
            FitGraph.PlotSize = new CGSize(totalWidth, fitHeight);
            FitGraph.Origin = new CGPoint(Frame.X, Frame.Y + residualHeight + gapHeight);
            FitGraph.XAxis.Hidden = ShouldShowResidualGraph;
            FitGraph.YAxis.Hidden = false;
            FitGraph.SetupAxisScalingUnits();
            FitGraph.Draw(gc);

            if (ShouldShowResidualGraph)
            {
                // Update the residual graph's frame. Draw both axes for the residual plot. It will
                // update its Y-axis range based on the current residuals.
                ResidualGraph.PlotSize = new CGSize(totalWidth, residualHeight);
                ResidualGraph.Origin = new CGPoint(Frame.X, Frame.Y);
                ResidualGraph.XAxis.Hidden = false;
                ResidualGraph.YAxis.Hidden = false;
                ResidualGraph.SetupAxisScalingUnits();
                ResidualGraph.Draw(gc);
            }
        }

        public void SetEnergyUnit(EnergyUnit unit)
        {
            EnergyUnit = unit;

            FitGraph.YAxis.ValueFactor = unit.IsSI() ? 0.001 : 0.001 * Energy.JouleToCalFactor;
        }

        public void SetTickNumber(int datax, int datay, int fitx, int fity)
        {
            FitGraph.YAxis.SetMaxTicks(fity);
            FitGraph.XAxis.SetMaxTicks(fitx);
        }

        public AnalysisGraph(ExperimentData data, NSView view) : base(data, view)
		{
            FitGraph = new DataFittingGraph(data, view)
            {
                DrawOnWhite = false,
                ShowGrid = false,
                ShowErrorBars = true,
                HideBadDataErrorBars = true,
                ShowPeakInfo = false,
                DrawWithOffset = false,
            };
            FitGraph.YAxis.MirrorTicks = true;
            FitGraph.XAxis.MirrorTicks = true;

            ResidualGraph = new ResidualGraph(FitGraph);
        }

        internal override void Draw(CGContext gc)
        {
            return;
        }

        public override MouseOverFeatureEvent CursorFeatureFromPos(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
        {
            var evt = FitGraph.CursorFeatureFromPos(cursorpos, isclick, ismouseup);
            if (evt.IsMouseOverFeature) return evt;
            return ResidualGraph.CursorFeatureFromPos(cursorpos, isclick, ismouseup);
        }
    }
}

