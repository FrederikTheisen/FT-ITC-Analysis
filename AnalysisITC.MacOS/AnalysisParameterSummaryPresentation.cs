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
        /// <summary>Readable model name; set only on the model header row.</summary>
        public string ModelName { get; }
        /// <summary>Global or Individual; set only on the model header row.</summary>
        public string Scope { get; }
        public string ScopeToolTip => Scope switch
        {
            AnalysisParameterSummaryPresentation.GlobalScope => "Parameters shared across experiments",
            AnalysisParameterSummaryPresentation.IndividualScope => "No shared parameters",
            _ => string.Empty,
        };

        public AnalysisParameterSummaryRow(
            string label,
            string value,
            bool isModelHeader,
            string modelName = null,
            string scope = null)
        {
            Label = label ?? string.Empty;
            Value = value ?? string.Empty;
            IsModelHeader = isModelHeader;
            ModelName = modelName ?? string.Empty;
            Scope = scope ?? string.Empty;
        }
    }

    /// <summary>
    /// Produces the structured parameter summary shared by the analysis graph's
    /// parameter box and the analysis inspector.
    /// </summary>
    internal static class AnalysisParameterSummaryPresentation
    {
        public const string GlobalScope = "Global";
        public const string IndividualScope = "Individual";
        public const string ModelToolTip = "Fitted model";

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
                    isModelHeader,
                    isModelHeader ? solution.Model?.ModelName : null,
                    isModelHeader
                        ? solution.IsGlobalAnalysisSolution ? GlobalScope : IndividualScope
                        : null));
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
