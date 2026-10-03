using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("AutoSaveManager")]
public sealed class ClassifiedOutputPolicyTests
{
    [Theory]
    [InlineData(BindingAssessmentOutcome.NotAssessed, false)]
    [InlineData(BindingAssessmentOutcome.Inconclusive, true)]
    [InlineData(BindingAssessmentOutcome.BindingDetected, false)]
    [InlineData(BindingAssessmentOutcome.NoBindingDetected, true)]
    public void OnlyEffectiveNegativeStandardPurposeSuppressesBindingOutputs(
        BindingAssessmentOutcome outcome, bool expected)
    {
        var result = NegativeResult();
        if (outcome == BindingAssessmentOutcome.Inconclusive)
        {
            var comparison = new NullModelComparison
            {
                BindingFitSucceeded = true, NullFitSucceeded = true, DeltaAicc = 8,
                BindingInformationCriteria = Criteria(100), NullInformationCriteria = Criteria(108),
            };
            SetNullComparison(result, comparison);
            typeof(AnalysisResult).GetProperty(nameof(AnalysisResult.BindingAssessment), BindingFlags.Instance | BindingFlags.Public)
                .GetSetMethod(true).Invoke(result, new object[] { BindingAssessmentState.FromComparison(comparison) });
        }
        else if (outcome == BindingAssessmentOutcome.NotAssessed) result.UseAutomaticBindingAssessment();
        else result.SetBindingAssessmentOverride(outcome);

        Assert.Equal(expected,
            ResultOutputPolicy.SuppressBindingOutputs(result));
        Assert.False(ResultOutputPolicy.SuppressBindingOutputs(result, ResultOutputPurpose.Diagnostic));
    }

    [Fact]
    public void StandardResultTableHidesNegativeBindingEstimatesWhileDiagnosticRetainsThem()
    {
        var result = NegativeResult();
        const double fittedSentinel = 123.456;
        result.Solution.Solutions[0].Parameters[ParameterType.Nvalue1] = new FloatWithError(fittedSentinel, 0);
        var marker = fittedSentinel.ToString("G5", System.Globalization.CultureInfo.CurrentCulture);

        var standard = AnalysisResultTableExporter.Build(new[] { result }, new AnalysisResultExportOptions
        {
            RowMode = AnalysisResultExportRowMode.AllRows,
            ErrorStyle = AnalysisResultExportErrorStyle.SeparateColumns,
        });
        var diagnostic = AnalysisResultTableExporter.Build(new[] { result }, new AnalysisResultExportOptions
        {
            OutputPurpose = ResultOutputPurpose.Diagnostic,
            RowMode = AnalysisResultExportRowMode.AllRows,
            ErrorStyle = AnalysisResultExportErrorStyle.SeparateColumns,
        });

        Assert.Contains("Output purpose", standard);
        Assert.Contains("No binding detected", standard);
        Assert.DoesNotContain("N_value", ParseCsvLine(standard.Split(new[] { "\r\n", "\n" },
            System.StringSplitOptions.RemoveEmptyEntries)[0]));
        Assert.Contains("Binding-fit diagnostics", diagnostic);
        Assert.Equal(marker, Cell(diagnostic, "N_value"));
    }

    [Fact]
    public void MixedResultTableSuppressesOnlyTheNegativeResultEvenWhenExperimentIsShared()
    {
        var negative = NegativeResult();
        const double positiveSentinel = 321.654;
        negative.Solution.Solutions[0].Parameters[ParameterType.Nvalue1] = new FloatWithError(positiveSentinel, 0);
        var positive = new AnalysisResult(negative.Solution);
        positive.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);

        var table = AnalysisResultTableExporter.Build(new[] { negative, positive }, new AnalysisResultExportOptions
        {
            RowMode = AnalysisResultExportRowMode.AllRows,
            ErrorStyle = AnalysisResultExportErrorStyle.SeparateColumns,
        });

        var lines = table.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);
        var headers = ParseCsvLine(lines[0]);
        var rows = lines.Skip(1).Select(ParseCsvLine).ToList();
        var nIndex = headers.IndexOf("N_value");
        var modelIndex = headers.IndexOf("Model");
        var outcomeIndex = headers.IndexOf("Binding assessment");
        var negativeRow = Assert.Single(rows, row => row[outcomeIndex] == "No binding detected");
        var positiveRow = Assert.Single(rows, row => row[outcomeIndex] == "Binding detected");
        Assert.Equal("", negativeRow[nIndex]);
        Assert.NotEqual("", positiveRow[nIndex]);
        Assert.StartsWith("Attempted: ", negativeRow[modelIndex]);
        Assert.DoesNotContain("Attempted: ", positiveRow[modelIndex]);

        var report = AnalysisReportBuilder.Build(new[] { negative, positive }, new AnalysisReportOptions());
        var included = Assert.Single(report.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportTableBlock>(), block => block.Title == "Included results");
        var reportModelIndex = included.Columns.ToList().FindIndex(column => column.Id == "model");
        var reportAssessmentIndex = included.Columns.ToList().FindIndex(column => column.Id == "assessment");
        Assert.DoesNotContain(included.Columns, column => column.Id == "assessment-mode");
        var negativeReportRow = Assert.Single(included.Rows,
            row => row.Cells[reportAssessmentIndex] == "No binding detected (manual)");
        var positiveReportRow = Assert.Single(included.Rows,
            row => row.Cells[reportAssessmentIndex] == "Binding detected (manual)");
        Assert.StartsWith("Attempted: ", negativeReportRow.Cells[reportModelIndex]);
        Assert.DoesNotContain("Attempted: ", positiveReportRow.Cells[reportModelIndex]);
    }

    [Fact]
    public void NegativeClipboardUsesSharedMemberRowsAndExplicitEnergyOverride()
    {
        var result = NegativeResult();
        var data = result.Solution.Solutions[0].Data;
        SetNullComparison(result, new NullModelComparison
        {
            NullFitSucceeded = true,
            Members = new List<NullModelComparisonMember>
            {
                new NullModelComparisonMember { ExperimentId = data.UniqueID, Offset = 1234, Scope = "local" },
            },
        });
        var clipboard = new RecordingClipboardService();
        PlatformServices.RegisterClipboardService(clipboard);
        try
        {
            Exporter.CopyToClipboard(result, EnergyUnitFamily.Calories, EnergyUnit.Cal, usekelvin: false);
        }
        finally
        {
            PlatformServices.RegisterClipboardService(null);
        }

        Assert.Contains("Binding assessment", clipboard.Value);
        Assert.Contains(data.Name, clipboard.Value);
        Assert.Contains("cal/mol", clipboard.Value);
        Assert.DoesNotContain("N_value", ParseCsvLine(clipboard.Value.Split(new[] { "\r\n", "\n" },
            System.StringSplitOptions.RemoveEmptyEntries)[0]));
    }

    [Fact]
    public void NegativeStandardReportUsesAssessmentPathWithoutBindingParameterSection()
    {
        var result = NegativeResult();
        var report = AnalysisReportBuilder.Build(result, new AnalysisReportOptions());
        var sectionNames = report.Sections.Select(section => section.Title).ToList();

        Assert.Contains("Analysis summary", sectionNames);
        Assert.Contains("Appendix", sectionNames);
        Assert.Contains(report.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportKeyValueBlock>().SelectMany(block => block.Items),
            item => item.Label == "Binding assessment" && item.Value == "No binding detected");
        Assert.DoesNotContain("Binding parameters omitted", report.Sections
            .SelectMany(section => section.Blocks)
            .OfType<AnalysisReportNoticeBlock>().Select(block => block.Title));
        Assert.DoesNotContain("Fitted and derived parameters", report.Sections
            .SelectMany(section => section.Blocks).Select(block => block.Title));
    }

    [Fact]
    public void CompactInterpretationInputPreservesAssessmentAndUnroundedDeltaEvidence()
    {
        var package = new AnalysisInterpretationPackage
        {
            Result = new InterpretationResultEvidence
            {
                EvidenceId = "result-evidence", ResultId = "result", Name = "result",
                BindingAssessment = new InterpretationBindingAssessmentEvidence
                {
                    EffectiveOutcome = "NoBindingDetected", Mode = "Automatic",
                    DeltaAicc = 6.0000004,
                    NullFitStatus = "Converged",
                    NullModel = "Offset",
                },
            },
        };

        var prompt = AnalysisInterpretationPromptBuilder.Build(package);

        Assert.Contains("NoBindingDetected", prompt.CanonicalPackageJson);
        Assert.Contains("NoBindingDetected", prompt.ModelPackageJson);
        Assert.Contains("6.0000004", prompt.ModelPackageJson);
        Assert.Contains("saved null comparison", prompt.ResponseFormatInstructions);
    }

    [Fact]
    public void ManualInterpretationCreateAndEditRecordCurrentAssessmentContext()
    {
        var result = NegativeResult();
        var report = new AnalysisReport { Name = "Report" };
        report.SetResultIds(new[] { result.UniqueID });
        var before = AnalysisInterpretationPackageBuilder.AssessmentContextFingerprintFromResults(new[] { result });
        report.SetManualInterpretation("A manually written conclusion.", before);
        Assert.Equal(before, report.ApprovedInterpretation.AssessmentContextFingerprint);

        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
        var after = AnalysisInterpretationPackageBuilder.AssessmentContextFingerprintFromResults(new[] { result });
        Assert.NotEqual(before, after);
        report.UpdateApprovedInterpretationText("An edited manual conclusion.", after);
        Assert.Equal(after, report.ApprovedInterpretation.AssessmentContextFingerprint);
    }

    [Fact]
    public void NullFigureDrawsFittedOffsetOnCurrentObservationsWithoutChangingThem()
    {
        var result = NegativeResult();
        var data = result.Solution.Solutions[0].Data;
        SetNullComparison(result, new NullModelComparison
        {
            NullFitSucceeded = true,
            Members = new List<NullModelComparisonMember>
            {
                new NullModelComparisonMember
                {
                    ExperimentId = data.UniqueID,
                    Offset = 1234,
                    Points = new List<NullModelComparisonPoint>
                    {
                        new NullModelComparisonPoint
                        {
                            InjectionId = 0, Ratio = 7, InjectionMass = 2e-9,
                            ObservedHeatJoules = 2.468e-6, PredictedHeatJoules = 2.468e-6,
                            Included = true,
                        },
                    },
                },
            },
        });
        var peakAreasBefore = data.Injections.Select(injection => injection.PeakArea.Value).ToArray();
        var rawPeakAreasBefore = data.Injections.Select(injection => injection.RawPeakArea.Value).ToArray();

        var figure = PublicationFigureBuilder.Build(
            new PublicationFigureSource(data, result.Solution.Solutions[0], result),
            new PublicationFigureOptions
            {
                EnergyUnitFamily = EnergyUnitFamily.Joules, EnergyUnitOverride = EnergyUnit.Joule,
                DrawFitOffsetCorrected = false,
            });

        var fit = Assert.IsType<PublicationFigurePanel>(figure.FitPanel);
        Assert.Equal(data.Injections.Select(injection => injection.Ratio), fit.Points.Select(point => point.X));
        Assert.Equal(data.Injections.Select(injection => injection.PeakArea.Value / injection.InjectionMass),
            fit.Points.Select(point => point.Y), new ToleranceComparer(1e-6));
        var offset = Assert.Single(fit.Series);
        Assert.All(offset.Points, point => Assert.Equal(1234, point.Y, 6));
        Assert.DoesNotContain(7d, fit.Points.Select(point => point.X));
        Assert.DoesNotContain("No binding detected", fit.AnnotationBoxes.SelectMany(box => box.Lines));
        Assert.Equal(peakAreasBefore, data.Injections.Select(injection => injection.PeakArea.Value).ToArray());
        Assert.Equal(rawPeakAreasBefore, data.Injections.Select(injection => injection.RawPeakArea.Value).ToArray());
    }

    [Fact]
    public void FailedNullFitDrawsCurrentObservationsWithoutPrediction()
    {
        var result = NegativeResult();
        var data = result.Solution.Solutions[0].Data;
        SetNullComparison(result, new NullModelComparison
        {
            NullFitSucceeded = false,
            NullFitReason = "Offset solver did not converge.",
            Members = new List<NullModelComparisonMember>
            {
                new NullModelComparisonMember
                {
                    ExperimentId = data.UniqueID,
                    Points = new List<NullModelComparisonPoint>
                    {
                        new NullModelComparisonPoint
                        {
                            InjectionId = 0, Ratio = 2, InjectionMass = 1e-9,
                            ObservedHeatJoules = 3e-6, PredictedHeatJoules = double.NaN,
                            Included = true,
                        },
                    },
                },
            },
        });

        var figure = PublicationFigureBuilder.Build(
            new PublicationFigureSource(data, result.Solution.Solutions[0], result), new PublicationFigureOptions());

        var fit = Assert.IsType<PublicationFigurePanel>(figure.FitPanel);
        Assert.Equal(data.Injections.Count, fit.Points.Count);
        Assert.Empty(fit.Series);
        Assert.Null(figure.ResidualPanel);
        Assert.Equal(new[] { "Offset fit unavailable" }, fit.AnnotationBoxes.SelectMany(box => box.Lines));
    }

    [Fact]
    public void SuccessfulComparisonWithoutMatchingMemberDrawsCurrentObservationsOnly()
    {
        var result = NegativeResult();
        var data = result.Solution.Solutions[0].Data;
        SetNullComparison(result, new NullModelComparison
        {
            NullFitSucceeded = true,
            Members = new List<NullModelComparisonMember>
            {
                new NullModelComparisonMember
                {
                    ExperimentId = "different-experiment",
                    Offset = 9999,
                    Points = new List<NullModelComparisonPoint>
                    {
                        new NullModelComparisonPoint { InjectionMass = 1e-9, Ratio = 4,
                            ObservedHeatJoules = 5e-6, PredictedHeatJoules = 6e-6, Included = true },
                    },
                },
            },
        });

        var figure = PublicationFigureBuilder.Build(
            new PublicationFigureSource(data, result.Solution.Solutions[0], result), new PublicationFigureOptions());

        var fit = Assert.IsType<PublicationFigurePanel>(figure.FitPanel);
        Assert.Empty(fit.Series);
        Assert.Contains("Offset fit unavailable", fit.AnnotationBoxes.SelectMany(box => box.Lines));
        Assert.DoesNotContain(9999, fit.Points.Select(point => point.Y));
    }

    sealed class ToleranceComparer : IEqualityComparer<double>
    {
        readonly double tolerance;
        public ToleranceComparer(double tolerance) => this.tolerance = tolerance;
        public bool Equals(double x, double y) => System.Math.Abs(x - y) <= tolerance * System.Math.Max(1, System.Math.Abs(x));
        public int GetHashCode(double obj) => 0;
    }

    [Fact]
    public void OwnedBindingFitFiguresKeepOriginalAnnotationsAndLabelOnlyDiagnosticPurpose()
    {
        var result = NegativeResult();
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.BindingDetected);
        var data = result.Solution.Solutions[0].Data;
        var standard = PublicationFigureBuilder.Build(
            new PublicationFigureSource(data, result.Solution.Solutions[0], result), new PublicationFigureOptions());
        var standardLabels = standard.FitPanel.AnnotationBoxes.SelectMany(box => box.Lines).ToList();
        var original = PublicationFigureBuilder.Build(
            new PublicationFigureSource(data, result.Solution.Solutions[0]), new PublicationFigureOptions());
        Assert.Equal(original.FitPanel.AnnotationBoxes.SelectMany(box => box.Lines), standardLabels);

        var diagnostic = PublicationFigureBuilder.Build(
            new PublicationFigureSource(data, result.Solution.Solutions[0], result, ResultOutputPurpose.Diagnostic),
            new PublicationFigureOptions());
        var diagnosticLabels = diagnostic.FitPanel.AnnotationBoxes.SelectMany(box => box.Lines).ToList();
        Assert.Contains("Binding-fit diagnostics", diagnosticLabels);
        Assert.Equal(standardLabels, diagnosticLabels.Where(line => line != "Binding-fit diagnostics"));
    }

    [Fact]
    public void NegativeInterpretationPackageKeepsAssessmentButOmitsBindingParameterEvidence()
    {
        var result = NegativeResult();
        var report = new AnalysisReport { Name = "Report" };
        report.SetResultIds(new[] { result.UniqueID });
        const double fittedSentinel = 765.4321;
        result.Solution.Solutions[0].Parameters[ParameterType.Nvalue1] = new FloatWithError(fittedSentinel, 0);
        var package = AnalysisInterpretationPackageBuilder.Build(report, result);
        var json = JsonSerializer.Serialize(package);
        var model = AnalysisInterpretationPromptBuilder.Build(package).ModelPackageJson;

        Assert.Contains("NoBindingDetected", json);
        Assert.Contains("NoBindingDetected", model);
        Assert.All(package.Result.Experiments, experiment =>
        {
            Assert.Empty(experiment.Parameters);
            Assert.Empty(experiment.ModelOptions);
        });
        Assert.Empty(package.Result.Model.Options);
        Assert.Empty(package.Result.Model.Constraints);
        Assert.Null(package.Result.InformationCriteria);
        Assert.Contains("Offset", model);
    }

    [Fact]
    public void NegativeNullEvidenceRespectsInterpretationInjectionRowSelection()
    {
        var result = NegativeResult();
        var data = result.Solution.Solutions[0].Data;
        SetNullComparison(result, new NullModelComparison
        {
            NullFitSucceeded = true,
            Members = new List<NullModelComparisonMember>
            {
                new NullModelComparisonMember
                {
                    ExperimentId = data.UniqueID,
                    Points = new List<NullModelComparisonPoint>
                    {
                        new NullModelComparisonPoint { InjectionId = 0, InjectionMass = 1e-9,
                            Ratio = 1, ObservedHeatJoules = 2e-6, PredictedHeatJoules = 1e-6, Included = true },
                        new NullModelComparisonPoint { InjectionId = 1, InjectionMass = 1e-9,
                            Ratio = 2, ObservedHeatJoules = 3e-6, PredictedHeatJoules = 1e-6, Included = false },
                    },
                },
            },
        });
        var report = new AnalysisReport { Name = "Report" };
        report.SetResultIds(new[] { result.UniqueID });

        var all = AnalysisInterpretationPackageBuilder.Build(report, result);
        Assert.Equal(2, Assert.Single(all.Result.BindingAssessment.NullMembers).Points.Count);
        var settings = report.InterpretationSettings.Copy();
        settings.InjectionRows = AnalysisInterpretationInjectionRows.IncludedOnly;
        report.UpdateInterpretationSettings(settings);
        var included = AnalysisInterpretationPackageBuilder.Build(report, result);
        Assert.Equal(1, Assert.Single(included.Result.BindingAssessment.NullMembers).Points.Single().InjectionNumber);
        settings.InjectionRows = AnalysisInterpretationInjectionRows.None;
        report.UpdateInterpretationSettings(settings);
        var none = AnalysisInterpretationPackageBuilder.Build(report, result);
        Assert.Empty(Assert.Single(none.Result.BindingAssessment.NullMembers).Points);
    }

    static AnalysisResult NegativeResult()
    {
        var model = InjectionProcessingMethodTests.FittedModel(bootstrap: false);
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
        result.SetBindingAssessmentOverride(BindingAssessmentOutcome.NoBindingDetected);
        return result;
    }

    sealed class RecordingClipboardService : IClipboardService
    {
        public string Value { get; private set; } = string.Empty;
        public void SetString(string value) => Value = value ?? string.Empty;
    }

    static void SetNullComparison(AnalysisResult result, NullModelComparison comparison)
        => typeof(AnalysisResult).GetProperty(nameof(AnalysisResult.NullComparison), BindingFlags.Instance | BindingFlags.Public)
            .GetSetMethod(true).Invoke(result, new object[] { comparison });

    static FitInformationCriteria Criteria(double aicc)
        => FitInformationCriteria.Restore(observationCount: 20, fittedParameterCount: 2,
            likelihoodParameterCount: 3, likelihoodMode: GaussianLikelihoodMode.EstimatedCommonVariance,
            minusTwoLogLikelihood: 90, aic: aicc - 4, aicc: aicc, isAicAvailable: true,
            isAiccAvailable: true, aicUnavailableReason: "", aiccUnavailableReason: "",
            rawResidualSumOfSquares: 1, residualRmsdMicrojoules: 1,
            standardizedResidualSumOfSquares: 1, logSigmaSquaredSum: 1);

    static string Cell(string csv, string header)
    {
        var lines = csv.Split(new[] { "\r\n", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);
        var headers = ParseCsvLine(lines[0]);
        var values = ParseCsvLine(lines[1]);
        return values[headers.IndexOf(header)];
    }

    static System.Collections.Generic.List<string> ParseCsvLine(string line)
    {
        var values = new System.Collections.Generic.List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else current.Append(character);
        }
        values.Add(current.ToString());
        return values;
    }
}
