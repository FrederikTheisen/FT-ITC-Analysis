using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Measured status stamp geometry relative to its section heading.</summary>
    public sealed class AnalysisReportStatusBadgeLayout
    {
        public const double TitleGap = 8;
        public const double HorizontalPadding = 8;
        public const double TextTop = 5;
        public static readonly AnalysisReportTextStyle TextStyle = new AnalysisReportTextStyle(6.5, true);

        AnalysisReportStatusBadgeLayout(AnalysisResultHealth health, string text, AnalysisReportRect bounds)
        {
            Health = health;
            Text = text;
            Bounds = bounds;
        }

        public AnalysisResultHealth Health { get; }
        public string Text { get; }
        public AnalysisReportRect Bounds { get; }

        internal static AnalysisReportStatusBadgeLayout Create(AnalysisResultHealth health, string text,
            double headingWidth, IAnalysisReportTextMeasurer measurer)
        {
            var width = measurer.Measure(text, TextStyle).Width + 2 * HorizontalPadding;
            return new AnalysisReportStatusBadgeLayout(health, text,
                new AnalysisReportRect(headingWidth - width, 1, width, 18));
        }

        internal static string TextFor(AnalysisResultHealth health, bool multipleResults = false) => health switch
        {
            AnalysisResultHealth.Valid => multipleResults ? "ANALYSES VALID" : "ANALYSIS VALID",
            AnalysisResultHealth.Warning => "REVIEW WARNINGS",
            AnalysisResultHealth.PartialInvalid => "PARTIAL / STALE",
            AnalysisResultHealth.Invalid => "INVALID / STALE",
            _ => "STATUS UNKNOWN",
        };
    }
}
