using System;
using System.Collections.Generic;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Measured, relative geometry for a heading and its conditions subtitle.</summary>
    public sealed class AnalysisReportHeadingLayout
    {
        public const double SubtitleGap = 3;
        public static readonly AnalysisReportTextStyle SubtitleStyle = new AnalysisReportTextStyle(11, light: true);

        AnalysisReportHeadingLayout(IReadOnlyList<string> titleLines, IReadOnlyList<string> subtitleLines,
            AnalysisReportTextStyle titleStyle, double width, double titleAdvance, double subtitleAdvance)
        {
            TitleLines = titleLines;
            SubtitleLines = subtitleLines;
            TitleStyle = titleStyle;
            TitleLineAdvance = titleAdvance;
            SubtitleLineAdvance = subtitleAdvance;
            TitleBounds = new AnalysisReportRect(0, 0, width, titleLines.Count * titleAdvance);
            SubtitleBounds = new AnalysisReportRect(0, TitleBounds.Bottom + SubtitleGap,
                width, subtitleLines.Count * subtitleAdvance);
        }

        public IReadOnlyList<string> TitleLines { get; }
        public IReadOnlyList<string> SubtitleLines { get; }
        public AnalysisReportTextStyle TitleStyle { get; }
        public AnalysisReportRect TitleBounds { get; }
        public AnalysisReportRect SubtitleBounds { get; }
        public double TitleLineAdvance { get; }
        public double SubtitleLineAdvance { get; }
        public double Height => SubtitleBounds.Bottom;

        internal static AnalysisReportHeadingLayout Create(string title, string subtitle, double width,
            AnalysisReportTextStyle titleStyle, IAnalysisReportTextMeasurer measurer,
            Func<string, double, AnalysisReportTextStyle, List<string>> wrap)
        {
            double Advance(AnalysisReportTextStyle style) => Math.Max(style.FontSize * 1.34,
                measurer.Measure("Ag", style).Height * 1.2);
            return new AnalysisReportHeadingLayout(wrap(title, width, titleStyle),
                wrap(subtitle, width, SubtitleStyle), titleStyle, width, Advance(titleStyle), Advance(SubtitleStyle));
        }
    }
}
