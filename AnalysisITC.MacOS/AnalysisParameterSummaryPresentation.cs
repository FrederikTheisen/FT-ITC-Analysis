using System.Collections.Generic;

using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.UI.MacOS
{
    internal readonly struct AnalysisParameterSummaryRow
    {
        public string Label { get; }
        public string Value { get; }
        public bool IsModelHeader { get; }

        public AnalysisParameterSummaryRow(
            string label,
            string value,
            bool isModelHeader)
        {
            Label = label ?? string.Empty;
            Value = value ?? string.Empty;
            IsModelHeader = isModelHeader;
        }
    }

    /// <summary>
    /// Produces the structured parameter summary shared by the analysis graph's
    /// parameter box and the analysis inspector.
    /// </summary>
    internal static class AnalysisParameterSummaryPresentation
    {
        public static List<AnalysisParameterSummaryRow> BuildRows(
            SolutionInterface solution,
            FinalFigureDisplayParameters display)
        {
            var rows = new List<AnalysisParameterSummaryRow>();
            if (solution == null) return rows;

            foreach (var parameter in solution.UISolutionParameters(display))
            {
                var isModelHeader =
                    display.HasFlag(FinalFigureDisplayParameters.Model)
                    && rows.Count == 0;
                rows.Add(new AnalysisParameterSummaryRow(
                    parameter.Item1,
                    parameter.Item2,
                    isModelHeader));
            }

            return rows;
        }

        public static List<string> BuildLines(
            SolutionInterface solution,
            FinalFigureDisplayParameters display)
        {
            var lines = new List<string>();
            foreach (var row in BuildRows(solution, display))
            {
                lines.Add(row.IsModelHeader
                    ? $"{row.Label} |\u00A0RMSD = {row.Value}"
                    : $"{row.Label} = {row.Value}");
            }

            return lines;
        }
    }
}
