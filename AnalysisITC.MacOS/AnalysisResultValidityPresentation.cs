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

        public static string ButtonTooltip(AnalysisResult result, AnalysisResultValidityReport report)
        {
            var count = result?.Solution?.Solutions?.Count ?? 0;
            var experimentText = count == 1 ? "experiment" : "experiments";
            return $"{StatusText(result?.Health ?? AnalysisResultHealth.Unknown)} for current data; {count} {experimentText} included.";
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
