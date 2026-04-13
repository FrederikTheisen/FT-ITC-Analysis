using System;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using AppKit;
using CoreGraphics;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.Utilities;

namespace AnalysisITC
{
	public partial class ResultGraphView : NSGraph
	{
        ResultGraphType Type { get; set; }
        AnalysisResult Result { get; set; }
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
            DataManager.ResultSolutionSelectionDidChange += (s, e) => Invalidate();
        }

        #endregion

        public void Setup(ResultGraphType type, AnalysisResult result)
        {
            Result = result;

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

        public void Setup(ElectrostaticsAnalysis analysis, ElectrostaticsAnalysis.DissocFitMode mode)
        {
            Type = ResultGraphType.IonicStrengthDependence;
            var dataPoints = analysis.GetDataPoints(mode);

            switch (mode)
            {
                case ElectrostaticsAnalysis.DissocFitMode.AffinityVsSalt:
                    {
                        var grouped = dataPoints.GroupBy(dp => dp.Item1).ToList();
                        double jitter = (dataPoints.Max(dp => dp.Item1) - dataPoints.Min(dp => dp.Item1)) / 70;

                        var dps = grouped.SelectMany(g =>
                        {
                            int i = 0;
                            int n = g.Count();

                            return g.Select(dp => new Tuple<double, FloatWithError>(
                                dp.Item1 + (n == 1 ? 0.0 : (i++ - (n - 1) / 2.0) * jitter),
                                dp.Item2));
                        }).ToList();

                        Graph = new ParameterDependenceGraph(this)
                        {
                            XLabel = "[Salt] (mM)",
                            YLabel = $"{MarkdownStrings.DissociationConstant} ({analysis.Data.AppropriateAffinityUnit.GetProperties().Name})",
                            XValues = dps.Select(dp => new FloatWithError(dp.Item1)).ToArray(),
                            YValues = dps.Select(dp => dp.Item2).ToArray(),
                            YScaleFactor = analysis.Data.AppropriateAffinityUnit.GetMod()
                        };
                        (Graph as ParameterDependenceGraph).Setup();
                        Graph.YAxis.HideUnwantedTicks = true;
                        Graph.XAxis.HideUnwantedTicks = true;
                        break;
                    }
                case ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease:
                    {
                        var dps = dataPoints.GroupBy(dp => dp.Item1).ToList();

                        var x = dps.Select(g => new FloatWithError(g.Select(v => v.Item1).ToList()));
                        var y = dps.Select(g => new FloatWithError(g.Select(v => v.Item2).ToList()));

                        Graph = new ParameterDependenceGraph(this)
                        {
                            XLabel = "ln(*a*{salt})",
                            YLabel = $"ln({MarkdownStrings.DissociationConstant})",
                            XValues = x.ToArray(),
                            YValues = y.ToArray(),
                            XScaleFactor = 1,
                            YScaleFactor = 1,
                            Fit = analysis.CounterIonReleaseFit,
                        };

                        (Graph as ParameterDependenceGraph).Setup();
                        break;
                    }
                default:
                case ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel:
                    {
                        var dps = dataPoints.GroupBy(dp => dp.Item1).ToList();

                        var x = dps.Select(g => new FloatWithError(Math.Sqrt(g.Select(v => v.Item1).Average())));
                        var y = dps.Select(g => FWEMath.Log10(new FloatWithError(g.Select(v => v.Item2).ToList())));

                        var yvalues = dataPoints.Select(dp => FWEMath.Log10(dp.Item2)).ToArray();

                        Graph = new ParameterDependenceGraph(this)
                        {
                            XLabel = "√(*Ionic Strength*)",
                            YLabel = $"Log({MarkdownStrings.DissociationConstant})",
                            XValues = x.ToArray(),
                            YValues = y.ToArray(),
                            XScaleFactor = ConcentrationUnit.mM.GetMod(),
                            YScaleFactor = 1,
                            Fit = analysis.IonicStrengthDependenceFit,
                        };

                        (Graph as ParameterDependenceGraph).Setup();
                        Graph.XAxis.Min = -0.05f;
                        Graph.XAxis.Max *= 1.2f;
                        Graph.XAxis.HideUnwantedTicks = true;
                        Graph.XAxis.ValueFactor = 1; //should not be necessary

                        //var logrange = Math.Log(yvalues.Max()) - Math.Log(yvalues.Min());
                        //var logmin = yvalues.Min()) - 1;// - logrange * 2f;
                        //var logmax = Math.Log(yvalues.Max()) + 1;// + logrange * 0.5f;
                        //Graph.YAxis.Set(Math.Exp(logmin), Math.Exp(logmax)); // We want a larger Y range
                        break;
                    }
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

        public override void MouseMoved(NSEvent theEvent)
        {
            base.MouseMoved(theEvent);

            switch (Graph)
            {
                case TemperatureDependenceGraph:
                case ThermodynamicParameterBarPlot:
                    {
                        var b = Graph.CursorFeatureFromPos(CursorPositionInView);

                        if (b.IsMouseOverFeature) NSCursor.PointingHandCursor.Set();
                        else NSCursor.ArrowCursor.Set();
                        break;
                    }
                default: break;
            }
        }

        public override void MouseDown(NSEvent theEvent)
        {
            base.MouseDown(theEvent);

            switch (Graph)
            {
                case TemperatureDependenceGraph:
                case ThermodynamicParameterBarPlot:
                    {
                        var feature = Graph.CursorFeatureFromPos(CursorPositionInView);

                        if (feature.IsMouseOverFeature)
                        {
                            NSCursor.PointingHandCursor.Set();
                            var sol = Result.Solution.Solutions[feature.FeatureID];

                            if (theEvent.ClickCount == 2) sol.Data.ToggleInclude();

                            DataManager.SelectResultSolution(sol);
                        }
                        else
                        {
                            NSCursor.ArrowCursor.Set();
                            DataManager.ClearResultSolutionSelection();
                        }

                            Invalidate();
                        break;
                    }
                default: break;
            }
        }

        public override void MouseUp(NSEvent theEvent)
        {
            base.MouseUp(theEvent);
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
