using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AnalysisITC.Core.Presentation
{
    public static class TandemOriginPresentation
    {
        static readonly Regex BackMixingDescription = new Regex(
            @"^Tandem concatenation \((?<mode>[^\r\n]+)\): DeadVolume=(?<volume>[^,\r\n]+), RemoveOverflow=(?<overflow>True|False), MixFrac=(?<fractions>[^,\r\n]+), (?<method>[^\r\n]+) bookkeeping$");
        static readonly Regex WithoutBackMixingDescription = new Regex(
            @"^Tandem concatenation \(MicroCal concat; no back-mixing; (?<method>[^\r\n]+) bookkeeping\)\.$");

        /// <summary>Formats recorded provenance for display without changing the saved description.</summary>
        public static string FormatDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return description;

            var lines = new List<string>();
            foreach (var line in description.Replace("\r\n", "\n").Split('\n'))
            {
                var mixing = BackMixingDescription.Match(line);
                var noMixing = WithoutBackMixingDescription.Match(line);
                if (mixing.Success)
                {
                    lines.Add("Tandem concatenation (" + mixing.Groups["mode"].Value + ")");
                    lines.Add("Dead volume: " + mixing.Groups["volume"].Value);
                    lines.Add("Overflow removed: " + (mixing.Groups["overflow"].Value == "True" ? "Yes" : "No"));
                    lines.Add("Back-mixing fraction: " + mixing.Groups["fractions"].Value);
                    lines.Add("Bookkeeping: " + mixing.Groups["method"].Value);
                }
                else if (noMixing.Success)
                {
                    lines.Add("Tandem concatenation (MicroCal concat; no back-mixing)");
                    lines.Add("Bookkeeping: " + noMixing.Groups["method"].Value);
                }
                else if (line.StartsWith("Source files: ", StringComparison.Ordinal))
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Length > 0) lines.Add("");
                    lines.Add("Source files:");
                    lines.Add(line.Substring("Source files: ".Length));
                }
                else lines.Add(line);
            }
            return string.Join("\n", lines);
        }
    }
}
