using System;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using AppKit;
using CoreGraphics;
using AnalysisITC.Core.Analysis;
using AnalysisITC.UI.MacOS.Drawing;
using AnalysisITC.Core.Utilities;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;

namespace AnalysisITC
{
	public partial class ResultGraphView : NSGraph
	{
        ResultGraphType Type { get; set; }
        AnalysisResult Result { get; set; }
        new GraphBase Graph { get; set; }
        CorrelationGraphControl correlationControl;

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

            WantsLayer = true;
            Layer.BackgroundColor = NSColor.Clear.CGColor;
		}

        #endregion

        public void Setup(ResultGraphType type, AnalysisResult result)
        {
            HideCorrelationHost();
            Result = result;

            Type = type;

            switch (Type)
            {
                case ResultGraphType.Parameters: Graph = new ThermodynamicParameterBarPlot(result, this); break;
                case ResultGraphType.TemperatureDependence:
                    Graph = new TemperatureDependenceGraph(
                        result,
                        this,
                        AnalysisResultTabViewController.CurrentUseKelvin);
                    break;
            }

            Invalidate();
        }

        public void SetupCorrelation(
            BootstrapCorrelationResult correlation,
            int selectedCount,
            string selectedLabel,
            bool isGlobalResult,
            string unavailableMessage = null)
        {
            Type = ResultGraphType.Correlation;
            Result = null;
            Graph = null;

            EnsureCorrelationHost();
            correlationControl.Hidden = false;
            correlationControl.SetCorrelationResult(
                correlation,
                selectedCount,
                selectedLabel,
                isGlobalResult,
                unavailableMessage);
            LayoutSubtreeIfNeeded();
            Invalidate();
        }

        public void SetupSelectedFit(SolutionInterface solution)
        {
            HideCorrelationHost();
            Type = ResultGraphType.SelectedFit;

            if (solution?.Data == null || solution.Model == null)
            {
                Graph = null;
                Invalidate();
                return;
            }

            var fitGraph = new DataFittingGraph(solution.Data, solution, this)
            {
                ShowGrid = true,
                ShowZero = true,
                ShowPeakInfo = false,
                ShowErrorBars = true,
                DrawConfidenceBands = true,
                HideBadData = false,
                HideBadDataErrorBars = true,
                DrawWithOffset = false,
                ShowParameterGuides = false,
                ShowParameterBox = false,
                AutoAxesFocusesIncludedOnly = true,
            };
            fitGraph.ResidualDisplayOptions.ShowResidualGraph = true;
            fitGraph.ResidualDisplayOptions.GapGraphs = true;
            Graph = fitGraph;

            Invalidate();
        }

        public void Setup(ProtonationAnalysis analysis)
        {
            HideCorrelationHost();
            Type = ResultGraphType.ProtonationAnalysis;

            var energyUnit = EnergyUnitResolver.Resolve(
                AppSettings.EnergyUnitFamily,
                analysis.DataPoints.SelectMany(point => new[]
                {
                    point.Item1,
                    point.Item2.Value,
                }));

            Graph = new ParameterDependenceGraph(this)
            {
                YLabel = "∆*H*{obs} (" + energyUnit.GetUnit() + ")",
                XLabel = "∆*H*{buffer,protonation} (" + energyUnit.GetUnit() + ")",
                XValues = analysis.DataPoints.Select(dp => new FloatWithError(dp.Item1)).ToArray(),
                YValues = analysis.DataPoints.Select(dp => dp.Item2).ToArray(),
                XScaleFactor = Energy.ScaleFactor(energyUnit),
                YScaleFactor = Energy.ScaleFactor(energyUnit),
                Fit = analysis.Fit,
            };

            (Graph as ParameterDependenceGraph).Setup();

            Invalidate();
        }

        public void Setup(ElectrostaticsAnalysis analysis, ElectrostaticsAnalysis.DissocFitMode mode)
        {
            HideCorrelationHost();
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
                            XLabel = "√(*Ionic Strength* / M)",
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

        void EnsureCorrelationHost()
        {
            if (correlationControl != null)
                return;

            correlationControl = new CorrelationGraphControl
            {
                TranslatesAutoresizingMaskIntoConstraints = false,
                Hidden = true,
            };
            AddSubview(correlationControl);
            NSLayoutConstraint.ActivateConstraints(new[]
            {
                correlationControl.LeadingAnchor.ConstraintEqualToAnchor(LeadingAnchor),
                correlationControl.TrailingAnchor.ConstraintEqualToAnchor(TrailingAnchor),
                correlationControl.TopAnchor.ConstraintEqualToAnchor(TopAnchor),
                correlationControl.BottomAnchor.ConstraintEqualToAnchor(BottomAnchor),
            });
        }

        void HideCorrelationHost()
        {
            if (correlationControl != null)
            {
                correlationControl.ClearHoverPresentation();
                correlationControl.Hidden = true;
            }
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            if (Type == ResultGraphType.Correlation)
            {
                base.DrawRect(dirtyRect);
                return;
            }

            base.DrawRect(dirtyRect);

            if (Graph == null)
            {
                if (Type == ResultGraphType.SelectedFit)
                    DrawSelectedFitEmptyState();
                return;
            }

            var cg = NSGraphicsContext.CurrentContext.CGContext;

            Graph.PrepareDraw(cg, new CGPoint(Frame.GetMidX(), Frame.GetMidY()));
        }

        void DrawSelectedFitEmptyState()
        {
            var paragraph = new NSMutableParagraphStyle
            {
                Alignment = NSTextAlignment.Center,
                LineBreakMode = NSLineBreakMode.ByWordWrapping,
            };
            var title = new NSAttributedString(
                "No experiment selected",
                new NSStringAttributes
                {
                    Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize, NSFontWeight.Semibold),
                    ForegroundColor = NSColor.SecondaryLabel,
                    ParagraphStyle = paragraph,
                });
            var message = new NSAttributedString(
                "Select an experiment in the result table or an overview graph to inspect its saved fit.",
                new NSStringAttributes
                {
                    Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
                    ForegroundColor = NSColor.SecondaryLabel,
                    ParagraphStyle = paragraph,
                });

            nfloat horizontalInset = 24;
            nfloat spacing = 6;
            var contentWidth = Math.Max(1, Bounds.Width - 2 * horizontalInset);
            var drawingOptions = NSStringDrawingOptions.UsesLineFragmentOrigin
                | NSStringDrawingOptions.UsesFontLeading;
            var titleBounds = title.BoundingRectWithSize(
                new CGSize(contentWidth, nfloat.MaxValue),
                drawingOptions);
            var messageBounds = message.BoundingRectWithSize(
                new CGSize(contentWidth, nfloat.MaxValue),
                drawingOptions);
            var contentHeight = titleBounds.Height + spacing + messageBounds.Height;
            var messageY = Math.Max(0, Bounds.GetMidY() - contentHeight / 2);

            message.DrawString(new CGRect(
                horizontalInset,
                messageY,
                contentWidth,
                messageBounds.Height));
            title.DrawString(new CGRect(
                horizontalInset,
                messageY + messageBounds.Height + spacing,
                contentWidth,
                titleBounds.Height));
        }

        new public void Print()
        {
            if (Type == ResultGraphType.Correlation)
            {
                correlationControl?.ClearHoverPresentation();
                if (correlationControl == null || !correlationControl.HasPrintableData)
                    return;

                LayoutSubtreeIfNeeded();
                correlationControl.LayoutSubtreeIfNeeded();
                correlationControl.PrintOnWhite = true;
                correlationControl.NeedsDisplay = true;
                try
                {
                    var operation = NSPrintOperation.FromView(correlationControl);
                    operation.PrintInfo.PaperSize = correlationControl.Frame.Size;
                    operation.PrintInfo.BottomMargin = 0;
                    operation.PrintInfo.TopMargin = 0;
                    operation.PrintInfo.LeftMargin = 0;
                    operation.PrintInfo.RightMargin = 0;
                    operation.PrintInfo.ScalingFactor = 1;
                    operation.RunOperation();
                }
                finally
                {
                    correlationControl.ClearHoverPresentation();
                    correlationControl.PrintOnWhite = false;
                    correlationControl.NeedsDisplay = true;
                }
                return;
            }

            if (Graph == null) return;

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
                case DataFittingGraph:
                    {
                        var feature = Graph.CursorFeatureFromPos(CursorPositionInView);
                        if (feature.IsMouseOverFeature)
                        {
                            NSCursor.PointingHandCursor.Set();
                            ToolTip = feature.ToolTip;
                        }
                        else
                        {
                            NSCursor.ArrowCursor.Set();
                            ToolTip = null;
                        }
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
            SelectedFit,
            Correlation,
            TemperatureDependence,
            IonicStrengthDependence,
            ProtonationAnalysis,
        }
    }
}
