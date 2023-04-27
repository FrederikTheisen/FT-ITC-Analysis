using System;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using AppKit;
using CoreGraphics;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC.GUI.MacOS.GraphViews
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
                case ResultGraphType.TemperatureDependence: Graph = new ThermodynamicParameterBarPlot(result, this); break;
            }

            Invalidate();
        }

        public void Setup(ProtonationAnalysis protonationAnalysis)
        {
            Type = ResultGraphType.ProtonationAnalysis;

            Graph = new ParameterDependenceGraph(this)
            {
                XLabel = "∆*H*{obs} (" + AppSettings.EnergyUnit.ToString() + ")",
                YLabel = "∆*H*{buffer,protonation} (" + AppSettings.EnergyUnit.ToString() + ")",
                XValues = protonationAnalysis.DataPoints.Select(dp => new FloatWithError(dp.Item1)).ToArray(),
                YValues = protonationAnalysis.DataPoints.Select(dp => dp.Item2).ToArray(),
                XScaleFactor = Energy.ScaleFactor(AppSettings.EnergyUnit),
                YScaleFactor = Energy.ScaleFactor(AppSettings.EnergyUnit),
                Fit = protonationAnalysis.LinearFitWithError,
            };

            (Graph as ParameterDependenceGraph).Setup();

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
