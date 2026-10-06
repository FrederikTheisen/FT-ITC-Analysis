using System;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class ExplicitSolutionEvidenceTests
{
    [Theory]
    [InlineData(EnergyUnitFamily.Joules, EnergyUnit.Joule, 1)]
    [InlineData(EnergyUnitFamily.Calories, EnergyUnit.Cal, 0.2390057361376673)]
    public void InjectionTableUsesSuppliedOffsetAndObservedMinusPredicted(
        EnergyUnitFamily family, EnergyUnit unit, double scale)
    {
        var attached = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var data = attached.Data;
        var offset = new Offset(data);
        offset.InitializeParameters(data);
        offset.Parameters.AddOrUpdateParameter(ParameterType.Offset, 1234);
        var supplied = new Offset.ModelSolution(offset);
        data.Injections[0].SetPeakArea(new FloatWithError(1334 * data.Injections[0].InjectionMass));
        var table = ExperimentOverviewTable.Build(data, supplied, family, unit);
        Assert.Equal(1234 * scale, Parse(table.Rows[0]["FittedHeat"]), 2);
        Assert.Equal(100 * scale, Parse(table.Rows[0]["Residual"]), 3);
        Assert.Equal(1334 * scale, Parse(table.Rows[0]["Heat"]), 2);
        Assert.True(table.Columns.Single(column => column.Id == "FittedHeat").IsVisible);
        Assert.Same(attached.Solution, data.Solution);
    }

    [Fact]
    public void AutomaticTableUnitsUseOnlyObservationAndSuppliedSolutionValues()
    {
        var attached = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        foreach (var injection in attached.Data.Injections)
            injection.SetPeakArea(new FloatWithError(10 * injection.InjectionMass));
        var offset = new Offset(attached.Data);
        offset.InitializeParameters(attached.Data);
        offset.Parameters.AddOrUpdateParameter(ParameterType.Offset, 20);
        var solution = new Offset.ModelSolution(offset);
        var table = ExperimentOverviewTable.Build(attached.Data, solution, EnergyUnitFamily.Joules);
        Assert.Equal("Heat (J/mol)", table.Columns.Single(column => column.Id == "Heat").Title);
        var observations = ExperimentOverviewTable.Build(attached.Data, null, EnergyUnitFamily.Joules);
        Assert.Equal("Heat (J/mol)", observations.Columns.Single(column => column.Id == "Heat").Title);
        offset.Parameters.AddOrUpdateParameter(ParameterType.Offset, 1000);
        var kilo = ExperimentOverviewTable.Build(attached.Data, solution, EnergyUnitFamily.Joules);
        Assert.Equal("Heat (kJ/mol)", kilo.Columns.Single(column => column.Id == "Heat").Title);
    }

    [Fact]
    public void ExplicitNullKeepsSupportingObservationsWithoutAttachedFit()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var table = ExperimentOverviewTable.Build(model.Data, null, EnergyUnitFamily.Joules);
        Assert.NotEmpty(table.Rows);
        Assert.All(table.Rows, row => { Assert.Equal("", row["FittedHeat"]); Assert.Equal("", row["Residual"]); });
        Assert.All(table.Columns.Where(column => column.Id is "FittedHeat" or "Residual"), column => Assert.False(column.IsVisible));
        Assert.NotEmpty(table.Rows[0]["Heat"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedReportChaptersUseTheirOwnSavedOffsetForInjectionTables(bool condense)
    {
        var attached = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var data = attached.Data;
        foreach (var injection in data.Injections)
            injection.SetPeakArea(new FloatWithError(1334 * injection.InjectionMass));
        var first = OffsetResult(data, 1234, attached.Solution.Convergence);
        var second = OffsetResult(data, 2345, attached.Solution.Convergence);
        var document = AnalysisReportBuilder.Build(new[] { first, second }, new AnalysisReportOptions
        {
            IncludeInjectionTables = true,
            EnergyUnitOverride = EnergyUnit.Joule,
            CondenseRepeatedExperiments = condense,
        });
        Assert.True(document.IsValid);
        var chapters = document.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment).ToList();
        Assert.Equal(2, chapters.Count);
        var expectedFits = new[] { 1234d, 2345d };
        var expectedResiduals = new[] { 100d, -1011d };
        for (var index = 0; index < chapters.Count; index++)
        {
            var table = Assert.Single(chapters[index].Blocks.OfType<AnalysisReportTableBlock>(),
                block => block.Title == "Injection table");
            var fitColumn = table.Columns.ToList().FindIndex(column => column.Id == "FittedHeat");
            var residualColumn = table.Columns.ToList().FindIndex(column => column.Id == "Residual");
            Assert.True(fitColumn >= 0);
            Assert.True(residualColumn >= 0);
            Assert.All(table.Rows, row =>
            {
                Assert.Equal(expectedFits[index], Parse(row.Cells[fitColumn]), 4);
                Assert.Equal(expectedResiduals[index], Parse(row.Cells[residualColumn]), 4);
            });
        }
        Assert.Equal(condense, chapters[1].Blocks.OfType<AnalysisReportKeyValueBlock>()
            .Any(block => block.Title == "Experiment details — condensed"));
        Assert.Same(attached.Solution, data.Solution);
    }

    [Fact]
    public void SupportingReportInjectionTableOmitsAttachedFitAndResidual()
    {
        var resultModel = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = resultModel }));
        var supportingModel = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        supportingModel.Data.SetID(Guid.NewGuid().ToString());
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        report.SetSupportingExperimentIds(new[] { supportingModel.Data.UniqueID });
        var document = AnalysisReportBuilder.Build(report,
            id => id == result.UniqueID ? result : null,
            id => id == supportingModel.Data.UniqueID ? supportingModel.Data : null,
            new AnalysisReportOptions { IncludeInjectionTables = true, EnergyUnitOverride = EnergyUnit.Joule });
        Assert.True(document.IsValid);
        var supportingSection = Assert.Single(document.Sections,
            section => section.Kind == AnalysisReportSectionKind.SupportingData);
        var table = Assert.Single(supportingSection.Blocks.OfType<AnalysisReportTableBlock>(),
            block => block.Title == "Injection table");
        Assert.NotEmpty(table.Rows);
        Assert.DoesNotContain(table.Columns, column => column.Id is "FittedHeat" or "Residual");
        var heatColumn = table.Columns.ToList().FindIndex(column => column.Id == "Heat");
        Assert.True(heatColumn >= 0);
        Assert.NotEmpty(table.Rows[0].Cells[heatColumn]);
        Assert.Same(supportingModel.Solution, supportingModel.Data.Solution);
    }

    [Theory]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected)]
    [InlineData(BindingAssessmentOutcome.BindingDetected)]
    public void InterpretationRetainsParametersAlongsideMemberAssessment(BindingAssessmentOutcome outcome)
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
        result.SetBindingAssessmentOverride(outcome);
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        var package = AnalysisInterpretationPackageBuilder.Build(report, result);
        var member = Assert.Single(package.Result.Experiments);
        Assert.NotEmpty(member.Parameters);
        Assert.Equal(outcome.ToString(), package.Result.BindingAssessment.EffectiveOutcome);
        var prompt = AnalysisInterpretationPromptBuilder.Build(package);
        Assert.Contains("attempted-model estimates", prompt.ResponseFormatInstructions);
        Assert.Contains("Inconclusive permits parameter and uncertainty discussion", prompt.ResponseFormatInstructions);
        Assert.DoesNotContain("NoBindingDetected and Inconclusive suppress", prompt.ResponseFormatInstructions);
    }

    [Fact]
    public void MixedIndependentInterpretationRetainsEveryMemberAndAssessment()
    {
        var models = new[]
        {
            InjectionProcessingMethodTests.FittedModel(bootstrap: false),
            InjectionProcessingMethodTests.FittedModel(bootstrap: false),
        };
        var globalModel = new GlobalModel(models.ToList()) { Parameters = new GlobalModelParameters() };
        foreach (var model in models) globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = globalModel },
            models.Select(model => model.Solution).ToList(), models[0].Solution.Convergence,
            reconstructBootstrap: false);
        globalModel.Solution = global;
        var result = new AnalysisResult(global);
        result.SetMemberBindingAssessmentOverride(models[0].Solution.Guid, BindingAssessmentOutcome.NoBindingDetected);
        result.SetMemberBindingAssessmentOverride(models[1].Solution.Guid, BindingAssessmentOutcome.BindingDetected);
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        var package = AnalysisInterpretationPackageBuilder.Build(report, result);
        Assert.Equal(2, package.Result.Experiments.Count);
        Assert.All(package.Result.Experiments, member => Assert.NotEmpty(member.Parameters));
        Assert.Equal(new[] { "NoBindingDetected", "BindingDetected" },
            package.Result.BindingAssessment.Members.Select(member => member.EffectiveOutcome));
        Assert.Empty(package.Result.TemperatureDependence);
    }

    static AnalysisResult OffsetResult(ExperimentData data, double offsetValue, SolverConvergence convergence)
    {
        var model = new Offset(data);
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, offsetValue);
        model.Solution = SolutionInterface.FromModel(model, convergence);
        return new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
    }

    static double Parse(string text) => double.Parse(text, CultureInfo.CurrentCulture);
}
