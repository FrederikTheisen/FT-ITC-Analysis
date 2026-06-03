using System;
using System.Collections.Generic;
using AppKit;
using Foundation;
using AnalysisITC.Utilities;

namespace AnalysisITC
{
    static class AnalysisResultValidityPresentation
    {
        public static string ButtonTitle(AnalysisResult result, AnalysisResultValidityReport report)
        {
            var count = result?.Solution?.Solutions?.Count ?? 0;
            return $"{StatusText(report?.Status ?? AnalysisResultValidity.Unknown)}";
        }

        public static NSColor ButtonColor(AnalysisResultValidityReport report)
        {
            return StatusColor(report?.Status ?? AnalysisResultValidity.Unknown);
        }

        public static NSMutableAttributedString ButtonAttributedTitle(AnalysisResult result, AnalysisResultValidityReport report)
        {
            var status = report?.Status ?? AnalysisResultValidity.Unknown;
            var title = "● " + ButtonTitle(result, report);
            var attributed = new NSMutableAttributedString(title);
            var range = new NSRange(0, attributed.Length);

            attributed.AddAttribute(NSStringAttributeKey.Font, NSFont.BoldSystemFontOfSize(NSFont.SmallSystemFontSize), range);
            attributed.AddAttribute(NSStringAttributeKey.ForegroundColor, NSColor.Label, range);
            attributed.AddAttribute(NSStringAttributeKey.ForegroundColor, StatusColor(status), new NSRange(0, 1));

            return attributed;
        }

        public static string ButtonTooltip(AnalysisResult result, AnalysisResultValidityReport report)
        {
            var count = result?.Solution?.Solutions?.Count ?? 0;
            var experimentText = count == 1 ? "experiment" : "experiments";
            return $"{StatusText(report?.Status ?? AnalysisResultValidity.Unknown)} for current data; {count} {experimentText} included.";
        }

        public static NSMutableAttributedString ReportText(AnalysisResult result, NSFont font)
        {
            var report = result?.ValidityReport ?? AnalysisResultValidityReport.Unknown("No analysis result is selected.");
            var statusText = ReportHeaderMessage(report.Status);
            var heading = "Status: " + statusText;
            var markdown = BuildReportMarkdown(result, report, heading);
            var attributed = new NSMutableAttributedString();
            attributed.Append(MacStrings.FromMarkDownString(markdown, font));

            //var headingLength = Math.Min(heading.Length, attributed.Value.Length);
            //if (headingLength > 0)
            //{
            //    attributed.AddAttribute(
            //        NSStringAttributeKey.ForegroundColor,
            //        StatusColor(report.Status),
            //        new NSRange(0, headingLength));
            //}

            return attributed;
        }

        static string BuildReportMarkdown(AnalysisResult result, AnalysisResultValidityReport report, string heading)
        {
            var lines = new List<string>
            {
                $"**{heading}**"
            };

            if (report.Reasons.Count > 0)
            {
                foreach (var reason in report.Reasons)
                    lines.Add("--" + reason + "--");
            }
            else if (report.Status == AnalysisResultValidity.Valid)
            {
                lines.Add("--Cached data matches current.--");
            }
            else
            {
                lines.Add("--Validity could not be determined.--");
            }

            return string.Join(Environment.NewLine, lines);
        }

        public static NSAttributedString ExperimentListText(AnalysisResult result, NSFont font)
        {
            var lines = BuildExperimentListLines(result);
            return MacStrings.FromMarkDownString(string.Join(Environment.NewLine, lines), font);
        }

        static List<string> BuildExperimentListLines(AnalysisResult result)
        {
            var lines = new List<string>();

            if (result?.Solution?.Model?.Models == null || result.Solution.Model.Models.Count == 0)
            {
                lines.Add("No experiments are included.");
                return lines;
            }

            foreach (var mdl in result.Solution.Model.Models)
            {
                if (mdl?.Data == null) continue;

                lines.Add($"**{mdl.Data.Name}**");
                lines.Add($"  --Date: {mdl.Data.UIShortDateWithTime}");
                lines.Add($"  Temperature: {mdl.Data.MeasuredTemperature:G3} °C--");
            }

            return lines;
        }

        static string StatusText(AnalysisResultValidity status)
        {
            return status switch
            {
                AnalysisResultValidity.Valid => "Analysis is Valid",
                AnalysisResultValidity.PartialInvalid => "Partially Invalid",
                AnalysisResultValidity.Invalid => "Invalid",
                _ => "Unknown Status"
            };
        }

        static string ReportHeaderMessage(AnalysisResultValidity status)
        {
            return status switch
            {
                AnalysisResultValidity.Valid => "The analysis is valid.",
                AnalysisResultValidity.PartialInvalid => "Partially invalid analysis.",
                AnalysisResultValidity.Invalid => "Invalid analysis.",
                _ => "Validity could not be determined."
            };
        }

        static NSColor StatusColor(AnalysisResultValidity status)
        {
            return status switch
            {
                AnalysisResultValidity.Valid => NSColor.SystemGreen, // NSColor.FromCalibratedRgb(0.22f, 0.72f, 0.34f),
                AnalysisResultValidity.PartialInvalid => NSColor.SystemOrange,
                AnalysisResultValidity.Invalid => NSColor.SystemRed, // NSColor.FromCalibratedRgb(0.95f, 0.36f, 0.32f),
                _ => NSColor.SystemYellow // NSColor.FromCalibratedRgb(0.95f, 0.69f, 0.20f)
            }; // ; ;
        }
    }
}
