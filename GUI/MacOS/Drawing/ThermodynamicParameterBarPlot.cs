using System;
using AppKit;
using System.Collections.Generic;
using AnalysisITC.AppClasses.Analysis2;
using System.Linq;
using CoreGraphics;
using AnalysisITC.GUI.MacOS;
using AnalysisITC.AppClasses.Analysis2.Models;

namespace AnalysisITC
{
	public class ThermodynamicParameterBarPlot : GraphBase
	{
        AnalysisResult Result { get; set; }
        GlobalSolution Solution => Result.Solution;

        GraphAxis DissociationConstantAxis { get; set; }
        double Mag { get; set; }

        List<ParameterType> ParameterFilter { get; set; } = new List<ParameterType>() { ParameterType.Affinity1, ParameterType.Enthalpy2, ParameterType.Nvalue1, ParameterType.Nvalue2 };

        List<ParameterType> Parameters => Result.Solution.IndividualModelReportParameters.Where(p => !ParameterFilter.Contains(p)).Select(p => p).ToList();
        int DataCount => Result.Solution.Solutions.Count;
        float BinWidth = 0.8f;
        float CategoryWidth => BinWidth / DataCount;

        float GetBarPosition(ParameterType key, int dataindex)
        {
            int catpos = (XAxis as ParameterCategoryAxis).CategoryLabels[key];

            return catpos - 0.5f * BinWidth + (.5f + dataindex) * CategoryWidth;
        }

        public ThermodynamicParameterBarPlot(AnalysisResult analysis, NSView view)
        {
            View = view;
            Result = analysis;

            var kd = Solution.Solutions.Average(s => s.ReportParameters[AppClasses.Analysis2.ParameterType.Affinity1]);

            Mag = Math.Log10(kd);

            var kdunit = Mag switch
            {
                > 0 => "M",
                > -3 => "mM",
                > -6 => "µM",
                > -9 => "nM",
                > -12 => "pM",
                _ => "M"
            };

            Mag = Math.Floor(Math.Pow(10,-Math.Floor(Mag)));

            XAxis = new ParameterCategoryAxis(this, Parameters, AxisPosition.Bottom);
            XAxis.HideUnwantedTicks = true;
            XAxis.LegendTitle = "";

            var miny = Math.Min(analysis.GetMinimumParameter(), 0);
            var maxy = Math.Max(analysis.GetMaximumParameter(), 0);

            YAxis = GraphAxis.WithBuffer(this, miny, maxy, buffer: .1, position: AxisPosition.Left);
            YAxis.HideUnwantedTicks = false;
            YAxis.ValueFactor = Energy.ScaleFactor(AppSettings.EnergyUnit);
            YAxis.MirrorTicks = true;
            YAxis.LegendTitle = "Energy (" + AppSettings.EnergyUnit.GetUnit() + "/mol)";

            var affinities = Solution.Solutions.Select(s => s.ReportParameters.Where(p => p.Key.GetProperties().ParentType == ParameterType.Affinity1)).SelectMany(p => p).Select(p => p.Value);

            DissociationConstantAxis = GraphAxis.WithBuffer(this, 0, affinities.Max(), buffer: .1, position: AxisPosition.Right);
            DissociationConstantAxis.HideUnwantedTicks = false;
            DissociationConstantAxis.ValueFactor = Mag;
            DissociationConstantAxis.MirrorTicks = false;
            DissociationConstantAxis.LegendTitle = "Kd (" + kdunit + ")";
        }

        public override void PrepareDraw(CGContext gc, CGPoint center)
        {
            this.Center = center;

            AutoSetFrame();

            SetupAxisScalingUnits();

            Draw(gc);

            DrawFrame(gc);

            XAxis.Draw(gc);
            YAxis.Draw(gc);
            //DissociationConstantAxis.Draw(gc);
        }

        public override void AutoSetFrame()
        {
            base.AutoSetFrame();

            //PlotSize.Width -= DissociationConstantAxis.EstimateLabelMargin();
        }

        void SetupAxisScalingUnits()
        {
            if (Frame.Size.Width * Frame.Size.Height < 0) return;

            var pppw = PlotSize.Width / (XAxis.Max - XAxis.Min);
            var ppph = PlotSize.Height / (YAxis.Max - YAxis.Min);

            PointsPerUnit = new CGSize(pppw, ppph);
        }

        void Draw(CGContext gc)
        {
            //foreach (var par in Parameters) DrawParameter(gc, par);

            foreach (var sol in Solution.Solutions)
            {
                DrawSolutionParameters(gc, sol);
            }

            DrawZeroLine(gc);
        }

        private void DrawZeroLine(CGContext gc)
        {
            var path = new CGPath();
            path.MoveToPoint(GetRelativePosition(XAxis.Min, 0));
            path.AddLineToPoint(GetRelativePosition(XAxis.Max, 0));

            var layer = CGLayer.Create(gc, PlotSize);
            layer.Context.SetStrokeColor(StrokeColor);
            layer.Context.AddPath(path);
            layer.Context.StrokePath();

            gc.DrawLayer(layer, Frame.Location);
        }

        void DrawSolutionParameters(CGContext gc, SolutionInterface sol)
        {
            int index = Solution.Solutions.IndexOf(sol);

            var color = MacColors.GetColor(index, Solution.Solutions.Count);
            if (color == null) color = new CGColor[] { StrokeColor, StrokeColor };

            var barlayer = CGLayer.Create(gc, PlotSize);
            var errorlayer = CGLayer.Create(gc, PlotSize);
            var points = new CGPoint[DataCount];
            var barwidth = GetRelativePosition(CategoryWidth, 0).X - GetRelativePosition(0, 0).X - 2;

            foreach (var key in Parameters)
            {
                GraphAxis axis = null;
                if (key.GetProperties().ParentType == ParameterType.Affinity1) axis = DissociationConstantAxis;

                var position = GetBarPosition(key, index);
                var value = sol.ReportParameters[key];
                var barpoint = GetRelativePosition(position, value, axis);
                var errorpoint = GetRelativePosition(position, value.Value * (1 + value.FractionSD), axis);

                points[index] = barpoint;

                AddBarToLayer(barlayer, axis, barpoint, barwidth);
                AddErrorBarToLayer(errorlayer, barpoint, errorpoint, barwidth);
            }

            barlayer.Context.SetFillColor(color[0]);
            barlayer.Context.SetStrokeColor(color[1]);       
            barlayer.Context.DrawPath(CGPathDrawingMode.FillStroke);
            gc.DrawLayer(barlayer, Origin);
            errorlayer.Context.SetStrokeColor(color[1]);
            errorlayer.Context.StrokePath();
            gc.DrawLayer(errorlayer, Origin);
        }

        void DrawParameter(CGContext gc, ParameterType key)
        {
            int index = 0;

            var barlayer = CGLayer.Create(gc, PlotSize);
            var errorlayer = CGLayer.Create(gc, PlotSize);
            var points = new CGPoint[DataCount];
            var barwidth = GetRelativePosition(CategoryWidth, 0).X - GetRelativePosition(0, 0).X - 2;

            foreach (var sol in Solution.Solutions)
            {
                GraphAxis axis = null;
                if (key.GetProperties().ParentType == ParameterType.Affinity1) axis = DissociationConstantAxis;
                var position = GetBarPosition(key, index);
                var value = sol.ReportParameters[key];
                var barpoint = GetRelativePosition(position, value, axis);
                var errorpoint = GetRelativePosition(position, value.Value * (1 + value.FractionSD), axis);

                points[index] = barpoint;

                AddBarToLayer(barlayer, axis, barpoint, barwidth);
                AddErrorBarToLayer(errorlayer, barpoint, errorpoint, barwidth);

                index++;
            }

            barlayer.Context.SetFillColor(StrokeColor);
            barlayer.Context.FillPath();
            gc.DrawLayer(barlayer, Origin);
            errorlayer.Context.SetStrokeColor(StrokeColor);
            errorlayer.Context.StrokePath();
            gc.DrawLayer(errorlayer, Origin);
        }

        void AddBarToLayer(CGLayer layer, GraphAxis axis, CGPoint value, nfloat barwidth)
        {
            var zero = GetRelativePosition(XAxis.Min, 0, axis).Y;
            var height = zero - value.Y;

            value.X -= barwidth / 2;

            var rect = new CGRect(value, new CGSize(barwidth, height));

            layer.Context.AddRect(rect);
        }

        void AddErrorBarToLayer(CGLayer layer, CGPoint bartop, CGPoint error, nfloat barwidth)
        {
            var path = new CGPath();
            path.MoveToPoint(bartop);
            path.AddLineToPoint(error);
            path.MoveToPoint(error - new CGSize(barwidth / 3, 0));
            path.AddLineToPoint(error + new CGSize(barwidth / 3, 0));

            layer.Context.AddPath(path);
        }
    }
}

