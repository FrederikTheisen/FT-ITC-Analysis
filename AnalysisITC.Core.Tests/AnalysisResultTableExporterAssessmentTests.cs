using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class AnalysisResultTableExporterAssessmentTests
{
    [Theory]
    [InlineData(AnalysisResultExportErrorStyle.ValueWithError)]
    [InlineData(AnalysisResultExportErrorStyle.SeparateColumns)]
    public void MissingParameterCellsStayBlankWhileZeroRemainsNumeric(AnalysisResultExportErrorStyle errorStyle)
    {
        var result = FractionalResult();
        var member = result.Solution.Solutions[0];
        member.Parameters[ParameterType.Nvalue1] = FloatWithError.NaN;
        member.Parameters[ParameterType.Enthalpy1] = new FloatWithError(0);
        var table = Lines(AnalysisResultTableExporter.Build(new[] { result }, new AnalysisResultExportOptions
        {
            RowMode = AnalysisResultExportRowMode.AllRows,
            FileFormat = AnalysisResultExportFileFormat.TSV,
            ErrorStyle = errorStyle,
            UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
            EnergyUnitOverride = EnergyUnit.KiloJoule,
        }));
        var header = table[0];
        var row = table[1];
        var suffixes = errorStyle == AnalysisResultExportErrorStyle.SeparateColumns
            ? new[] { "_value", "_sd", "_ci_lower", "_ci_upper" } : new[] { "" };

        foreach (var suffix in suffixes)
            Assert.Equal("", row[Array.IndexOf(header, "N" + suffix)]);
        var valueSuffix = errorStyle == AnalysisResultExportErrorStyle.SeparateColumns ? "_value" : "";
        Assert.Equal(0, double.Parse(row[Array.IndexOf(header, "∆H (kJ/mol)" + valueSuffix)], CultureInfo.InvariantCulture));
        Assert.DoesNotContain("Unavailable", row);
    }

    [Fact]
    public void IndependentSummaryShowsCollectionCountsAndLeavesOrdinaryComparisonBlank()
    {
        var result = IndependentResult(out var members, includePooledComparison: true);
        members[0].NullComparison = Comparison(members[0].Data.UniqueID, 0);
        members[1].NullComparison = Comparison(members[1].Data.UniqueID, 15);
        foreach (var member in members)
            result.RestoreMemberComparison(member.Guid, member.NullComparison);

        var table = AnalysisResultTableExporter.Build(new[] { result }, new AnalysisResultExportOptions
        {
            RowMode = AnalysisResultExportRowMode.Summary,
            FileFormat = AnalysisResultExportFileFormat.TSV,
        });
        var lines = Lines(table);
        var headers = lines[0];
        var row = lines[1];

        Assert.Contains("Mixed assessments; 1 of 2 binding detected, 1 of 2 no binding detected",
            row[Array.IndexOf(headers, "Binding assessment")]);
        Assert.Equal("Derived from member assessments", row[Array.IndexOf(headers, "Assessment mode")]);
        foreach (var label in new[] { "Null model", "Null fit", "Comparison scope", "Null model parameters",
                     "Binding AICc", "Null AICc", "ΔAICc", "Assessment reason" })
            Assert.Equal("", row[Array.IndexOf(headers, label)]);
        var rmsdColumn = headers.Select((header, index) => (header, index))
            .Single(item => item.header.StartsWith("Null RMSD (", StringComparison.Ordinal)).index;
        Assert.Equal("", row[rmsdColumn]);
    }

    [Theory]
    [InlineData(0, "No binding detected", false)]
    [InlineData(3, "Inconclusive", true)]
    [InlineData(12, "Inconclusive", true)]
    [InlineData(15, "Binding detected", true)]
    public void IndependentRowsUseEachMemberAssessmentAndOnlySuppressIneligibleMember(
        double firstDelta, string firstAssessment, bool firstAllowed)
    {
        var result = IndependentResult(out var members, includePooledComparison: false);
        members[0].NullComparison = Comparison(members[0].Data.UniqueID, 0);
        members[1].NullComparison = Comparison(members[1].Data.UniqueID, 15);
        foreach (var member in members)
        {
            member.NullComparison = Comparison(member.Data.UniqueID,
                ReferenceEquals(member, members[0]) ? firstDelta : 15);
            member.Parameters[ParameterType.Nvalue1] = new FloatWithError(ReferenceEquals(member, members[0]) ? 111 : 222, 0);
            result.RestoreMemberComparison(member.Guid, member.NullComparison);
        }

        var table = AnalysisResultTableExporter.Build(new[] { result }, new AnalysisResultExportOptions
        {
            RowMode = AnalysisResultExportRowMode.AllRows,
            FileFormat = AnalysisResultExportFileFormat.TSV,
        });
        var lines = Lines(table);
        var headers = lines[0];
        var rows = lines.Skip(1).ToList();
        var assessment = Array.IndexOf(headers, "Binding assessment");
        var value = Array.IndexOf(headers, "N");
        var scope = Array.IndexOf(headers, "Comparison scope");

        Assert.Equal(firstAssessment, rows[0][assessment]);
        Assert.Equal("Binding detected", rows[1][assessment]);
        Assert.Equal(firstAllowed, !string.IsNullOrWhiteSpace(rows[0][value]));
        Assert.NotEqual("", rows[1][value]);
        Assert.Equal("Independent member comparison", rows[0][scope]);
        Assert.Equal("Independent member comparison", rows[1][scope]);
        Assert.All(rows, row => Assert.Equal("Offset", row[Array.IndexOf(headers, "Null model")]));
    }

    [Fact]
    public void DiagnosticSummaryAddsPooledColumnsOnlyForIndependentRows()
    {
        var independent = IndependentResult(out _, includePooledComparison: true);
        var singleModel = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var single = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = singleModel }));
        var table = AnalysisResultTableExporter.Build(new[] { independent, single }, new AnalysisResultExportOptions
        {
            RowMode = AnalysisResultExportRowMode.Summary,
            OutputPurpose = ResultOutputPurpose.Diagnostic,
            FileFormat = AnalysisResultExportFileFormat.TSV,
        });
        var lines = Lines(table);
        var headers = lines[0];
        var pooledIndex = Array.IndexOf(headers, "Pooled diagnostic binding AICc");
        Assert.True(pooledIndex >= 0);
        var independentRow = lines[1];
        var singleRow = lines[2];
        Assert.NotEqual("", independentRow[pooledIndex]);
        Assert.Equal("", singleRow[pooledIndex]);
        Assert.Contains("pooled variance convention", independentRow[Array.IndexOf(headers, "Pooled diagnostic scope")]);
        Assert.Equal("", independentRow[Array.IndexOf(headers, "Binding AICc")]);
    }

    [Theory]
    [InlineData(ResultOutputPurpose.Standard, "", "")]
    [InlineData(ResultOutputPurpose.Diagnostic, "1.75", "3")]
    public void ReducedChiSquareUsesFullPoolAndBindingOutputPolicy(
        ResultOutputPurpose purpose, string expectedPooled, string expectedNoBindingMember)
    {
        var result = IndependentResult(out var members, includePooledComparison: false, includeReducedChiSquare: true);
        result.SetMemberBindingAssessmentOverride(members[0].Guid, BindingAssessmentOutcome.BindingDetected);
        result.SetMemberBindingAssessmentOverride(members[1].Guid, BindingAssessmentOutcome.NoBindingDetected);

        var pooledNull = new NullModelComparison { NullModelId = "offset", NullFitSucceeded = true };
        for (var index = 0; index < members.Count; index++)
        {
            var data = members[index].Data;
            var model = new Offset(data);
            model.InitializeParameters(data);
            var nullSolution = SolutionInterface.FromModel(model, ConvergenceWithReducedChiSquare(
                SolverConvergence.FromFixedFit(0, 0), index == 0 ? 5 : 15, 6, 1));
            var localNull = new NullModelComparison { NullModelId = "offset", NullFitSucceeded = true };
            localNull.NullSolutions.Add(nullSolution);
            localNull.Members.Add(new NullModelComparisonMember { ExperimentId = data.UniqueID, Scope = "local" });
            pooledNull.NullSolutions.Add(nullSolution);
            pooledNull.Members.AddRange(localNull.Members);
            var assessment = result.GetMemberBindingAssessment(members[index]);
            result.RestoreMemberComparisonAndAssessment(members[index].Guid, localNull, assessment);
        }
        result.RestoreNullComparison(pooledNull);

        var options = new AnalysisResultExportOptions
        {
            OutputPurpose = purpose,
            FileFormat = AnalysisResultExportFileFormat.TSV,
            RowMode = AnalysisResultExportRowMode.Summary,
        };
        var summary = Lines(AnalysisResultTableExporter.Build(new[] { result }, options));
        Assert.All(summary.Skip(1), row => Assert.Equal(summary[0].Length, row.Length));
        if (purpose == ResultOutputPurpose.Diagnostic)
        {
            Assert.Equal(expectedPooled, summary[1][Array.IndexOf(summary[0], "Reduced chi-square")]);
            Assert.Equal("", summary[1][Array.IndexOf(summary[0], "Null reduced chi-square")]);
        }
        else
        {
            Assert.DoesNotContain("Reduced chi-square", summary[0]);
            Assert.DoesNotContain("Null reduced chi-square", summary[0]);
        }
        var pooledNullColumn = Array.IndexOf(summary[0], "Pooled diagnostic null reduced chi-square");
        if (purpose == ResultOutputPurpose.Diagnostic)
            Assert.Equal("2", summary[1][pooledNullColumn]);
        else
            Assert.Equal(-1, pooledNullColumn);

        options.RowMode = AnalysisResultExportRowMode.AllRows;
        var rows = Lines(AnalysisResultTableExporter.Build(new[] { result }, options));
        var primaryColumn = Array.IndexOf(rows[0], "Reduced chi-square");
        var nullColumn = Array.IndexOf(rows[0], "Null reduced chi-square");
        Assert.All(rows.Skip(1), row => Assert.Equal(rows[0].Length, row.Length));
        if (purpose == ResultOutputPurpose.Diagnostic)
        {
            Assert.Equal(new[] { "1", expectedNoBindingMember }, rows.Skip(1).Select(row => row[primaryColumn]));
            Assert.Equal(new[] { "1", "3" }, rows.Skip(1).Select(row => row[nullColumn]));
        }
        else { Assert.Equal(-1, primaryColumn); Assert.Equal(-1, nullColumn); }

        var report = AnalysisReportBuilder.Build(result, new AnalysisReportOptions { OutputPurpose = purpose });
        var summaryBlocks = report.Sections.Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().ToList();
        Assert.Equal(expectedPooled, ReadItem(summaryBlocks.Single(block => block.Title == "Model and fit details"),
            "Reduced chi-square"));
        Assert.Equal(purpose == ResultOutputPurpose.Diagnostic ? new[] { "2" } : Array.Empty<string>(),
            summaryBlocks.SelectMany(block => block.Items).Where(item => item.Label == "Null reduced chi-square")
                .Select(item => item.Value));
        var memberBlocks = report.Sections.Where(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Select(section => section.Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Fit details"))
            .ToList();
        Assert.Equal(purpose == ResultOutputPurpose.Diagnostic ? new[] { "1", expectedNoBindingMember } : new[] { "", "" }, memberBlocks.Select(block => ReadItem(block, "Reduced chi-square")));
        Assert.Equal(purpose == ResultOutputPurpose.Diagnostic ? new[] { "1", "3" } : new[] { "", "" }, memberBlocks.Select(block => ReadItem(block, "Null reduced chi-square")));

        if (purpose == ResultOutputPurpose.Diagnostic)
        {
            pooledNull.NullSolutions.RemoveAt(1);
            pooledNull.NullFitSucceeded = false;
            options.RowMode = AnalysisResultExportRowMode.Summary;
            var incomplete = Lines(AnalysisResultTableExporter.Build(new[] { result }, options));
            Assert.Equal("", incomplete[1][pooledNullColumn]);
        }

        static string ReadItem(AnalysisReportKeyValueBlock block, string label)
            => block.Items.SingleOrDefault(item => item.Label == label)?.Value ?? "";
    }

    [Fact]
    public void DesktopOverviewOmitsSavedMemberReducedChiSquare()
    {
        var result = IndependentResult(out _, includePooledComparison: false, includeReducedChiSquare: true);
        var desktop = AnalysisResultOverviewTable.Build(result.PresentationData, EnergyUnitFamily.Joules,
            energyUnitOverride: null, useKelvin: false);
        Assert.DoesNotContain(desktop.Columns, candidate => candidate.Title == "Reduced chi-square");
    }

    public static IEnumerable<object[]> ExportLayouts()
    {
        foreach (var format in Enum.GetValues<AnalysisResultExportFileFormat>())
        foreach (var rows in Enum.GetValues<AnalysisResultExportRowMode>())
        foreach (var errors in Enum.GetValues<AnalysisResultExportErrorStyle>())
        foreach (var purpose in Enum.GetValues<ResultOutputPurpose>())
            yield return new object[] { format, rows, errors, purpose };
    }

    [Theory]
    [MemberData(nameof(ExportLayouts))]
    public void EntireTableUsesDecimalPointsAcrossCultures(
        AnalysisResultExportFileFormat format, AnalysisResultExportRowMode rows,
        AnalysisResultExportErrorStyle errors, ResultOutputPurpose purpose)
    {
        var single = FractionalResult();
        var independent = IndependentResult(out var members, includePooledComparison: false);
        independent.RestoreNullComparison(Comparison(members[0].Data.UniqueID, 12.5, 123.25, 4.184));
        foreach (var member in members)
            independent.RestoreMemberComparison(member.Guid,
                Comparison(member.Data.UniqueID, 12.5, 123.25, 4.184));
        var options = new AnalysisResultExportOptions
        {
            FileFormat = format, RowMode = rows, ErrorStyle = errors, OutputPurpose = purpose,
            UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval,
            EnergyUnitFamily = EnergyUnitFamily.Joules,
            EnergyUnitOverride = EnergyUnit.KiloJoule,
        };
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var expected = AnalysisResultTableExporter.Build(new[] { single, independent }, options);
            Assert.Contains("\"Run, \"\"A\"\"\"", expected);

            foreach (var cultureName in new[] { "en-US", "da-DK", "de-DE", "fr-FR" })
            {
                var culture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                Assert.Equal(expected, AnalysisResultTableExporter.Build(new[] { single, independent }, options));
                Assert.Same(culture, CultureInfo.CurrentCulture);
                Assert.Same(originalUiCulture, CultureInfo.CurrentUICulture);
                Assert.Same(originalDefaultCulture, CultureInfo.DefaultThreadCurrentCulture);
                Assert.Same(originalDefaultUiCulture, CultureInfo.DefaultThreadCurrentUICulture);

                // TSV makes individual numeric cells easy to inspect independently of CSV quoting.
                options.FileFormat = AnalysisResultExportFileFormat.TSV;
                var table = Lines(AnalysisResultTableExporter.Build(new[] { single, independent }, options));
                options.FileFormat = format;
                Assert.All(table.Skip(1), item => Assert.Equal(table[0].Length, item.Length));
                var header = table[0];
                var row = table[1];
                Assert.Equal("123.25", row[Array.IndexOf(header, "Binding AICc")]);
                Assert.Equal("135.75", row[Array.IndexOf(header, "Null AICc")]);
                Assert.Equal("+12.5", row[Array.IndexOf(header, "ΔAICc")]);
                Assert.Equal("4.184", row[Array.IndexOf(header, "Null RMSD (µJ)")]);
                Assert.Equal("25.4", row[Array.IndexOf(header,
                    rows == AnalysisResultExportRowMode.Summary ? "Evaluation temperature (°C)" : "Temperature (°C)")]);
                if (rows == AnalysisResultExportRowMode.AllRows)
                {
                    if (errors == AnalysisResultExportErrorStyle.SeparateColumns)
                    {
                        Assert.Equal("1.25", row[Array.IndexOf(header, "N_value")]);
                        Assert.Equal("0.125", row[Array.IndexOf(header, "N_sd")]);
                        Assert.Equal("1.05", row[Array.IndexOf(header, "N_ci_lower")]);
                        Assert.Equal("1.45", row[Array.IndexOf(header, "N_ci_upper")]);
                        Assert.Equal("-25.25", row[Array.IndexOf(header, "∆H (kJ/mol)_value")]);
                    }
                    else
                        Assert.Equal("1.25 ± 0.125 [1.05, 1.45]", row[Array.IndexOf(header, "N")]);
                }
                if (rows == AnalysisResultExportRowMode.Summary && purpose == ResultOutputPurpose.Diagnostic)
                {
                    var pooled = table[2];
                    Assert.Equal("123.25", pooled[Array.IndexOf(header, "Pooled diagnostic binding AICc")]);
                    Assert.Equal("135.75", pooled[Array.IndexOf(header, "Pooled diagnostic null AICc")]);
                    Assert.Equal("+12.5", pooled[Array.IndexOf(header, "Pooled diagnostic ΔAICc")]);
                    Assert.Equal("4.184", pooled[Array.IndexOf(header, "Pooled diagnostic null RMSD (µJ)")]);
                }
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Fact]
    public void EmptyAndThrowingInputsRestoreCallingCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        var defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var culture = CultureInfo.GetCultureInfo("da-DK");
        try
        {
            CultureInfo.CurrentCulture = culture;
            Assert.Equal("", AnalysisResultTableExporter.Build(Array.Empty<AnalysisResult>(), null));
            Assert.Same(culture, CultureInfo.CurrentCulture);
            var exception = new InvalidOperationException("Input enumeration failed");
            IEnumerable<AnalysisResult> ThrowingResults()
            {
                yield return null;
                throw exception;
            }
            Assert.Same(exception, Assert.Throws<InvalidOperationException>(() =>
                AnalysisResultTableExporter.Build(ThrowingResults(), null)));
            Assert.Same(culture, CultureInfo.CurrentCulture);
            Assert.Same(uiCulture, CultureInfo.CurrentUICulture);
            Assert.Same(defaultCulture, CultureInfo.DefaultThreadCurrentCulture);
            Assert.Same(defaultUiCulture, CultureInfo.DefaultThreadCurrentUICulture);
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Fact]
    public void ResultAnalysisUsesSelectedIndependentEvidenceAndEffectiveAssessment()
    {
        var result = IndependentResult(out var members, includePooledComparison: false);
        members[0].Data.Name = "First";
        members[1].Data.Name = "Second";
        result.RestoreMemberComparison(members[0].Guid, Comparison(members[0].Data.UniqueID, 12, 100, 4.184));
        result.RestoreMemberComparison(members[1].Guid, Comparison(members[1].Data.UniqueID, 20, 200, 8.368));
        result.SetMemberBindingAssessmentOverride(members[0].Guid, BindingAssessmentOutcome.NoBindingDetected);
        Assert.True(NullModelComparisonPresentation.ResultAnalysis(result, null, EnergyUnitFamily.Joules).SelectionRequired);
        Assert.True(NullModelComparisonPresentation.ResultAnalysis(result, "unrelated", EnergyUnitFamily.Joules).SelectionRequired);
        var first = NullModelComparisonPresentation.ResultAnalysis(result, members[0].Data.UniqueID, EnergyUnitFamily.Calories);
        var second = NullModelComparisonPresentation.ResultAnalysis(result, members[1].Data.UniqueID, EnergyUnitFamily.Calories);
        Assert.Equal("First", first.ExperimentName);
        Assert.Equal("Second", second.ExperimentName);
        Assert.Equal(new[] { "RMSD", "AICc" }, first.BindingRows.Select(row => row.Label));
        Assert.Equal(new[] { "RMSD", "AICc" }, first.NullRows.Select(row => row.Label));
        Assert.Equal("1 µcal", first.NullRows[0].Value);
        Assert.Equal("2 µcal", second.NullRows[0].Value);
        Assert.Equal("100", first.BindingRows[1].Value);
        Assert.Equal("200", second.BindingRows[1].Value);
        Assert.Equal("+12", first.DeltaRow.Value);
        Assert.Contains("Null AICc − binding AICc", first.DeltaRow.Tooltip);
        Assert.Equal("No binding detected (Manual)", first.ConclusionRow.Value);
    }

    [Fact]
    public void ResultAnalysisRetainsNullRmsdWhenAiccIsUnavailableAndHandlesMissingComparison()
    {
        var result = FractionalResult();
        var comparison = result.NullComparison;
        comparison.DeltaAicc = null;
        comparison.NullInformationCriteria = FitInformationCriteria.Restore(20, 1, 2,
            GaussianLikelihoodMode.EstimatedCommonVariance, 0, null, null, false, false,
            "AIC unavailable", "Too few observations", 0, 4.184, 0, 0);
        var view = NullModelComparisonPresentation.ResultAnalysis(result, "ignored", EnergyUnitFamily.Joules);
        Assert.Equal(4.184.ToString("G4", CultureInfo.CurrentCulture) + " µJ", view.NullRows[0].Value);
        Assert.Equal("Unavailable", view.NullRows[1].Value);
        Assert.Equal("Too few observations", view.NullRows[1].Tooltip);
        Assert.Equal("Unavailable", view.DeltaRow.Value);
        result.RestoreNullComparison(null);
        view = NullModelComparisonPresentation.ResultAnalysis(result, null, EnergyUnitFamily.Joules);
        Assert.Equal("Unavailable", view.NullRows[0].Value);
        Assert.Equal("Unavailable", view.NullRows[1].Value);
        Assert.Equal("Unavailable", view.DeltaRow.Value);
    }

    static AnalysisResult FractionalResult()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        model.Data.Name = "Run, \"A\"";
        model.Data.TargetTemperature = model.Data.MeasuredTemperature = 25.4;
        model.Solution.Parameters[ParameterType.Nvalue1] = new FloatWithError(1.25, 0.125, 1.05, 1.45);
        model.Solution.Parameters[ParameterType.Enthalpy1] = new FloatWithError(-25250, 125, -25500, -25000);
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
        result.Name = model.Data.Name;
        result.RestoreNullComparison(Comparison(model.Data.UniqueID, 12.5, 123.25, 4.184));
        return result;
    }

    static AnalysisResult IndependentResult(out List<SolutionInterface> members, bool includePooledComparison,
        bool includeReducedChiSquare = false)
    {
        var first = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var second = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        first.Data.Name = "Duplicate";
        second.Data.Name = "Duplicate";
        if (includeReducedChiSquare)
        {
            first.Solution = SolutionInterface.FromModel(first,
                ConvergenceWithReducedChiSquare(first.Solution.Convergence, 10, 12, 2));
            second.Solution = SolutionInterface.FromModel(second,
                ConvergenceWithReducedChiSquare(second.Solution.Convergence, 18, 8, 2));
        }
        members = new List<SolutionInterface> { first.Solution, second.Solution };
        var model = new GlobalModel(new List<Model> { first, second })
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        model.Parameters.AddIndivdualParameter(first.Parameters);
        model.Parameters.AddIndivdualParameter(second.Parameters);
        var solution = new GlobalSolution(new GlobalSolver { Model = model }, members,
            first.Solution.Convergence, reconstructBootstrap: false);
        model.Solution = solution;
        if (includePooledComparison)
            solution.NullComparison = Comparison(first.Data.UniqueID, 15);
        return new AnalysisResult(solution);
    }

    static SolverConvergence ConvergenceWithReducedChiSquare(SolverConvergence convergence, double q, int n, int p)
    {
        var snapshot = convergence.ToSnapshot();
        snapshot.ReducedChiSquareQ = q;
        snapshot.ReducedChiSquareObservationCount = n;
        snapshot.ReducedChiSquareParameterCount = p;
        return SolverConvergence.FromSnapshot(snapshot);
    }

    static NullModelComparison Comparison(string id, double delta, double bindingAicc = 100, double rmsd = 2)
    {
        FitInformationCriteria Criteria(double aicc) => FitInformationCriteria.Restore(
            observationCount: 20, fittedParameterCount: 2, likelihoodParameterCount: 3,
            likelihoodMode: GaussianLikelihoodMode.EstimatedCommonVariance,
            minusTwoLogLikelihood: aicc - 10, aic: aicc - 4, aicc: aicc,
            isAicAvailable: true, isAiccAvailable: true, aicUnavailableReason: "",
            aiccUnavailableReason: "", rawResidualSumOfSquares: 1,
            residualRmsdMicrojoules: rmsd, standardizedResidualSumOfSquares: 1, logSigmaSquaredSum: 1);
        return new NullModelComparison
        {
            NullModelId = "offset",
            BindingFitSucceeded = true,
            NullFitSucceeded = true,
            BindingInformationCriteria = Criteria(bindingAicc),
            NullInformationCriteria = Criteria(bindingAicc + delta),
            DeltaAicc = delta,
            Members = new List<NullModelComparisonMember>
            {
                new() { ExperimentId = id, Offset = 1234, Scope = "local" },
            },
        };
    }

    static List<string[]> Lines(string table)
        => table.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t')).ToList();
}
