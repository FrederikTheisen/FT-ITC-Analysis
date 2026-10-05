using System.Collections.Generic;

using AnalysisITC.Core.Analysis.Models;

namespace AnalysisITC.Core.Presentation
{
    public readonly struct AnalysisParameterSummaryRow
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
    /// Read-only fit summary shown by the analysis inspector: a model header
    /// followed by label/value rows, starting with RMSD when available.
    /// </summary>
    public sealed class AnalysisFitSummary
    {
        public static readonly AnalysisFitSummary Empty =
            new AnalysisFitSummary(null, new List<AnalysisParameterSummaryRow>());

        AnalysisFitSummary(
            AnalysisParameterSummaryRow? header,
            IReadOnlyList<AnalysisParameterSummaryRow> rows)
        {
            Header = header;
            Rows = rows;
        }

        public AnalysisParameterSummaryRow? Header { get; }
        public IReadOnlyList<AnalysisParameterSummaryRow> Rows { get; }
        public bool IsEmpty => Header == null && Rows.Count == 0;

        public string ModelTitle => Header == null
            ? string.Empty
            : string.IsNullOrWhiteSpace(Header.Value.ModelName)
                ? Header.Value.Label
                : Header.Value.ModelName;
        public string Scope => Header?.Scope ?? string.Empty;
        public string ScopeToolTip => Header?.ScopeToolTip ?? string.Empty;

        internal static AnalysisFitSummary Create(IReadOnlyList<AnalysisParameterSummaryRow> rows)
        {
            if (rows == null || rows.Count == 0) return Empty;
            if (!rows[0].IsModelHeader) return new AnalysisFitSummary(null, rows);

            var header = rows[0];
            var gridRows = new List<AnalysisParameterSummaryRow>();
            if (!string.IsNullOrWhiteSpace(header.Value))
                gridRows.Add(new AnalysisParameterSummaryRow(
                    AnalysisParameterSummaryPresentation.RmsdLabel,
                    header.Value,
                    false));
            for (var index = 1; index < rows.Count; index++)
                gridRows.Add(rows[index]);

            return new AnalysisFitSummary(header, gridRows);
        }
    }

    /// <summary>
    /// Produces the structured parameter summary shared by the analysis graph's
    /// parameter box and the analysis inspector on every platform.
    /// </summary>
    public static class AnalysisParameterSummaryPresentation
    {
        public const string GlobalScope = "Global";
        public const string IndividualScope = "Individual";
        public const string InspectorTitle = "Fit summary";
        public const string RmsdLabel = "RMSD";
        public const string NoFitText = "No fit result for the selected experiment.";

        public const string ModelToolTip = "Fitted model";
        public const string ParameterBoxToolTip = "Draw the fitted parameter box on the graph";
        public const string LargeParameterTextToolTip = "Use larger text in the graph parameter box";
        public const string ParameterGuidesToolTip = "Draw fitted stoichiometry and enthalpy guide lines";

        /// <summary>
        /// Parameter categories shown by the inspector summary. The summary
        /// always shows these, independent of the graph box preference.
        /// </summary>
        public const FinalFigureDisplayParameters InspectorDisplay =
            FinalFigureDisplayParameters.Model
            | FinalFigureDisplayParameters.Fitted
            | FinalFigureDisplayParameters.Derived;

        /// <summary>
        /// Parameter categories drawn in the analysis graph's parameter box. The
        /// model header and fitted parameters are always included; the preference
        /// adds optional categories such as derived parameters.
        /// </summary>
        public static FinalFigureDisplayParameters GraphBoxDisplay(FinalFigureDisplayParameters preference)
            => preference
               | FinalFigureDisplayParameters.Model
               | FinalFigureDisplayParameters.Fitted;

        public static AnalysisFitSummary BuildInspectorSummary(SolutionInterface solution)
            => AnalysisFitSummary.Create(BuildRows(solution, InspectorDisplay));

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
                    ? $"{row.Label} | {RmsdLabel} = {row.Value}"
                    : $"{row.Label} = {row.Value}");
            }

            return lines;
        }
    }
}
