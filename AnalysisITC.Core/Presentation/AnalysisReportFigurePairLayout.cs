using System;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Figure dimensions and measured axis margins in the renderer's coordinates.</summary>
    public readonly struct AnalysisReportFigureMetrics
    {
        public AnalysisReportFigureMetrics(double plotWidth, double plotHeight, double leftMargin,
            double rightMargin, double topMargin, double bottomMargin)
        {
            PlotWidth = plotWidth;
            PlotHeight = plotHeight;
            LeftMargin = leftMargin;
            RightMargin = rightMargin;
            TopMargin = topMargin;
            BottomMargin = bottomMargin;
        }

        public double PlotWidth { get; }
        public double PlotHeight { get; }
        public double LeftMargin { get; }
        public double RightMargin { get; }
        public double TopMargin { get; }
        public double BottomMargin { get; }
        public double PageWidth => LeftMargin + PlotWidth + RightMargin;
    }

    /// <summary>Paired report figures share a graph height, vertical margins and drawing scale.</summary>
    public sealed class AnalysisReportFigurePairLayout
    {
        public const double ColumnGap = 12;

        AnalysisReportFigurePairLayout() { }

        public double PlotHeight { get; private set; }
        public double TopMargin { get; private set; }
        public double BottomMargin { get; private set; }
        public double Scale { get; private set; }
        public AnalysisReportRect LeftColumn { get; private set; }
        public AnalysisReportRect RightColumn { get; private set; }
        public AnalysisReportRect LeftBounds { get; private set; }
        public AnalysisReportRect RightBounds { get; private set; }

        public static AnalysisReportFigurePairLayout Create(AnalysisReportRect target,
            AnalysisReportFigureMetrics left, AnalysisReportFigureMetrics right, bool focusThermogramOnBaseline)
        {
            var leftWidth = (target.Width - ColumnGap) * (focusThermogramOnBaseline ? .57 : .5);
            var rightWidth = target.Width - ColumnGap - leftWidth;
            var plotHeight = Math.Min(left.PlotHeight, right.PlotHeight);
            var topMargin = Math.Max(left.TopMargin, right.TopMargin);
            var bottomMargin = Math.Max(left.BottomMargin, right.BottomMargin);
            var pageHeight = topMargin + plotHeight + bottomMargin;
            // Fit both complete figures, including labels, without scaling their graph heights differently.
            var scale = Math.Min(1, Math.Min(target.Height / pageHeight,
                Math.Min(leftWidth / left.PageWidth, rightWidth / right.PageWidth)));
            var rightX = target.X + leftWidth + ColumnGap;
            return new AnalysisReportFigurePairLayout
            {
                PlotHeight = plotHeight,
                TopMargin = topMargin,
                BottomMargin = bottomMargin,
                Scale = scale,
                LeftColumn = new AnalysisReportRect(target.X, target.Y, leftWidth, target.Height),
                RightColumn = new AnalysisReportRect(rightX, target.Y, rightWidth, target.Height),
                LeftBounds = new AnalysisReportRect(target.X + (leftWidth - left.PageWidth * scale) / 2,
                    target.Y, left.PageWidth * scale, pageHeight * scale),
                RightBounds = new AnalysisReportRect(rightX + (rightWidth - right.PageWidth * scale) / 2,
                    target.Y, right.PageWidth * scale, pageHeight * scale),
            };
        }
    }
}
