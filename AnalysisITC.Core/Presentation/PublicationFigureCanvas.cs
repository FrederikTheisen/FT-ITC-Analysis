using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Presentation
{
    public sealed class PublicationFigureCanvasOptions
    {
        public double PlotWidthCentimeters { get; set; } = 5;
        public double PlotHeightCentimeters { get; set; } = 7.7;
        public double FontSize { get; set; } = 10;
        public double SymbolSize { get; set; } = 4;
        public double StrokeWidth { get; set; } = 1;
        public int Columns { get; set; } = 3;
        public int Rows { get; set; } = 3;
        public bool ShowPanelLetters { get; set; } = true;
        public bool GroupResultFigures { get; set; } = true;
        public bool ShowInformationBoxes { get; set; } = true;

        public int Capacity => Math.Max(0, Columns) * Math.Max(0, Rows);

        public string Validate()
        {
            if (PlotWidthCentimeters < 1 || PlotWidthCentimeters > 20)
                return "Plot width must be between 1 and 20 cm.";
            if (PlotHeightCentimeters < 2 || PlotHeightCentimeters > 28)
                return "Plot height must be between 2 and 28 cm.";
            if (FontSize < 5 || FontSize > 24)
                return "Font size must be between 5 and 24 pt.";
            if (SymbolSize < 3 || SymbolSize > 14)
                return "Data point size must be between 3 and 14 pt.";
            if (Math.Abs(StrokeWidth - 0.5) > 0.001 && Math.Abs(StrokeWidth - 1) > 0.001)
                return "Stroke width must be either 0.5 or 1 pt.";
            if (Columns < 1 || Columns > 6)
                return "Columns must be between 1 and 6.";
            if (Rows < 1 || Rows > 10)
                return "Rows must be between 1 and 10.";
            return "";
        }
    }

    public sealed class PublicationFigureCanvasCell
    {
        internal PublicationFigureCanvasCell(PublicationFigureSource source, int row, int column, int groupIndex, string panelLabel)
        {
            Source = source;
            Row = row;
            Column = column;
            GroupIndex = groupIndex;
            PanelLabel = panelLabel ?? "";
        }

        public PublicationFigureSource Source { get; private set; }
        public int Row { get; private set; }
        public int Column { get; private set; }
        public int GroupIndex { get; private set; }
        public string PanelLabel { get; private set; }
    }

    public sealed class PublicationFigureCanvasDocument
    {
        internal PublicationFigureCanvasDocument(PublicationFigureCanvasOptions options, PublicationFigureOptions figureOptions)
        {
            Options = options;
            FigureOptions = figureOptions;
        }

        public PublicationFigureCanvasOptions Options { get; private set; }
        public PublicationFigureOptions FigureOptions { get; private set; }
        public List<PublicationFigureCanvasCell> Cells { get; private set; } = new List<PublicationFigureCanvasCell>();
        public string ValidationError { get; internal set; } = "";
        public bool IsValid => string.IsNullOrWhiteSpace(ValidationError);
    }

    public sealed class PublicationFigureCanvasLayoutResult
    {
        public PublicationFigureCanvasLayoutResult(
            double plotWidthCentimeters,
            double plotHeightCentimeters,
            double figureWidthCentimeters,
            double figureHeightCentimeters,
            string validationError)
        {
            PlotWidthCentimeters = plotWidthCentimeters;
            PlotHeightCentimeters = plotHeightCentimeters;
            FigureWidthCentimeters = figureWidthCentimeters;
            FigureHeightCentimeters = figureHeightCentimeters;
            ValidationError = validationError ?? "";
        }

        public double PlotWidthCentimeters { get; private set; }
        public double PlotHeightCentimeters { get; private set; }
        public double FigureWidthCentimeters { get; private set; }
        public double FigureHeightCentimeters { get; private set; }
        public string ValidationError { get; private set; }
        public bool IsValid => string.IsNullOrWhiteSpace(ValidationError);
    }

    public static class PublicationFigureCanvasBuilder
    {
        public static PublicationFigureCanvasDocument Build(
            IEnumerable<ITCDataContainer> orderedSelections,
            PublicationFigureOptions figureOptions,
            PublicationFigureCanvasOptions canvasOptions)
        {
            figureOptions = figureOptions ?? new PublicationFigureOptions();
            canvasOptions = canvasOptions ?? new PublicationFigureCanvasOptions();
            var document = new PublicationFigureCanvasDocument(canvasOptions, figureOptions)
            {
                ValidationError = canvasOptions.Validate()
            };
            if (!document.IsValid) return document;

            var expanded = Expand(orderedSelections, canvasOptions.GroupResultFigures).ToList();
            if (expanded.Count == 0)
            {
                document.ValidationError = "Add at least one experiment or analysis result.";
                return document;
            }

            if (expanded.Count > canvasOptions.Capacity)
            {
                document.ValidationError = $"The selection contains {expanded.Count} figures, but the {canvasOptions.Columns} × {canvasOptions.Rows} grid holds {canvasOptions.Capacity}.";
                return document;
            }

            var firstCellForGroup = new HashSet<int>();
            for (var index = 0; index < expanded.Count; index++)
            {
                var entry = expanded[index];
                var first = firstCellForGroup.Add(entry.GroupIndex);
                var label = canvasOptions.ShowPanelLetters && first ? PanelLabel(entry.GroupIndex) : "";
                document.Cells.Add(new PublicationFigureCanvasCell(
                    entry.Source,
                    index / canvasOptions.Columns,
                    index % canvasOptions.Columns,
                    entry.GroupIndex,
                    label));
            }

            return document;
        }

        static IEnumerable<ExpandedSource> Expand(IEnumerable<ITCDataContainer> selections, bool groupResults)
        {
            var groupIndex = 0;
            foreach (var selection in selections ?? Enumerable.Empty<ITCDataContainer>())
            {
                if (selection is ExperimentData experiment)
                {
                    yield return new ExpandedSource(new PublicationFigureSource(experiment, experiment.Solution), groupIndex++);
                    continue;
                }

                if (!(selection is AnalysisResult result) || result.Solution?.Solutions == null) continue;

                var resultGroup = groupIndex;
                var added = false;
                foreach (var solution in result.Solution.Solutions.Where(item => item?.Data != null))
                {
                    var sourceGroup = groupResults ? resultGroup : groupIndex;
                    yield return new ExpandedSource(new PublicationFigureSource(solution.Data, solution), sourceGroup);
                    added = true;
                    if (!groupResults) groupIndex++;
                }

                if (groupResults && added) groupIndex++;
            }
        }

        static string PanelLabel(int index)
        {
            var label = "";
            var value = index + 1;
            while (value > 0)
            {
                value--;
                label = (char)('A' + value % 26) + label;
                value /= 26;
            }
            return label;
        }

        sealed class ExpandedSource
        {
            public ExpandedSource(PublicationFigureSource source, int groupIndex)
            {
                Source = source;
                GroupIndex = groupIndex;
            }

            public PublicationFigureSource Source { get; private set; }
            public int GroupIndex { get; private set; }
        }
    }
}
