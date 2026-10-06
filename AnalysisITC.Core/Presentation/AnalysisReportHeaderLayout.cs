using System;
using System.Globalization;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Shared running-header text and date position, measured in PDF points.</summary>
    public sealed class AnalysisReportHeaderLayout
    {
        public const double ExportDateGap = 8;
        public const double Top = 14;
        public const double DateFontSize = 6.5;
        const string Brand = "FT-ITC Analysis";
        const string Separator = " · ";

        AnalysisReportHeaderLayout(string contextText, AnalysisReportTextStyle contextStyle,
            string exportDateText, double exportDateX, double contextWidth)
        {
            ContextText = contextText;
            ContextStyle = contextStyle;
            ExportDateText = exportDateText;
            ExportDateX = exportDateX;
            ContextWidth = contextWidth;
        }

        public string ContextText { get; }
        public AnalysisReportTextStyle ContextStyle { get; }
        public string ExportDateText { get; }
        public double ExportDateX { get; }
        public double ContextWidth { get; }

        public static AnalysisReportHeaderLayout Create(AnalysisReportDocument document,
            AnalysisReportPagePlan page, double left, double right, IAnalysisReportTextMeasurer measurer)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (page == null) throw new ArgumentNullException(nameof(page));
            if (measurer == null) throw new ArgumentNullException(nameof(measurer));

            var exportDate = document.ExportDateText;
            var dateX = right - measurer.Measure(exportDate, new AnalysisReportTextStyle(DateFontSize)).Width;
            var width = Math.Max(0, dateX - ExportDateGap - left);
            var style = new AnalysisReportTextStyle(page.IsCover ? 7.5 : 6.5, page.IsCover);
            if (page.IsCover)
                return new AnalysisReportHeaderLayout("FT-ITC ANALYSIS REPORT", style, exportDate, dateX, width);

            var label = SingleLine(page.ExperimentLabel);
            var name = SingleLine(page.ExperimentName);
            var result = FitName(SingleLine(page.ResultName), candidate =>
                measurer.Measure(Compose(candidate, label, ""), style).Width <= width);
            // The result uses all the space it needs before the experiment name is considered.
            name = label.Length == 0 ? "" : FitName(name, candidate =>
                measurer.Measure(Compose(result, label, candidate), style).Width <= width,
                requireReadablePrefix: true);
            return new AnalysisReportHeaderLayout(Compose(result, label, name), style, exportDate, dateX, width);
        }

        static string Compose(string result, string label, string name) => Brand
            + (result.Length == 0 ? "" : Separator + result)
            + (label.Length == 0 ? "" : Separator + label + "." + (name.Length == 0 ? "" : " " + name));

        static string SingleLine(string text) => (text ?? "").Trim()
            .Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

        static string FitName(string text, Func<string, bool> fits, bool requireReadablePrefix = false)
        {
            if (text.Length == 0 || fits(text)) return text;
            var elements = StringInfo.ParseCombiningCharacters(text);
            var low = 0;
            var high = elements.Length - 1;
            var best = -1;
            while (low <= high)
            {
                var count = low + (high - low) / 2;
                var candidate = text.Substring(0, elements[count]) + "…";
                if (fits(candidate)) { best = count; low = count + 1; }
                else high = count - 1;
            }
            if (best < 0 || (requireReadablePrefix && best == 0)) return "";
            return text.Substring(0, elements[best]) + "…";
        }
    }
}
