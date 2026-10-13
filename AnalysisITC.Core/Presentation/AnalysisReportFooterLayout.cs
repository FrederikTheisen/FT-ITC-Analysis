using System;
using System.Globalization;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Shared single-line footer fields and positions, measured in PDF points.</summary>
    public sealed class AnalysisReportFooterLayout
    {
        public const double FontSize = 6.5;
        public const double FieldGap = 8;

        AnalysisReportFooterLayout(string provenanceText, string pageNumberText, string titleText,
            double provenanceX, double pageNumberX, double titleX)
        {
            ProvenanceText = provenanceText;
            PageNumberText = pageNumberText;
            TitleText = titleText;
            ProvenanceX = provenanceX;
            PageNumberX = pageNumberX;
            TitleX = titleX;
        }

        public string ProvenanceText { get; }
        public string PageNumberText { get; }
        public string TitleText { get; }
        public double ProvenanceX { get; }
        public double PageNumberX { get; }
        public double TitleX { get; }

        public static AnalysisReportFooterLayout Create(AnalysisReportDocument document,
            AnalysisReportLayoutPlan plan, AnalysisReportPagePlan page, IAnalysisReportTextMeasurer measurer)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (page == null) throw new ArgumentNullException(nameof(page));
            if (measurer == null) throw new ArgumentNullException(nameof(measurer));

            var style = new AnalysisReportTextStyle(FontSize);
            var left = plan.MarginLeft;
            var right = page.Width - plan.MarginRight;
            var center = (left + right) / 2;
            var provenance = document.Creator
                + (string.IsNullOrWhiteSpace(document.ApplicationVersion) ? "" : " " + document.ApplicationVersion);
            var number = "Page " + page.PageNumber.ToString(CultureInfo.CurrentCulture)
                + " of " + plan.Pages.Count.ToString(CultureInfo.CurrentCulture);
            var numberX = right - measurer.Measure(number, style).Width;
            var titleLeft = left + measurer.Measure(provenance, style).Width + FieldGap;
            var titleRight = numberX - FieldGap;
            // Keep the title centered on the page content while respecting both side fields.
            var titleWidth = 2 * Math.Max(0, Math.Min(center - titleLeft, titleRight - center));
            var title = page.IsCover ? "" : FitTitle(SingleLine(document.Title), titleWidth, measurer, style);
            return new AnalysisReportFooterLayout(provenance, number, title, left, numberX,
                center - measurer.Measure(title, style).Width / 2);
        }

        static string SingleLine(string text) => (text ?? "").Replace("\r\n", " ")
            .Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();

        static string FitTitle(string text, double width, IAnalysisReportTextMeasurer measurer,
            AnalysisReportTextStyle style)
        {
            if (text.Length == 0 || width <= 0) return "";
            if (measurer.Measure(text, style).Width <= width) return text;
            var elements = StringInfo.ParseCombiningCharacters(text);
            var low = 0;
            var high = elements.Length - 1;
            var best = -1;
            while (low <= high)
            {
                var count = low + (high - low) / 2;
                var candidate = text.Substring(0, elements[count]) + "…";
                if (measurer.Measure(candidate, style).Width <= width) { best = count; low = count + 1; }
                else high = count - 1;
            }
            return best < 0 ? "" : text.Substring(0, elements[best]) + "…";
        }
    }
}
