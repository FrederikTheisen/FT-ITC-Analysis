using System;
using AppKit;
using System.Collections.Generic;
using System.Linq;
using CoreGraphics;
using AnalysisITC.UI.MacOS;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.UI.MacOS.Drawing;
using AnalysisITC.Core.Analysis;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.UI.MacOS.Drawing
{
	public class ThermodynamicParameterBarPlot : GraphBase
	{
        public AnalysisResult Result { get; set; }
        readonly AnalysisResultPresentationData presentation;
        GlobalSolution Solution => Result.Solution;
        List<FeatureBoundingBox> FeatureBoundingBoxes = new List<FeatureBoundingBox>();

        GraphAxis DissociationConstantAxis { get; set; }
        double Mag { get; set; }

        List<ParameterType> Parameters => presentation.Parameters
            .Where(parameter => ParameterTypeAttribute.IsEnergyUnitParameter(parameter)
                && parameter.GetProperties().ParentType != ParameterType.HeatCapacity1
                && parameter != ParameterType.Offset)
            .ToList();
        int DataCount => presentation.Members.Count;
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
            presentation = analysis.PresentationData;

            var affinitiesForMagnitude = presentation.Members
                .Where(member => member.Parameters.ContainsKey(ParameterType.Affinity1))
                .Select(member => member.Parameters[ParameterType.Affinity1].Value);
            var kd = affinitiesForMagnitude.Any() ? affinitiesForMagnitude.Average() : 1;

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

            var energyValues = presentation.Members.SelectMany(member => member.Parameters
                .Where(parameter => ParameterTypeAttribute.IsEnergyUnitParameter(parameter.Key)
                    && parameter.Key.GetProperties().ParentType != ParameterType.HeatCapacity1
                    && parameter.Key != ParameterType.Offset)
                .SelectMany(parameter => new[] { parameter.Value.Value, parameter.Value.Lower, parameter.Value.Upper }))
                .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
                .ToList();
            var miny = Math.Min(energyValues.Count == 0 ? 0 : energyValues.Min(), 0);
            var maxy = Math.Max(energyValues.Count == 0 ? 0 : energyValues.Max(), 0);

            var energyUnit = EnergyUnitResolver.Resolve(
                AppSettings.EnergyUnitFamily,
                presentation.Members
                    .SelectMany(member => member.Parameters
                        .Where(parameter => ParameterTypeAttribute.IsEnergyUnitParameter(parameter.Key)
                            && parameter.Key.GetProperties().ParentType != ParameterType.HeatCapacity1
                            && parameter.Key != ParameterType.Offset)
                        .Select(parameter => parameter.Value.Value)));

            YAxis = GraphAxis.WithBuffer(this, miny, maxy, buffer: .1, position: AxisPosition.Left);
            YAxis.HideUnwantedTicks = false;
            YAxis.ValueFactor = Energy.ScaleFactor(energyUnit);
            YAxis.MirrorTicks = true;
            YAxis.LegendTitle = "Energy (" + energyUnit.GetUnit() + "/mol)";

            var affinities = presentation.Members.SelectMany(member => member.Parameters
                .Where(parameter => parameter.Key.GetProperties().ParentType == ParameterType.Affinity1)
                .Select(parameter => parameter.Value)).ToList();

            DissociationConstantAxis = GraphAxis.WithBuffer(this, 0, affinities.Count == 0 ? 1 : affinities.Max(), buffer: .1, position: AxisPosition.Right);
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

            DrawFrameBackground(gc);

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
            FeatureBoundingBoxes.Clear();

            foreach (var sol in presentation.Members.Select(member => member.Solution))
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
            int index = presentation.Members.ToList().FindIndex(member => ReferenceEquals(member.Solution, sol));
            var member = presentation.Members[index];

            var color = MacColors.GetColor(index, Solution.Solutions.Count) ?? (new CGColor[] { StrokeColor, StrokeColor });

            var barlayer = CGLayer.Create(gc, PlotSize);
            var errorlayer = CGLayer.Create(gc, PlotSize);
            var points = new CGPoint[DataCount];
            var barwidth = GetRelativePosition(CategoryWidth, 0).X - GetRelativePosition(0, 0).X - 2;

            var drawmode = (sol == DataManager.SelectedResultSolution ? CGPathDrawingMode.Stroke : CGPathDrawingMode.FillStroke);

            if (sol == DataManager.SelectedResultSolution)
            {
                var c = NSColor.ControlAccent.CGColor;
                color[1] = MacColors.Adjust(c, -40);
                color[0] = c;
            }

            foreach (var key in Parameters)
            {
                GraphAxis axis = null;
                if (key.GetProperties().ParentType == ParameterType.Affinity1) axis = DissociationConstantAxis;

                var position = GetBarPosition(key, index);
                if (!member.Parameters.TryGetValue(key, out var value)) continue;
                var barpoint = GetRelativePosition(position, value, axis);
                var errorpoint1 = GetRelativePosition(position, value.Upper, axis);
                var errorpoint2 = GetRelativePosition(position, value.Lower, axis);

                points[index] = barpoint;

                var rect = AddBarToLayer(barlayer, axis, barpoint, barwidth);
                AddErrorBarToLayer(errorlayer, barpoint, errorpoint1, barwidth);
                AddErrorBarToLayer(errorlayer, barpoint, errorpoint2, barwidth);

                FeatureBoundingBoxes.Add(new FeatureBoundingBox(MouseOverFeatureEvent.FeatureType.Bar, rect, Solution.Solutions.IndexOf(sol), Frame.Location, (int)key));
            }

            barlayer.Context.SetFillColor(color[0]);
            barlayer.Context.SetStrokeColor(color[1]);       
            barlayer.Context.DrawPath(CGPathDrawingMode.FillStroke);
            gc.DrawLayer(barlayer, Origin);
            errorlayer.Context.SetStrokeColor(color[1]);
            errorlayer.Context.StrokePath();
            gc.DrawLayer(errorlayer, Origin);
        }

        CGRect AddBarToLayer(CGLayer layer, GraphAxis axis, CGPoint value, nfloat barwidth)
        {
            var zero = GetRelativePosition(XAxis.Min, 0, axis).Y;
            var height = zero - value.Y;

            value.X -= barwidth / 2;

            var rect = new CGRect(value, new CGSize(barwidth, height));

            layer.Context.AddRect(rect);

            return rect;
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

        public override MouseOverFeatureEvent CursorFeatureFromPos(CGPoint cursorpos, bool isclick = false, bool ismouseup = false)
        {
            foreach (var feature in FeatureBoundingBoxes)
            {
                if (feature.CursorInBox(cursorpos))
                    return new MouseOverFeatureEvent(feature);
            }

            return new MouseOverFeatureEvent();
        }
    }
}
