using System;
using System.Collections.Generic;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Measured, relative geometry for a heading, optional status stamp and conditions subtitle.</summary>
    public sealed class AnalysisReportHeadingLayout
    {
        public const double SubtitleGap = 3;
        public static readonly AnalysisReportTextStyle SubtitleStyle = new AnalysisReportTextStyle(11, light: true);

        AnalysisReportHeadingLayout(IReadOnlyList<string> titleLines, IReadOnlyList<string> subtitleLines,
            AnalysisReportTextStyle titleStyle, double width, double titleWidth, double titleAdvance,
            double subtitleAdvance, AnalysisReportStatusBadgeLayout statusBadge)
        {
            TitleLines = titleLines;
            SubtitleLines = subtitleLines;
            TitleStyle = titleStyle;
            TitleLineAdvance = titleAdvance;
            SubtitleLineAdvance = subtitleAdvance;
            StatusBadge = statusBadge;
            TitleBounds = new AnalysisReportRect(0, 0, titleWidth, titleLines.Count * titleAdvance);
            var titleBottom = Math.Max(TitleBounds.Bottom, statusBadge?.Bounds.Bottom ?? 0);
            SubtitleBounds = new AnalysisReportRect(0, titleBottom + (subtitleLines.Count > 0 ? SubtitleGap : 0),
                width, subtitleLines.Count * subtitleAdvance);
        }

        public IReadOnlyList<string> TitleLines { get; }
        public IReadOnlyList<string> SubtitleLines { get; }
        public AnalysisReportTextStyle TitleStyle { get; }
        public AnalysisReportRect TitleBounds { get; }
        public AnalysisReportRect SubtitleBounds { get; }
        public AnalysisReportStatusBadgeLayout StatusBadge { get; }
        public double TitleLineAdvance { get; }
        public double SubtitleLineAdvance { get; }
        public double Height => SubtitleBounds.Bottom;

        internal static AnalysisReportHeadingLayout Create(string title, string subtitle, double width,
            AnalysisReportTextStyle titleStyle, IAnalysisReportTextMeasurer measurer,
            Func<string, double, AnalysisReportTextStyle, List<string>> wrap,
            AnalysisReportStatusBadgeLayout statusBadge = null)
        {
            double Advance(AnalysisReportTextStyle style) => Math.Max(style.FontSize * 1.34,
                measurer.Measure("Ag", style).Height * 1.2);
            var titleWidth = statusBadge == null ? width
                : Math.Max(1, statusBadge.Bounds.X - AnalysisReportStatusBadgeLayout.TitleGap);
            return new AnalysisReportHeadingLayout(wrap(title, titleWidth, titleStyle),
                string.IsNullOrWhiteSpace(subtitle) ? Array.Empty<string>() : wrap(subtitle, width, SubtitleStyle),
                titleStyle, width, titleWidth, Advance(titleStyle), Advance(SubtitleStyle), statusBadge);
        }
    }
}
