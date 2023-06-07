using System;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using AppKit;
using CoreGraphics;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC
{
	public partial class ResultGraphView : NSGraph
	{
        ResultGraphType Type { get; set; }

        new GraphBase Graph { get; set; }

        #region Constructors

        // Called when created from unmanaged code
        public ResultGraphView (IntPtr handle) : base (handle)
		{
			Initialize ();
		}

		// Shared initialization code
		void Initialize ()
		{
            AppSettings.SettingsDidUpdate += (s, e) => Invalidate();
        }

        #endregion

        public void Setup(ResultGraphType type, AnalysisResult result)
        {
            Type = type;

            switch (Type)
            {
                case ResultGraphType.Parameters: Graph = new ThermodynamicParameterBarPlot(result, this); break;
                case ResultGraphType.TemperatureDependence: Graph = new TemperatureDependenceGraph(result, this); break;
            }

            Invalidate();
        }

        public void Setup(ProtonationAnalysis analysis)
        {
            Type = ResultGraphType.ProtonationAnalysis;

            Graph = new ParameterDependenceGraph(this)
            {
                YLabel = "∆*H*{obs} (" + AppSettings.EnergyUnit.GetUnit() + ")",
                XLabel = "∆*H*{buffer,protonation} (" + AppSettings.EnergyUnit.GetUnit() + ")",
                XValues = analysis.DataPoints.Select(dp => new FloatWithError(dp.Item1)).ToArray(),
                YValues = analysis.DataPoints.Select(dp => dp.Item2).ToArray(),
                XScaleFactor = Energy.ScaleFactor(AppSettings.EnergyUnit),
                YScaleFactor = Energy.ScaleFactor(AppSettings.EnergyUnit),
                Fit = analysis.Fit,
            };

            (Graph as ParameterDependenceGraph).Setup();

            Invalidate();
        }

        public void Setup(ElectrostaticsAnalysis analysis)
        {
            Type = ResultGraphType.ProtonationAnalysis;
            var unit = ConcentrationUnitAttribute.FromConc(analysis.DataPoints.Select(dp => dp.Item2.Value).Average());

            if (analysis.Mode == ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease)
            {
                Graph = new ParameterDependenceGraph(this)
                {
                    XLabel = "ln(*a*{salt})",
                    YLabel = "ln(*K*{a})",
                    XValues = analysis.DataPoints.Select(dp => new FloatWithError(dp.Item1)).ToArray(),
                    YValues = analysis.DataPoints.Select(dp => dp.Item2).ToArray(),
                    XScaleFactor = 1,
                    YScaleFactor = 1,
                    Fit = analysis.CounterIonReleaseFit,
                };

                (Graph as ParameterDependenceGraph).Setup();
            }
            else
            {
                Graph = new ParameterDependenceGraph(this)
                {
                    XLabel = "*Ionic Strength* (mM)",
                    YLabel = "*K*{d} (" + unit.GetName() + ")",
                    XValues = analysis.DataPoints.Select(dp => new FloatWithError(dp.Item1)).ToArray(),
                    YValues = analysis.DataPoints.Select(dp => dp.Item2).ToArray(),
                    XScaleFactor = ConcentrationUnit.mM.GetMod(),
                    YScaleFactor = unit.GetMod(),
                    Fit = analysis.DebyeHuckelFit,
                };

                (Graph as ParameterDependenceGraph).Setup();
                Graph.XAxis.Min = 0;
                Graph.XAxis.ValueFactor = ConcentrationUnit.mM.GetMod(); //should not be necessary
            }

            Invalidate();
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            base.DrawRect(dirtyRect);

            var cg = NSGraphicsContext.CurrentContext.CGContext;

            Graph?.PrepareDraw(cg, new CGPoint(Frame.GetMidX(), Frame.GetMidY()));
        }

        new public void Print()
        {
            var _drawOnWhite = Graph.DrawOnWhite;
            Graph.DrawOnWhite = true;

            Invalidate();

            var op = NSPrintOperation.FromView(this);
            op.PrintInfo.PaperSize = this.Frame.Size;
            op.PrintInfo.BottomMargin = 0;
            op.PrintInfo.TopMargin = 0;
            op.PrintInfo.LeftMargin = 0;
            op.PrintInfo.RightMargin = 0;
            op.PrintInfo.ScalingFactor = 1;
            op.RunOperation();

            Graph.DrawOnWhite = _drawOnWhite;
        }

        public enum ResultGraphType
        {
            Parameters,
            TemperatureDependence,
            IonicStrengthDependence,
            ProtonationAnalysis,
        }
    }
}
