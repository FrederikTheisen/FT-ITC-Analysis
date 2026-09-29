using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class PublicationFigureCanvasPaginationTests
    {
        [Theory]
        [InlineData(3, 1)]
        [InlineData(4, 1)]
        [InlineData(5, 2)]
        [InlineData(9, 3)]
        public void PageCountMatchesGridCapacity(int panelCount, int expectedPages)
        {
            var selections = Enumerable.Range(0, panelCount)
                .Select(index => (ITCDataContainer)new ExperimentData($"count-{index}.itc"));
            var document = PublicationFigureCanvasBuilder.Build(selections, new PublicationFigureOptions(),
                new PublicationFigureCanvasOptions { Columns = 2, Rows = 2 });

            Assert.True(document.IsValid, document.ValidationError);
            Assert.Equal(expectedPages, document.PageCount);
        }

        [Fact]
        public void BuilderContinuesPanelsAcrossPagesInFigureOrder()
        {
            var selections = Enumerable.Range(0, 5)
                .Select(index => (ITCDataContainer)new ExperimentData($"page-{index}.itc"))
                .ToList();
            var options = new PublicationFigureCanvasOptions { Columns = 2, Rows = 2 };

            var document = PublicationFigureCanvasBuilder.Build(selections, new PublicationFigureOptions(), options);

            Assert.True(document.IsValid, document.ValidationError);
            Assert.Equal(2, document.PageCount);
            Assert.Collection(document.Cells,
                cell => AssertCell(cell, 0, 0, 0, "A"),
                cell => AssertCell(cell, 0, 0, 1, "B"),
                cell => AssertCell(cell, 0, 1, 0, "C"),
                cell => AssertCell(cell, 0, 1, 1, "D"),
                cell => AssertCell(cell, 1, 0, 0, "E"));
            Assert.Equal(4, document.CreatePageDocument(0).Cells.Count);
            Assert.Single(document.CreatePageDocument(1).Cells);
        }

        [Fact]
        public void GroupedResultCanCrossPageBoundaryAndKeepsFirstOnlyLabel()
        {
            var firstExperiment = new ExperimentData("before-result.itc");
            var result = CreateResult(3);
            var document = PublicationFigureCanvasBuilder.Build(
                new ITCDataContainer[] { firstExperiment, result },
                new PublicationFigureOptions(),
                new PublicationFigureCanvasOptions { Columns = 2, Rows = 1, GroupResultFigures = true });

            Assert.True(document.IsValid, document.ValidationError);
            Assert.Equal(2, document.PageCount);
            Assert.Equal(new[] { 0, 0, 1, 1 }, document.Cells.Select(cell => cell.PageIndex));
            Assert.Equal(new[] { "A", "B", "", "" }, document.Cells.Select(cell => cell.PanelLabel));
            Assert.Equal(document.Cells[1].GroupIndex, document.Cells[2].GroupIndex);
            Assert.Equal(document.Cells[2].GroupIndex, document.Cells[3].GroupIndex);
        }

        static AnalysisResult CreateResult(int memberCount)
        {
            var models = new List<Model>();
            var solutions = new List<SolutionInterface>();
            for (var index = 0; index < memberCount; index++)
            {
                var data = new ExperimentData($"result-member-{index}.itc")
                {
                    MeasuredTemperature = 25,
                    CellConcentration = new FloatWithError(35e-6),
                    SyringeConcentration = new FloatWithError(420e-6),
                    CellVolume = 1.4e-3
                };
                var injection = new InjectionData(data, 0, 2e-6, data.SyringeConcentration * 2e-6, include: true)
                {
                    ActualCellConcentration = data.CellConcentration,
                    ActualTitrantConcentration = new FloatWithError(5e-6),
                    Ratio = 1
                };
                injection.SetPeakArea(new FloatWithError(-2e-6, 1e-8));
                data.Injections.Add(injection);
                var model = new OneSetOfSites(data);
                model.InitializeParameters(data);
                model.ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None };
                var solution = SolutionInterface.FromModel(model, null);
                model.Solution = solution;
                models.Add(model);
                solutions.Add(solution);
            }

            var globalModel = new GlobalModel(models)
            {
                Parameters = new GlobalModelParameters(),
                ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None }
            };
            foreach (var solution in solutions)
                globalModel.Parameters.AddIndivdualParameter(solution.Model.Parameters);
            var global = new GlobalSolution(new GlobalSolver { Model = globalModel }, solutions, null, reconstructBootstrap: false);
            globalModel.Solution = global;
            return new AnalysisResult(global);
        }

        static void AssertCell(PublicationFigureCanvasCell cell, int page, int row, int column, string label)
        {
            Assert.Equal(page, cell.PageIndex);
            Assert.Equal(row, cell.Row);
            Assert.Equal(column, cell.Column);
            Assert.Equal(label, cell.PanelLabel);
        }
    }
}
