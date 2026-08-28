using System;
using System.Collections.Generic;
using AppKit;
using Foundation;
using AnalysisITC.Core.Utilities;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;

namespace AnalysisITC
{
    static class AnalysisResultValidityPresentation
    {
        public static string ButtonTitle(AnalysisResult result, AnalysisResultValidityReport report)
        {
            return StatusText(result?.Health ?? AnalysisResultHealth.Unknown);
        }

        public static NSColor ButtonColor(AnalysisResult result)
        {
            return StatusColor(result?.Health ?? AnalysisResultHealth.Unknown);
        }

        public static NSMutableAttributedString ButtonAttributedTitle(AnalysisResult result, AnalysisResultValidityReport report)
        {
            var status = result?.Health ?? AnalysisResultHealth.Unknown;
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
            return $"{StatusText(result?.Health ?? AnalysisResultHealth.Unknown)} for current data; {count} {experimentText} included.";
        }

        public static NSMutableAttributedString ReportText(AnalysisResult result, NSFont font)
        {
            var report = result?.ValidityReport ?? AnalysisResultValidityReport.Unknown("No analysis result is selected.");
            var statusText = ReportHeaderMessage(result?.Health ?? AnalysisResultHealth.Unknown);
            var heading = "Status: " + statusText;
            var markdown = BuildReportMarkdown(result, report, heading);
            var attributed = new NSMutableAttributedString();
            attributed.Append(AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(markdown, font));

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
                $"**{heading}**",
            };

            if (report.Reasons.Count > 0)
            {
                lines.Add("--");
                foreach (var reason in report.Reasons)
                    lines.Add(reason);
                lines.Add("--");
            }
            else if (report.Status == AnalysisResultValidity.Valid)
            {
                lines.Add("--Cached data matches current.--");

                if (result?.Health == AnalysisResultHealth.Warning)
                {
                    foreach (var solution in result.Solution.Solutions)
                    {
                        foreach (var warning in ParameterBoundaryWarningFormatter.MessagesFor(
                            solution,
                            result.Solution.ErrorEstimationMethod))
                        {
                            if (!lines.Contains(warning)) lines.Add(warning);
                        }
                    }
                }
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
            return AnalysisITC.UI.MacOS.MacStrings.FromMarkDownString(string.Join(Environment.NewLine, lines), font);
        }

        static List<string> BuildExperimentListLines(AnalysisResult result)
        {
            var lines = new List<string>();

            if (result?.Solution?.Solutions == null || result.Solution.Solutions.Count == 0)
            {
                lines.Add("No experiments are included.");
                return lines;
            }

            foreach (var solution in result.Solution.Solutions)
            {
                var data = solution?.Data;
                if (data == null) continue;

                lines.Add($"**{data.Name}**");
                lines.Add($"  --Date: {data.UIShortDateWithTime}");
                lines.Add($"  Temperature: {data.MeasuredTemperature:G3} °C--");
                foreach (var warning in ParameterBoundaryWarningFormatter.MessagesFor(
                    solution,
                    result.Solution.ErrorEstimationMethod))
                {
                    lines.Add($"  {warning}");
                }
            }

            return lines;
        }

        static string StatusText(AnalysisResultHealth status)
        {
            return status switch
            {
                AnalysisResultHealth.Valid => "Analysis is Valid",
                AnalysisResultHealth.Warning => "Warning",
                AnalysisResultHealth.PartialInvalid => "Partially Invalid",
                AnalysisResultHealth.Invalid => "Invalid",
                _ => "Unknown Status"
            };
        }

        static string ReportHeaderMessage(AnalysisResultHealth status)
        {
            return status switch
            {
                AnalysisResultHealth.Valid => "The analysis is valid.",
                AnalysisResultHealth.Warning => "The analysis is valid with warnings.",
                AnalysisResultHealth.PartialInvalid => "Partially invalid analysis.",
                AnalysisResultHealth.Invalid => "Invalid analysis.",
                _ => "Validity could not be determined."
            };
        }

        static NSColor StatusColor(AnalysisResultHealth status)
        {
            return status switch
            {
                AnalysisResultHealth.Valid => NSColor.SystemGreen, // NSColor.FromCalibratedRgb(0.22f, 0.72f, 0.34f),
                AnalysisResultHealth.Warning => NSColor.SystemOrange,
                AnalysisResultHealth.PartialInvalid => NSColor.SystemOrange,
                AnalysisResultHealth.Invalid => NSColor.SystemRed, // NSColor.FromCalibratedRgb(0.95f, 0.36f, 0.32f),
                _ => NSColor.SystemYellow // NSColor.FromCalibratedRgb(0.95f, 0.69f, 0.20f)
            }; // ; ;
        }
    }
}
