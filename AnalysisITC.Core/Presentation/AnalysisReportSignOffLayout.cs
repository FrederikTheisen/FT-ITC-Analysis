using System;
using System.Collections.Generic;
using System.Linq;

namespace AnalysisITC.Core.Presentation
{
    public sealed class AnalysisReportSignOffFieldLayout
    {
        internal AnalysisReportSignOffFieldLayout(string label, IReadOnlyList<string> lines, AnalysisReportRect bounds)
        {
            Label = label;
            Lines = lines;
            Bounds = bounds;
        }

        public string Label { get; }
        public IReadOnlyList<string> Lines { get; }
        public AnalysisReportRect Bounds { get; }
    }

    /// <summary>Shared, relative geometry for the cover's handwritten signing fields.</summary>
    public sealed class AnalysisReportSignOffLayout
    {
        public const double HeadingTop = 10;
        public const double HeadingFontSize = 10;
        public const double LabelFontSize = 7.5;
        public const double ValueFontSize = 9;
        public const double ValueTop = 13;
        public const double ValueLineHeight = ValueFontSize * 1.34;
        public const double SignatureLabelTop = 6;
        public const string SignatureLabel = "Preparer signature";
        public const string DateLabel = "Date signed";

        AnalysisReportSignOffLayout(IEnumerable<AnalysisReportSignOffFieldLayout> fields,
            AnalysisReportRect signature, AnalysisReportRect date, double height)
        {
            Fields = fields.ToList();
            Signature = signature;
            Date = date;
            Height = height;
        }

        public IReadOnlyList<AnalysisReportSignOffFieldLayout> Fields { get; }
        public AnalysisReportRect Signature { get; }
        public AnalysisReportRect Date { get; }
        public double Height { get; }

        internal static AnalysisReportSignOffLayout Create(AnalysisReportSignOffBlock block, double width,
            Func<string, double, IReadOnlyList<string>> wrap)
        {
            const double gap = 24;
            var columnWidth = (width - gap) / 2;
            var authorLines = wrap(block.PreparedBy, columnWidth);
            var generatedLines = wrap(block.GeneratedAt, columnWidth);
            var rowHeight = ValueTop + Math.Max(authorLines.Count, generatedLines.Count) * ValueLineHeight;
            var reportTop = 30 + rowHeight + 10;
            var reportLines = wrap(block.ReportId, width);
            var reportHeight = ValueTop + reportLines.Count * ValueLineHeight;
            var signatureTop = reportTop + reportHeight + 30;
            var signatureWidth = (width - gap) * .7;
            var fields = new[]
            {
                new AnalysisReportSignOffFieldLayout("Prepared by", authorLines,
                    new AnalysisReportRect(0, 30, columnWidth, rowHeight)),
                new AnalysisReportSignOffFieldLayout("Generated at", generatedLines,
                    new AnalysisReportRect(columnWidth + gap, 30, columnWidth, rowHeight)),
                new AnalysisReportSignOffFieldLayout("Report ID", reportLines,
                    new AnalysisReportRect(0, reportTop, width, reportHeight)),
            };
            return new AnalysisReportSignOffLayout(fields,
                new AnalysisReportRect(0, signatureTop, signatureWidth, 0),
                new AnalysisReportRect(signatureWidth + gap, signatureTop, width - signatureWidth - gap, 0),
                signatureTop + SignatureLabelTop + 12);
        }
    }
}
