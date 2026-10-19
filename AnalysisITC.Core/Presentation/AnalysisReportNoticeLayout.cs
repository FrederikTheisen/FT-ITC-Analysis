using System;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Notice geometry shared by pagination and both desktop renderers, in PDF points.</summary>
    public static class AnalysisReportNoticeLayout
    {
        public const double HorizontalInset = 6;
        public const double VerticalInset = 5;
        public const double TitleFontSize = 10;
        public const double TitleAdvance = 15;
        public const double BodyFontSize = 9;
        public const double LineHeight = BodyFontSize * 1.34;
        public static readonly AnalysisReportTextStyle BodyStyle = new AnalysisReportTextStyle(BodyFontSize);

        public static double HeadingHeight(string title) => string.IsNullOrWhiteSpace(title) ? 0 : TitleAdvance;

        public static double Height(string title, int lineCount) =>
            2 * VerticalInset + HeadingHeight(title) + Math.Max(1, lineCount) * LineHeight;

        public static AnalysisReportRect BodyBounds(AnalysisReportLayoutFragment fragment)
        {
            var bounds = fragment.Bounds;
            var titleHeight = HeadingHeight(fragment.Title);
            return new AnalysisReportRect(bounds.X + HorizontalInset, bounds.Y + VerticalInset + titleHeight,
                bounds.Width - 2 * HorizontalInset, bounds.Height - 2 * VerticalInset - titleHeight);
        }
    }
}
