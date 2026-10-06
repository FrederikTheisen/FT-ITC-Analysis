using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class ClassifiedOutputEvidenceTests
{
    [Fact]
    public void MixedExportDoesNotReadNegativeBindingParametersEvenForColumnHeaders()
    {
        var negative = NegativeResult();
        var original = negative.Solution.Solutions[0];
        var throwing = new ThrowingReportSolution(original.Model);
        typeof(SolutionInterface).GetProperty(nameof(SolutionInterface.Convergence))!.GetSetMethod(true)!
            .Invoke(throwing, new object[] { original.Convergence });
        foreach (var parameter in original.Parameters) throwing.Parameters.Add(parameter.Key, parameter.Value);
        original.Model.Solution = throwing;
        var positive = NegativeResult();
        positive.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);

        var table = AnalysisResultTableExporter.Build(new[] { negative, positive }, new AnalysisResultExportOptions());
        Assert.Contains("No binding detected", table);
        Assert.Contains("Binding detected", table);
        Assert.Throws<InvalidOperationException>(() => AnalysisReportBuilder.Build(new[] { negative, positive }));
        var report = new AnalysisReport();
        report.SetResultIds(new[] { negative.UniqueID });
        Assert.Throws<InvalidOperationException>(() => AnalysisInterpretationPackageBuilder.Build(report, negative));
        Assert.Throws<InvalidOperationException>(() =>
            AnalysisReportBuilder.Build(negative, new AnalysisReportOptions { OutputPurpose = ResultOutputPurpose.Diagnostic }));
    }

    [Fact]
    public void RetainedInterpretationShowsNoAssessmentContextWarningsAndKeepsText()
    {
        var result = NegativeResult();
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        const string text = "Retained interpretation statement.";
        report.SetManualInterpretation(text);
        var savedText = report.ApprovedInterpretation.InterpretationMarkdown;
        var unknown = AnalysisReportBuilder.Build(report, _ => result);
        Assert.DoesNotContain(unknown.Sections.SelectMany(section => section.Blocks), block =>
            block is AnalysisReportNoticeBlock notice && notice.Title == "Assessment context unknown");
        Assert.Contains(text, savedText);
        Assert.Equal(savedText, report.ApprovedInterpretation.InterpretationMarkdown);

        var context = AnalysisInterpretationPackageBuilder.AssessmentContextFingerprintFromResults(new[] { result });
        report.UpdateApprovedInterpretationText(savedText, context);
        var oldPrompt = AnalysisInterpretationPromptBuilder.Build(AnalysisInterpretationPackageBuilder.Build(report, result));
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
        var newPrompt = AnalysisInterpretationPromptBuilder.Build(AnalysisInterpretationPackageBuilder.Build(report, result));
        Assert.NotEqual(oldPrompt.EvidenceFingerprint, newPrompt.EvidenceFingerprint);
        Assert.NotEqual(oldPrompt.InputFingerprint, newPrompt.InputFingerprint);
        var changed = AnalysisReportBuilder.Build(report, _ => result);
        Assert.DoesNotContain(changed.Sections.SelectMany(section => section.Blocks), block =>
            block is AnalysisReportNoticeBlock notice && notice.Title == "Assessment context changed");
        Assert.Equal(savedText, report.ApprovedInterpretation.InterpretationMarkdown);
        Assert.Equal(context, report.ApprovedInterpretation.AssessmentContextFingerprint);
    }

    [Fact]
    public void AutomaticNegativeReportsAutomaticModeAndSavedComparison()
    {
        var result = NegativeResult();
        var comparison = Comparison(result.Solution.Solutions[0].Data.UniqueID);
        SetComparison(result, comparison);
        typeof(AnalysisResult).GetProperty(nameof(AnalysisResult.BindingAssessment))!.GetSetMethod(true)!
            .Invoke(result, new object[] { BindingAssessmentState.FromComparison(comparison) });

        Assert.False(result.BindingAssessment.IsManual);
        Assert.Equal(BindingAssessmentOutcome.Inconclusive, result.BindingAssessment.EffectiveOutcome);
        var report = AnalysisReportBuilder.Build(result);
        Assert.Contains(report.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportTableBlock>(),
            block => block.Title == "Fitted and derived parameters");
        Assert.Contains(report.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportNoticeBlock>(),
            block => block.Message.Contains("Inconclusive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NegativeOutputBypassesNonfiniteBindingParametersBeforeValidationAndUnitSelection()
    {
        var result = NegativeResult();
        var solution = result.Solution.Solutions[0];
        solution.Parameters[ParameterType.Enthalpy1] = FloatWithError.NaN;
        solution.Parameters[ParameterType.Affinity1] = FloatWithError.NaN;
        solution.Parameters[ParameterType.Nvalue1] = FloatWithError.NaN;

        Assert.True(AnalysisReportBuilder.Validate(result).IsValid);
        Assert.True(AnalysisReportBuilder.Validate(result, ResultOutputPurpose.Diagnostic).IsValid);
        var document = AnalysisReportBuilder.Build(result);
        Assert.NotEmpty(document.Sections);
        Assert.DoesNotContain(document.Diagnostics, diagnostic =>
            diagnostic.Severity == AnalysisReportDiagnosticSeverity.Error);
        var table = AnalysisResultTableExporter.Build(new[] { result }, new AnalysisResultExportOptions());
        Assert.DoesNotContain("N_value", table);
        Assert.Contains("No binding detected", table);
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        var evidence = AnalysisInterpretationPackageBuilder.Build(report, result);
        Assert.All(evidence.Result.Experiments, experiment => Assert.NotEmpty(experiment.Parameters));
        var figure = PublicationFigureBuilder.Build(new PublicationFigureSource(solution.Data, solution, result),
            new PublicationFigureOptions());
        Assert.NotNull(figure.FitPanel);
        Assert.Empty(figure.FitPanel.Bands);
    }

    [Theory]
    [InlineData(EnergyUnitFamily.Joules, EnergyUnit.Joule, 1d)]
    [InlineData(EnergyUnitFamily.Calories, EnergyUnit.Cal, 1d / 4.184d)]
    public void UnequalSavedInjectionAmountsDeterminePredictionResidualAndHeatTableUnits(
        EnergyUnitFamily family, EnergyUnit unit, double molarScale)
    {
        var result = NegativeResult();
        var solution = result.Solution.Solutions[0];
        var comparison = Comparison(solution.Data.UniqueID);
        SetComparison(result, comparison);
        var originalCriteria = comparison.NullInformationCriteria;
        // Change the current observations and concentration after the saved fit.
        solution.Data.Injections[0].SetPeakArea(new FloatWithError(9e-3));
        solution.Data.SyringeConcentration = new FloatWithError(100e-3);
        solution.Data.Injections[0].Ratio = 999;

        var figure = PublicationFigureBuilder.Build(new PublicationFigureSource(solution.Data, solution, result),
            new PublicationFigureOptions
            {
                EnergyUnitFamily = family, EnergyUnitOverride = unit,
                ShowResiduals = true, DrawFitOffsetCorrected = true, ShowConfidenceBand = true,
            });
        // The figure draws the fitted Offset on the current observations: q/m − b and residual q/m − b.
        var fit = Assert.IsType<PublicationFigurePanel>(figure.FitPanel);
        var line = Assert.Single(fit.Series);
        Assert.Equal(solution.Data.Injections.Select(injection => injection.Ratio), fit.Points.Select(point => point.X));
        Assert.Equal(999, fit.Points[0].X);
        var current = solution.Data.Injections[0];
        Assert.Equal((9e-3 / current.InjectionMass - 1234) * molarScale, fit.Points[0].Y, 6);
        Assert.All(line.Points, point => Assert.Equal(0, point.Y, 8));
        Assert.Empty(fit.Bands);
        var residuals = Assert.IsType<PublicationFigurePanel>(figure.ResidualPanel);
        Assert.Equal((9e-3 - 1234 * current.InjectionMass) / current.InjectionMass * molarScale, residuals.Points[0].Y, 6);
        Assert.DoesNotContain(fit.AnnotationBoxes.SelectMany(box => box.Lines),
            label => label.Contains("Saved", StringComparison.Ordinal) || label.Contains("Current", StringComparison.Ordinal));

        var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
        {
            EnergyUnitFamily = family, EnergyUnitOverride = unit, IncludeInjectionTables = true,
        });
        // Standard output matches the Offset figure: saved null predictions, not the binding fit.
        var evidence = Assert.Single(document.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportTableBlock>(), item => item.Title == "Saved null comparison injection evidence");
        var heatScale = ThermogramUnits.GetIntegratedHeatScale(family);
        Assert.Equal(1234 * 2e-9 * heatScale, Parse(evidence.Rows[0].Cells[5]), 5);
        Assert.Equal(100 * 2e-9 * heatScale, Parse(evidence.Rows[0].Cells[6]), 5);
        Assert.DoesNotContain(document.Sections.SelectMany(section => section.Blocks),
            block => block.Title == "Injection table");

        var diagnostic = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
        {
            EnergyUnitFamily = family, EnergyUnitOverride = unit, IncludeInjectionTables = true,
            OutputPurpose = ResultOutputPurpose.Diagnostic,
        });
        var table = Assert.Single(diagnostic.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportTableBlock>(), item => item.Title == "Injection table");
        Assert.Contains(table.Columns, column => column.Id == "FittedHeat");
        Assert.Contains(table.Columns, column => column.Id == "Residual");
        Assert.Same(originalCriteria, result.NullComparison.NullInformationCriteria);
        Assert.Equal(3, result.NullComparison.DeltaAicc);
    }

    [Fact]
    public void IndependentMemberRowsDoNotUsePooledComparisonEvidence()
    {
        var first = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var second = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        first.Data.Name = "First member";
        second.Data.Name = "Second member";
        var model = new GlobalModel(new List<Model> { first, second })
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        model.Parameters.AddIndivdualParameter(first.Parameters);
        model.Parameters.AddIndivdualParameter(second.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = model },
            new List<SolutionInterface> { first.Solution, second.Solution }, first.Solution.Convergence,
            reconstructBootstrap: false);
        model.Solution = global;
        var result = new AnalysisResult(global);
        var comparison = Comparison(first.Data.UniqueID);
        comparison.Members.Add(new NullModelComparisonMember
        {
            ExperimentId = second.Data.UniqueID, Offset = 2345, Scope = "locked",
        });
        SetComparison(result, comparison);
        var table = AnalysisResultTableExporter.Build(new[] { result }, new AnalysisResultExportOptions
        {
            RowMode = AnalysisResultExportRowMode.AllRows, FileFormat = AnalysisResultExportFileFormat.TSV,
            EnergyUnitOverride = EnergyUnit.Joule,
        });
        var lines = table.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var headers = lines[0].Split('\t');
        var rows = lines.Skip(1).Select(line => line.Split('\t')).ToList();
        Assert.Equal(2, rows.Count);
        foreach (var row in rows)
        {
            Assert.Equal("Not calculated", row[Array.IndexOf(headers, "Binding AICc")]);
            Assert.Equal("Not calculated", row[Array.IndexOf(headers, "Null AICc")]);
            Assert.Equal("Not calculated", row[Array.IndexOf(headers, "ΔAICc")]);
            Assert.Equal("Not assessed", row[Array.IndexOf(headers, "Binding assessment")]);
        }
        Assert.DoesNotContain("Pooled result-level comparison", table);
        var report = AnalysisReportBuilder.Build(result);
        var evidence = report.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
            .SelectMany(block => block.Items).ToList();
        Assert.DoesNotContain(evidence, item => item.Value is "100" or "103" or "+3");
        Assert.DoesNotContain(evidence, item => item.Value.Contains("Pooled result-level comparison", StringComparison.Ordinal));
    }

    static AnalysisResult NegativeResult()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        return result;
    }

    static NullModelComparison Comparison(string id) => new()
    {
        BindingFitSucceeded = true, NullFitSucceeded = true, DeltaAicc = 3,
        BindingInformationCriteria = Criteria(100), NullInformationCriteria = Criteria(103),
        Members = new List<NullModelComparisonMember>
        {
            new NullModelComparisonMember
            {
                ExperimentId = id, Offset = 1234, Scope = "shared",
                Points = new List<NullModelComparisonPoint>
                {
                    new() { InjectionId = 0, Ratio = 2, InjectionMass = 2e-9,
                        ObservedHeatJoules = 1334 * 2e-9, PredictedHeatJoules = 1234 * 2e-9, Included = true },
                    new() { InjectionId = 1, Ratio = 7, InjectionMass = 5e-9,
                        ObservedHeatJoules = 1134 * 5e-9, PredictedHeatJoules = 1234 * 5e-9, Included = true },
                },
            },
        },
    };

    static FitInformationCriteria Criteria(double aicc) => FitInformationCriteria.Restore(
        observationCount: 20, fittedParameterCount: 2, likelihoodParameterCount: 3,
        likelihoodMode: GaussianLikelihoodMode.EstimatedCommonVariance, minusTwoLogLikelihood: 90,
        aic: aicc - 4, aicc: aicc, isAicAvailable: true, isAiccAvailable: true,
        aicUnavailableReason: "", aiccUnavailableReason: "", rawResidualSumOfSquares: 1,
        residualRmsdMicrojoules: 1, standardizedResidualSumOfSquares: 1, logSigmaSquaredSum: 1);

    static void SetComparison(AnalysisResult result, NullModelComparison comparison) =>
        typeof(AnalysisResult).GetProperty(nameof(AnalysisResult.NullComparison))!.GetSetMethod(true)!
            .Invoke(result, new object[] { comparison });
    static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    sealed class ThrowingReportSolution : OneSetOfSites.ModelSolution
    {
        public ThrowingReportSolution(Model model) : base(model) { }
        public override Dictionary<ParameterType, FloatWithError> ReportParameters =>
            throw new InvalidOperationException("Suppressed binding parameters must not be read.");
    }
}
