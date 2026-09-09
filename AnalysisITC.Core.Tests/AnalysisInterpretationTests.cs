using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;

using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisInterpretationTests
{
    // These expectations are calculated independently from the package builder.
    [Theory]
    [InlineData("one-set-of-sites", "equivalent independent sites")]
    [InlineData("two-sets-of-sites", "two site classes")]
    [InlineData("sequential-binding-sites", "ordered macroscopic steps")]
    [InlineData("competitive-binding", "competitor concentration")]
    [InlineData("dissociation", "injected preformed complex")]
    public void PromptIncludesOnlyRelevantModelGuidance(string model, string expected)
    {
        var package = new AnalysisInterpretationPackage
        {
            Report = new InterpretationReportEvidence { EvidenceId = "report-1", ReportId = "r", Name = "R" },
            Result = new InterpretationResultEvidence
            {
                EvidenceId = "result-1", ResultId = "x", Name = "X",
                Model = new InterpretationModelEvidence { Type = model },
            },
            RequestedInterpretation = AnalysisInterpretationOptions.Default(),
        };
        var prompt = AnalysisInterpretationPromptBuilder.Build(package);
        Assert.Contains(expected, prompt.SystemInstructions, StringComparison.Ordinal);
        Assert.Contains("## Overall interpretation", prompt.ResponseFormatInstructions, StringComparison.Ordinal);
        Assert.Equal(AnalysisInterpretationPromptBuilder.OutputFormatVersion, prompt.OutputFormatVersion);
    }

    [Fact]
    public async Task DebugArchivePreservesCompleteLocalPackageAndPromptWithoutApprovingReport()
    {
        var result = await LoadResult();
        var report = ReportFor(result);
        report.UpdateStudyContext(new AnalysisStudyContext { ScientificQuestion = "Compare affinity", AdditionalNotes = "Private local context" });
        var package = AnalysisInterpretationPackageBuilder.Build(report, result);
        var expected = AnalysisInterpretationPromptBuilder.Build(package);
        var bytes = AnalysisInterpretationDebugExport.CreateArchive(package);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        string Read(string name)
        {
            using var reader = new StreamReader(archive.GetEntry(name)!.Open());
            return reader.ReadToEnd();
        }
        Assert.Equal(7, archive.Entries.Count);
        Assert.Equal(expected.CanonicalPackageJson, Read("canonical-package.json"));
        Assert.Equal(expected.SystemInstructions, Read("system-instructions.txt"));
        Assert.Equal(expected.UserMessage, Read("user-message.txt"));
        Assert.Equal(expected.ResponseFormatInstructions, Read("output-format.txt"));
        using var pretty = JsonDocument.Parse(Read("package.json"));
        Assert.Equal("Compare affinity", pretty.RootElement.GetProperty("studyContext").GetProperty("scientificQuestion").GetString());
        Assert.Contains("Private local context", Read("package.json"));
        using var manifest = JsonDocument.Parse(Read("manifest.json"));
        Assert.False(manifest.RootElement.GetProperty("sentToServer").GetBoolean());
        Assert.Equal(expected.InputFingerprint, manifest.RootElement.GetProperty("inputFingerprint").GetString());
        Assert.Null(report.ApprovedInterpretation);
    }

    [Fact]
    public void PromptDiagnosticsRecordIdentityAndSizeWithoutScientificText()
    {
        var secret = "private-context-" + Guid.NewGuid().ToString("N");
        var id = Guid.NewGuid().ToString("N");
        var package = new AnalysisInterpretationPackage();
        package.Report = new InterpretationReportEvidence { AuthorComments = secret };
        var prompt = AnalysisInterpretationPromptBuilder.Build(package, id);
        var log = AnalysisITC.Core.Application.AppEventHandler.GetLogReport();
        Assert.Contains("stage=prompt-ready request=" + id, log);
        Assert.Contains("fingerprint=" + prompt.InputFingerprint, log);
        Assert.Contains("packageBytes=", log);
        Assert.DoesNotContain(secret, log);
        Assert.Throws<ArgumentNullException>(() => AnalysisInterpretationPromptBuilder.Build(null, id));
        Assert.Contains("stage=prompt-failed request=" + id, AnalysisITC.Core.Application.AppEventHandler.GetLogReport());
    }

    [Fact]
    public async Task PackageAndPromptAreDeterministicAndSeparateKdFromFittedLogAffinity()
    {
        var result = await LoadResult();
        result.Solution.Solutions[0].Data.SetFileName("/private/studies/secret/source.itc");
        result.SetValiditySnapshot(AnalysisResultValiditySnapshot.Capture(result.Solution));
        var report = ReportFor(result);
        var supporting = new ExperimentData("supporting.itc");
        supporting.SetID("supporting-experiment-id");
        report.SetSupportingExperimentIds(new[] { result.Solution.Solutions[0].Data.UniqueID, supporting.UniqueID });
        ExperimentData ResolveExperiment(string id) => id == supporting.UniqueID ? supporting
            : result.Solution.Solutions.Select(item => item.Data).FirstOrDefault(item => item.UniqueID == id);

        var firstPackage = AnalysisInterpretationPackageBuilder.Build(report, _ => result, ResolveExperiment);
        var secondPackage = AnalysisInterpretationPackageBuilder.Build(report, _ => result, ResolveExperiment);
        var first = AnalysisInterpretationPromptBuilder.Build(firstPackage);
        var second = AnalysisInterpretationPromptBuilder.Build(secondPackage);

        Assert.Equal(first.CanonicalPackageJson, second.CanonicalPackageJson);
        Assert.Equal(first.SystemInstructions, second.SystemInstructions);
        Assert.Equal(first.InputFingerprint, second.InputFingerprint);
        Assert.DoesNotContain("/private/studies", first.CanonicalPackageJson, StringComparison.Ordinal);
        Assert.DoesNotContain("dataPoints", first.CanonicalPackageJson, StringComparison.OrdinalIgnoreCase);
        Assert.True(firstPackage.DataBoundary.ContainsRawThermogramSamples);
        Assert.True(firstPackage.DataBoundary.ContainsBaselineSummary);
        Assert.Equal("source.itc", firstPackage.Result.Experiments[0].SourceFileBasename);
        Assert.Equal("1", firstPackage.Result.ReportReference);
        Assert.Equal("1A", firstPackage.Result.Experiments[0].ReportReference);
        Assert.Contains(firstPackage.Report.References, reference =>
            reference.ReportReference == "1" && reference.Id == result.UniqueID);
        Assert.Contains(firstPackage.Report.References, reference =>
            reference.ReportReference == "1A"
            && reference.Id == result.Solution.Solutions[0].Data.UniqueID
            && reference.ParentReference == "1");
        Assert.Contains(firstPackage.Report.References, reference =>
            reference.ReportReference == "S1"
            && reference.Id == "supporting-experiment-id"
            && reference.Kind == "supporting-experiment");
        Assert.DoesNotContain(firstPackage.Report.References, reference =>
            reference.Kind == "supporting-experiment"
            && reference.Id == result.Solution.Solutions[0].Data.UniqueID);

        var affinity = firstPackage.Result.Experiments[0].Parameters.Single(parameter => parameter.QuantityId == "kd-1");
        Assert.Equal("affinity-log10-1", affinity.FittedCoordinateId);
        Assert.Equal("mol/L", affinity.SiUnit);
        Assert.True(affinity.IsDerived);
        Assert.True(affinity.Confidence95Available);
        Assert.NotNull(affinity.Confidence95Lower);
        Assert.NotNull(affinity.Confidence95Upper);
        Assert.True(affinity.Confidence95Lower <= affinity.BestFitValue);
        Assert.True(affinity.Confidence95Upper >= affinity.BestFitValue);
        Assert.NotEmpty(firstPackage.Result.Experiments[0].Injections);
        Assert.Equal(result.Solution.Solutions[0].Data.Injections.Count,
            firstPackage.Result.Experiments[0].Injections.Count);
        Assert.All(firstPackage.Result.Experiments[0].Injections, injection =>
            Assert.StartsWith("result-1/experiment-1/injection-", injection.EvidenceId, StringComparison.Ordinal));
        var sourceInjection = result.Solution.Solutions[0].Data.Injections[0];
        var packagedInjection = firstPackage.Result.Experiments[0].Injections[0];
        var sourceExperiment = result.Solution.Solutions[0].Data;
        var packagedExperiment = firstPackage.Result.Experiments[0];
        Assert.Equal(Math.Round(sourceExperiment.TargetTemperature, 1), packagedExperiment.TargetTemperatureCelsius);
        Assert.NotNull(packagedExperiment.Baseline);
        Assert.NotNull(packagedExperiment.ResidualDiagnostics);
        Assert.Contains(firstPackage.EvidenceCatalog, item => item.Id == "result-1/experiment-1/baseline");
        Assert.Contains(firstPackage.EvidenceCatalog, item => item.Id == "result-1/experiment-1/residual-diagnostics");
        Assert.Equal(sourceExperiment.CellConcentration.Value, packagedExperiment.CellConcentrationMolar);
        Assert.Equal(sourceExperiment.CellConcentration.SD, packagedExperiment.CellConcentrationSdMolar);
        Assert.Equal(sourceExperiment.SyringeConcentration.Value, packagedExperiment.SyringeConcentrationMolar);
        Assert.Equal(sourceExperiment.SyringeConcentration.SD, packagedExperiment.SyringeConcentrationSdMolar);
        Assert.Equal("microcal-peaq-itc", packagedExperiment.Instrument.ModelId);
        Assert.Equal("MicroCal PEAQ-ITC", packagedExperiment.Instrument.ModelName);
        Assert.Equal(sourceExperiment.CellVolume, packagedExperiment.Instrument.CellVolumeLitres);
        Assert.Equal(sourceExperiment.FeedBackMode.ToString(), packagedExperiment.Instrument.FeedbackMode);
        Assert.Equal(sourceExperiment.StirringSpeed, packagedExperiment.Instrument.StirringSpeedRpm);
        var independentlyCalculatedMass = result.Solution.Solutions[0].Data.SyringeConcentration.Value * sourceInjection.Volume;
        Assert.Equal(sourceInjection.ActualCellConcentration, packagedInjection.ActiveCellConcentrationMolar);
        Assert.Equal(sourceInjection.ActualTitrantConcentration, packagedInjection.ActiveTitrantConcentrationMolar);
        Assert.Equal(sourceInjection.Delay, packagedInjection.InjectionDelaySeconds);
        Assert.Equal(sourceInjection.Filter, packagedInjection.FilterPeriodSeconds);
        Assert.Equal(sourceInjection.IntegrationStartDelay, packagedInjection.IntegrationStartDelaySeconds);
        Assert.Equal(sourceInjection.IntegrationEndOffset, packagedInjection.IntegrationEndOffsetSeconds);
        Assert.Equal(sourceInjection.IntegrationLength, packagedInjection.IntegrationLengthSeconds);
        Assert.Equal(sourceInjection.IntegrationLength / sourceInjection.Delay,
            packagedInjection.IntegrationLengthFractionOfInjectionDelay.Value, 10);
        Assert.Equal(sourceInjection.PeakArea.Value, packagedInjection.IntegratedHeatJoules);
        Assert.Equal(sourceInjection.PeakArea.SD, packagedInjection.IntegratedHeatErrorJoules);
        Assert.Equal(sourceInjection.PeakArea.Value / independentlyCalculatedMass,
            packagedInjection.ObservedHeatJoulesPerMole.Value, 10);
        Assert.Equal(sourceInjection.PeakArea.SD / independentlyCalculatedMass,
            packagedInjection.ObservedHeatErrorJoulesPerMole.Value, 10);
        Assert.Equal(packagedInjection.ObservedHeatJoulesPerMole.Value - packagedInjection.FittedHeatJoulesPerMole.Value,
            packagedInjection.ResidualJoulesPerMole.Value, 8);
        Assert.Equal(sourceInjection.Include, packagedInjection.Included);

        var includedResiduals = packagedExperiment.Injections
            .Where(injection => injection.Included && injection.ResidualJoulesPerMole.HasValue)
            .Select(injection => injection.ResidualJoulesPerMole.Value).ToArray();
        Assert.Equal(includedResiduals.Average(),
            packagedExperiment.ResidualDiagnostics.MeanResidualJoulesPerMole.Value, 10);
        Assert.Equal(Math.Sqrt(includedResiduals.Average(value => value * value)),
            packagedExperiment.ResidualDiagnostics.RmsResidualJoulesPerMole.Value, 10);

        var attributedExperiment = firstPackage.Result.Experiments.Single(experiment => experiment.Attributes.Count > 0);
        Assert.All(attributedExperiment.Attributes, attribute =>
        {
            Assert.False(string.IsNullOrWhiteSpace(attribute.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(attribute.DisplayValue));
        });

        result.Solution.UseWeightedFitting = true;
        var weighted = AnalysisInterpretationPackageBuilder.Build(report, _ => result, ResolveExperiment);
        Assert.True(weighted.Result.Model.UsesWeightedFitting);
        Assert.True(weighted.Result.Solver.UsesWeightedObjective);
        Assert.NotNull(weighted.Result.Solver.UnweightedRmsdMicrojoules);
    }

    [Theory]
    [InlineData(false, GaussianLikelihoodMode.EstimatedCommonVariance, "estimatedCommonVariance")]
    [InlineData(true, GaussianLikelihoodMode.EstimatedWeightedVariance, "estimatedWeightedVariance")]
    public async Task InformationCriteriaEvidenceIncludesExplicitVarianceConvention(
        bool weighted, GaussianLikelihoodMode mode, string serializedMode)
    {
        var result = await LoadResult();
        result.Solution.UseWeightedFitting = weighted;
        foreach (var member in result.Solution.Solutions)
            member.UseWeightedFitting = weighted;
        result.UpdateSolution(result.Solution);
        var package = AnalysisInterpretationPackageBuilder.Build(ReportFor(result), result);
        var criteria = package.Result.InformationCriteria;

        Assert.Equal(mode, criteria.LikelihoodMode);
        Assert.False(criteria.UsesKnownObservationSigmas);
        Assert.Equal(criteria.FittedParameterCount + 1, criteria.LikelihoodParameterCount);
        Assert.All(package.Result.Experiments, experiment => Assert.Equal(mode, experiment.InformationCriteria.LikelihoodMode));
        var prompt = AnalysisInterpretationPromptBuilder.Build(package);
        using var json = JsonDocument.Parse(prompt.CanonicalPackageJson);
        Assert.Equal(serializedMode, json.RootElement.GetProperty("results")[0]
            .GetProperty("informationCriteria").GetProperty("likelihoodMode").GetString());
        Assert.Contains("one common variance multiplier", prompt.SystemInstructions);
        Assert.Contains("Weighted profile intervals still use fixed observation sigmas", prompt.SystemInstructions);
        Assert.Null(JsonSerializer.Deserialize<InterpretationInformationCriteriaEvidence>("{}").LikelihoodMode);
    }

    [Theory]
    [InlineData(0.000040, 0.0, 0.0)]
    [InlineData(0.000040, 0.000000002, 0.0)]
    [InlineData(0.000040, 0.0, 0.00000000001)]
    public void BaselineSummaryUsesStoredTraceWithoutExportingIt(double intercept, double slope, double curvature)
    {
        var experiment = new ExperimentData("synthetic.itc");
        for (var i = 0; i <= 12; i++)
        {
            var time = 10.0 * i;
            var baseline = intercept + slope * time + curvature * time * time;
            experiment.DataPoints.Add(new DataPoint((float)time, (float)(baseline + 0.000001), 25));
        }
        experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
        experiment.Processor.Interpolator.Baseline = experiment.DataPoints
            .Select(point => new Energy(intercept + slope * point.Time + curvature * point.Time * point.Time)).ToList();

        var summary = AnalysisInterpretationDiagnostics.Baseline(experiment, "result-1/experiment-1");

        Assert.True(summary.IsAvailable);
        Assert.Equal(12, summary.Landmarks.Count);
        Assert.Equal(120, summary.TraceDurationSeconds);
        Assert.Equal(intercept * 1e6, summary.StartPowerMicrowatts.Value, 8);
        Assert.Equal((slope * 120 + curvature * 120 * 120) * 1e6, summary.NetDriftMicrowatts.Value, 7);
        Assert.Equal(1.0, summary.OutsideIntegrationRmsRawMinusBaselineMicrowatts.Value, 5);
        Assert.NotNull(summary.Polynomial);
    }

    [Fact]
    public void BaselineSummaryMarksMismatchedTraceUnavailable()
    {
        var experiment = new ExperimentData("mismatch.itc");
        experiment.DataPoints.Add(new DataPoint(0, 1, 25));
        experiment.DataPoints.Add(new DataPoint(1, 1, 25));
        experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Spline);
        experiment.Processor.Interpolator.Baseline.Add(new Energy(1));

        var summary = AnalysisInterpretationDiagnostics.Baseline(experiment, "result-1/experiment-1");

        Assert.False(summary.IsAvailable);
        Assert.Contains("do not match", summary.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void BaselineControlsAndInjectionBoundaryCorrectionUseStoredRepresentations()
    {
        var experiment = new ExperimentData("controls.itc");
        for (var time = 0; time <= 10; time++)
            experiment.DataPoints.Add(new DataPoint(time, (float)((10 + time) * 1e-6), 25));

        experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Spline);
        var spline = Assert.IsType<SplineInterpolator>(experiment.Processor.Interpolator);
        spline.Algorithm = SplineInterpolator.SplineInterpolatorAlgorithm.Rigid;
        spline.SplinePoints.Add(new SplineInterpolator.SplinePoint(0, 10e-6, 0, 1e-6)
        { Locked = true, SlopeLocked = true, UserDefined = true, Linear = true });
        spline.Baseline = experiment.DataPoints.Select(point => new Energy((10 + point.Time) * 1e-6)).ToList();
        var splineSummary = AnalysisInterpretationDiagnostics.Baseline(experiment, "result-1/experiment-1");
        var control = Assert.Single(splineSummary.Spline.ControlPoints);
        Assert.Equal(10, control.PowerMicrowatts.Value, 10);
        Assert.Equal(1, control.SlopeMicrowattsPerSecond.Value, 10);
        Assert.True(control.Locked && control.SlopeLocked && control.UserDefined && control.Linear);

        var injection = new InjectionData(experiment, 1e-6f, 10, 0, 1);
        injection.Time = 0;
        injection.SetIntegrationLengthByTime(8);
        injection.SetIntegrationStartTime(2);
        var injectionEvidence = new InterpretationInjectionEvidence();
        AnalysisInterpretationDiagnostics.AddInjectionBaseline(injectionEvidence, experiment, injection);
        Assert.Equal(12, injectionEvidence.BaselineAtIntegrationStartMicrowatts.Value, 10);
        Assert.Equal(18, injectionEvidence.BaselineAtIntegrationEndMicrowatts.Value, 10);
        Assert.Equal(90e-6, injectionEvidence.IntegratedBaselineCorrectionJoules.Value, 12);

        experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Segmented);
        var segmented = Assert.IsType<SegmentedBaselineInterpolator>(experiment.Processor.Interpolator);
        segmented.RestoreSegments(new[] { new SegmentedBaselineInterpolator.BaselineSegment(
            SegmentedBaselineInterpolator.BaselineSegmentKind.InitialDelay, -1, 0, 10, 5, new[] { 10e-6, 1e-6 }) });
        Assert.Single(AnalysisInterpretationDiagnostics.Baseline(experiment, "e").Segmented.Segments);

        experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.ASL);
        var als = Assert.IsType<AssymetricLeastSquaresInterpolator>(experiment.Processor.Interpolator);
        als.Iterations = 17; als.Lambda = 2200; als.Asymmetry = 0.91;
        Assert.Equal(17, AnalysisInterpretationDiagnostics.Baseline(experiment, "e").AsymmetricLeastSquares.Iterations);
    }

    [Theory]
    [InlineData("An assessment without a heading.")]
    [InlineData("## Limitations\nKd < 1 µM; signal > blank.")]
    [InlineData("## Unknown\n**Unmatched* emphasis.\n  - nested\n[Reference](https://example.test)")]
    [InlineData("")]
    public void DraftFormattingDoesNotBlockApprovalOrRendering(string markdown)
    {
        Assert.Equal(markdown, AnalysisInterpretationResponseParser.ParseForApproval(markdown));
        var report = new AnalysisReport();
        report.ApproveInterpretation(new AnalysisInterpretationRecord { InterpretationMarkdown = markdown });
        Assert.Equal(markdown, report.ApprovedInterpretation.InterpretationMarkdown);
        AnalysisReportBuilder.BuildInterpretationPreview(markdown, report.ApprovedInterpretation);
    }

    [Fact]
    public void LongGeneratedTextIsPreservedForApproval()
    {
        var markdown = "## Overall interpretation\r\n" + new string('x', 25000);
        var expected = markdown.Replace("\r\n", "\n");
        Assert.Equal(expected, AnalysisInterpretationResponseParser.Parse(markdown));
        var report = new AnalysisReport();
        report.ApproveInterpretation(new AnalysisInterpretationRecord { InterpretationMarkdown = markdown });
        Assert.Equal(expected, report.ApprovedInterpretation.InterpretationMarkdown);
    }

    [Fact]
    public async Task NonFinitePackageValuesBecomeExplicitUnavailableNulls()
    {
        var result = await LoadResult();
        var data = result.Solution.Solutions[0].Data;
        data.CellVolume = double.NaN;
        data.Instrument = ITCInstrument.Unknown;
        data.StirringSpeed = -1;
        data.FeedBackMode = FeedbackMode.Null;
        var package = AnalysisInterpretationPackageBuilder.Build(ReportFor(result), result);
        var instrument = package.Result.Experiments[0].Instrument;
        Assert.Null(instrument.ModelId);
        Assert.Null(instrument.ModelName);
        Assert.Null(instrument.CellVolumeLitres);
        Assert.Null(instrument.StirringSpeedRpm);
        Assert.Null(instrument.FeedbackMode);
        var prompt = AnalysisInterpretationPromptBuilder.Build(
            package);
        Assert.Contains("\"cellVolumeLitres\":null", prompt.CanonicalPackageJson, StringComparison.Ordinal);
        Assert.DoesNotContain("NaN", prompt.CanonicalPackageJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", prompt.CanonicalPackageJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerationIsTransientUntilExplicitApprovalAndReportAdapterShowsProvenanceAndStaleness()
    {
        var result = await LoadResult();
        var report = ReportFor(result);
        var provider = new StubProvider();
        var generated = await new AnalysisInterpretationService(provider).GenerateAsync(report, result);

        Assert.Null(report.ApprovedInterpretation);
        report.ApproveInterpretation(generated.Interpretation);
        Assert.NotEqual(default, report.ApprovedInterpretation.ApprovedAtUtc);
        var current = AnalysisReportBuilder.Build(report, id => id == result.UniqueID ? result : null);
        Assert.Equal(AnalysisReportSectionKind.Interpretation, current.Sections[2].Kind);
        var interpretation = current.Sections[2];
        Assert.Contains(interpretation.Blocks.OfType<AnalysisReportHeadingBlock>(), block => block.Text == "Overall interpretation");
        Assert.Contains(interpretation.Blocks.OfType<AnalysisReportHeadingBlock>(), block => block.Text == "Binding conclusion" && block.Level == 3);
        Assert.Contains(interpretation.Blocks.OfType<AnalysisReportHeadingBlock>(), block => block.Text == "Suggested checks");
        Assert.Contains(interpretation.Blocks.OfType<AnalysisReportTextBlock>(), block => block.Text.StartsWith("• Compare", StringComparison.Ordinal));
        Assert.Contains(interpretation.Blocks.OfType<AnalysisReportNoticeBlock>(), block => block.Title == "Provenance");
        Assert.DoesNotContain(interpretation.Blocks.OfType<AnalysisReportNoticeBlock>(), block => block.Title.Contains("Stale"));

        var changed = report.StudyContext.Copy();
        changed.ExpectedOutcome = "A changed expectation";
        report.UpdateStudyContext(changed);
        var stale = AnalysisReportBuilder.Build(report, _ => result);
        Assert.Contains(stale.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Interpretation)
            .Blocks.OfType<AnalysisReportNoticeBlock>(), block => block.Title == "Stale AI interpretation");
    }

    [Fact]
    public async Task GenerationReportsOrderedProgressThroughValidationAndSuccess()
    {
        var result = await LoadResult();
        var updates = new List<AnalysisInterpretationProgressUpdate>();

        await new AnalysisInterpretationService(new StubProvider()).GenerateAsync(
            ReportFor(result), result, progress: new ImmediateProgress<AnalysisInterpretationProgressUpdate>(updates.Add));

        Assert.Equal(new[]
        {
            AnalysisInterpretationProgressStage.BuildingPackage,
            AnalysisInterpretationProgressStage.SendingRequest,
            AnalysisInterpretationProgressStage.WaitingForServer,
            AnalysisInterpretationProgressStage.ValidatingResponse,
            AnalysisInterpretationProgressStage.Finished,
        }, updates.Select(update => update.Stage));
        Assert.True(updates[^1].Succeeded);
    }

    [Fact]
    public async Task ManualInterpretationHasExplicitProvenanceAndIsNotEvaluatedAsAiOutput()
    {
        var result = await LoadResult();
        var report = ReportFor(result);

        report.SetManualInterpretation("A manually written scientific interpretation.");

        Assert.Equal(AnalysisInterpretationOrigin.Manual, report.ApprovedInterpretation.Origin);
        Assert.Empty(report.ApprovedInterpretation.Provider);
        Assert.Empty(report.ApprovedInterpretation.Model);
        Assert.Equal(AnalysisInterpretationFreshness.Current,
            AnalysisInterpretationService.EvaluateFreshness(report, result).Status);
        var document = AnalysisReportBuilder.Build(report, _ => result);
        var section = document.Sections.Single(item => item.Kind == AnalysisReportSectionKind.Interpretation);
        var provenance = Assert.Single(section.Blocks.OfType<AnalysisReportNoticeBlock>(),
            item => item.Title == "Provenance");
        Assert.Contains("written by the user", provenance.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("AI-generated", provenance.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ManualInterpretationAllowsOrdinaryScientificComparisonTextAndLegacyOriginDefaultsToAi()
    {
        var report = new AnalysisReport();
        report.SetManualInterpretation("The fitted Kd is < 1 µM and the response is > baseline noise.");

        Assert.Contains("Kd is < 1 µM", report.ApprovedInterpretation.InterpretationMarkdown, StringComparison.Ordinal);
        var legacy = JsonSerializer.Deserialize<AnalysisInterpretationRecord>("{}");
        Assert.Equal(AnalysisInterpretationOrigin.AiGenerated, legacy.Origin);
    }

    [Fact]
    public async Task EditingApprovedAiInterpretationPreservesOriginAndMarksItEdited()
    {
        var result = await LoadResult();
        var report = ReportFor(result);
        var generated = await new AnalysisInterpretationService(new StubProvider()).GenerateAsync(report, result);
        report.ApproveInterpretation(generated.Interpretation);

        report.UpdateApprovedInterpretationText(
            generated.Interpretation.InterpretationMarkdown.Replace("The interaction", "This interaction"));

        Assert.Equal(AnalysisInterpretationOrigin.AiGenerated, report.ApprovedInterpretation.Origin);
        Assert.True(report.ApprovedInterpretation.UserEdited);
        Assert.Equal("stub", report.ApprovedInterpretation.Provider);
    }

    [Fact]
    public async Task FailedOrCancelledGenerationDoesNotChangeApprovedInterpretation()
    {
        var result = await LoadResult();
        var report = ReportFor(result);
        report.SetManualInterpretation("Keep this interpretation.");
        var before = report.ApprovedInterpretation;
        var updates = new List<AnalysisInterpretationProgressUpdate>();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new AnalysisInterpretationService(new ThrowingProvider()).GenerateAsync(
                report, result, progress: new ImmediateProgress<AnalysisInterpretationProgressUpdate>(updates.Add)));

        Assert.Equal(before.InterpretationMarkdown, report.ApprovedInterpretation.InterpretationMarkdown);
        Assert.Equal(before.Origin, report.ApprovedInterpretation.Origin);
        Assert.Equal(AnalysisInterpretationProgressStage.Finished, updates[^1].Stage);
        Assert.False(updates[^1].Succeeded);
        Assert.Contains("cancelled", updates[^1].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ftxtc16RoundTripsReportAndRetainsUnresolvedResultReference()
    {
        var result = await LoadResult();
        var report = ReportFor(result);
        report.SetResultIds(new[] { "missing-result-id" });
        var supportingId = result.Solution.Solutions[0].Data.UniqueID;
        report.SetSupportingExperimentIds(new[] { supportingId });
        report.ApproveInterpretation(new AnalysisInterpretationRecord
        {
            InterpretationMarkdown = "## Overall interpretation\nThe saved interpretation is retained.",
            InputFingerprint = "saved-fingerprint",
            PromptVersion = AnalysisInterpretationPromptBuilder.PromptVersion,
            OutputFormatVersion = AnalysisInterpretationPromptBuilder.OutputFormatVersion,
            Provider = "test-provider",
            Model = "test-model",
            GeneratedAtUtc = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc),
        });

        using var package = new MemoryStream();
        await FTXTCWriter.WriteStream(package,
            result.Solution.Solutions.Select(solution => solution.Data),
            new[] { result },
            result.Solution.Solutions.Select(solution => (ITCDataContainer)solution.Data).Concat(new[] { result }),
            new[] { report });
        package.Position = 0;
        var restored = await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.Strict);

        var restoredReport = Assert.Single(restored.Reports);
        Assert.Equal("missing-result-id", Assert.Single(restoredReport.ResultIds));
        Assert.Equal(supportingId, Assert.Single(restoredReport.SupportingExperimentIds));
        Assert.Equal("Does the ligand bind as expected?", restoredReport.StudyContext.ScientificQuestion);
        Assert.Equal("test-provider", restoredReport.ApprovedInterpretation.Provider);
        Assert.Equal(AnalysisInterpretationOrigin.AiGenerated, restoredReport.ApprovedInterpretation.Origin);
        Assert.Equal(AnalysisInterpretationFreshness.Unverifiable,
            AnalysisInterpretationService.EvaluateFreshness(restoredReport, null).Status);

        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        Assert.NotNull(archive.GetEntry("reports/000000/report.json"));
        using var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json").Open());
        Assert.Equal(6, manifest.RootElement.GetProperty("schemaMinor").GetInt32());
    }

    [Fact]
    public async Task RelayPayloadContainsOnlyControlledBoundaryFields()
    {
        var handler = new RelayHandler();
        var client = new FtItcInterpretationClient(new HttpClient(handler), new Uri("https://app.ft-itc.org"));
        var package = new AnalysisInterpretationPackage
        {
            Report = new InterpretationReportEvidence { EvidenceId = "report-1", ReportId = "r", Name = "R" },
            Result = new InterpretationResultEvidence { EvidenceId = "result-1", ResultId = "x", Name = "X" },
        };
        var prompt = AnalysisInterpretationPromptBuilder.Build(package);
        var response = await client.GenerateAsync(new AnalysisInterpretationGenerationRequest
        {
            ClientRequestId = "client-1", GenerationProfile = "fast", Package = package, Prompt = prompt,
        }, CancellationToken.None);

        Assert.Equal("relay-model", response.Model);
        using var body = JsonDocument.Parse(handler.RequestBody);
        var names = body.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(new[] { "requestSchemaVersion", "promptProfileVersion", "outputFormatVersion", "generationProfile", "package", "clientRequestId" }, names);
        Assert.Equal("/api/interpretation/generate", handler.RequestUri.AbsolutePath);
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task OptInRelayConnectivityUsesAValidMinimalPackage()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FTITC_LIVE_INTERPRETATION_TEST"), "1", StringComparison.Ordinal))
            return;

        var result = await LoadResult();
        var report = ReportFor(result);
        report.UpdateInterpretationSettings(new AnalysisInterpretationOptions
        {
            Detail = AnalysisInterpretationDetail.Concise,
            InjectionRows = AnalysisInterpretationInjectionRows.None,
            RequestedSections = new List<AnalysisInterpretationSection>
            {
                AnalysisInterpretationSection.OverallInterpretation,
            },
        });
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var output = await new AnalysisInterpretationService(
            new FtItcInterpretationClient(httpClient, new Uri("https://app.ft-itc.org")))
            .GenerateAsync(report, result, report.InterpretationSettings);

        Assert.StartsWith("## Overall interpretation", output.Interpretation.InterpretationMarkdown, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(output.Interpretation.Provider));
        Assert.False(string.IsNullOrWhiteSpace(output.Interpretation.Model));
    }

    [Theory]
    [InlineData(413, AnalysisInterpretationFailureKind.PayloadRejected)]
    [InlineData(422, AnalysisInterpretationFailureKind.PayloadRejected)]
    [InlineData(500, AnalysisInterpretationFailureKind.ServiceFailure)]
    public async Task RelayMapsHttpFailuresWithoutRetry(int status, AnalysisInterpretationFailureKind expected)
    {
        var handler = new StatusHandler((HttpStatusCode)status, "{}");
        var exception = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            Client(handler).GenerateAsync(RelayRequest(), CancellationToken.None));
        Assert.Equal(expected, exception.Kind);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RelayPreservesRateLimitRetryAfterAndRejectsInvalidResponses()
    {
        var rateHandler = new StatusHandler((HttpStatusCode)429, "{}", retryAfterSeconds: 17);
        var rate = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            Client(rateHandler).GenerateAsync(RelayRequest(), CancellationToken.None));
        Assert.Equal(AnalysisInterpretationFailureKind.RateLimited, rate.Kind);
        Assert.Equal(TimeSpan.FromSeconds(17), rate.RetryAfter);

        var invalid = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            Client(new StatusHandler(HttpStatusCode.OK, "not-json")).GenerateAsync(RelayRequest(), CancellationToken.None));
        Assert.Equal(AnalysisInterpretationFailureKind.InvalidResponse, invalid.Kind);

        const string incompatible = "{\"responseSchemaVersion\":\"future\",\"requestId\":\"client-1\",\"provider\":\"p\",\"model\":\"m\",\"generatedAtUtc\":\"2026-09-03T09:00:00Z\",\"interpretationMarkdown\":\"## Overall interpretation\\nText.\"}";
        var schema = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            Client(new StatusHandler(HttpStatusCode.OK, incompatible)).GenerateAsync(RelayRequest(), CancellationToken.None));
        Assert.Equal(AnalysisInterpretationFailureKind.IncompatibleSchema, schema.Kind);
    }

    [Fact]
    public async Task RelayDistinguishesCallerCancellationFromTimeout()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var caller = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            Client(new CancellationHandler()).GenerateAsync(RelayRequest(), cancelled.Token));
        Assert.Equal(AnalysisInterpretationFailureKind.Cancelled, caller.Kind);

        var timeout = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() =>
            Client(new CancellationHandler()).GenerateAsync(RelayRequest(), CancellationToken.None));
        Assert.Equal(AnalysisInterpretationFailureKind.Timeout, timeout.Kind);
    }

    static AnalysisReport ReportFor(AnalysisResult result)
    {
        var report = new AnalysisReport { Name = "Interpretation report", Comments = "Author comment" };
        report.SetResultIds(new[] { result.UniqueID });
        report.UpdateStudyContext(new AnalysisStudyContext
        {
            ScientificQuestion = "Does the ligand bind as expected?",
            SystemDescription = "A protein and a small-molecule ligand.",
            ExpectedOutcome = "One saturable interaction.",
        });
        return report;
    }

    static FtItcInterpretationClient Client(HttpMessageHandler handler) =>
        new FtItcInterpretationClient(new HttpClient(handler), new Uri("https://app.ft-itc.org"));

    static AnalysisInterpretationGenerationRequest RelayRequest()
    {
        var package = new AnalysisInterpretationPackage
        {
            Report = new InterpretationReportEvidence { EvidenceId = "report-1", ReportId = "r", Name = "R" },
            Result = new InterpretationResultEvidence { EvidenceId = "result-1", ResultId = "x", Name = "X" },
        };
        return new AnalysisInterpretationGenerationRequest
        {
            ClientRequestId = "client-1", GenerationProfile = "fast", Package = package,
            Prompt = AnalysisInterpretationPromptBuilder.Build(package),
        };
    }

    static async Task<AnalysisResult> LoadResult()
    {
        using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "jors.ftxtc"));
        return (await FTXTCReader.ReadStream(source)).OfType<AnalysisResult>().First();
    }

    sealed class StubProvider : IAnalysisInterpretationProvider
    {
        public Task<AnalysisInterpretationProviderResponse> GenerateAsync(AnalysisInterpretationGenerationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AnalysisInterpretationProviderResponse
            {
                RequestId = request.ClientRequestId,
                Provider = "stub",
                Model = "fast-test",
                GeneratedAtUtc = new DateTime(2026, 9, 3, 9, 0, 0, DateTimeKind.Utc),
                InterpretationMarkdown = "## Overall interpretation\n### Binding conclusion\nThe stored result supports a saturable interaction.\n\n## Suggested checks\n- Compare an independent preparation.",
            });
    }

    sealed class ImmediateProgress<T> : IProgress<T>
    {
        readonly Action<T> report;
        public ImmediateProgress(Action<T> report) => this.report = report;
        public void Report(T value) => report(value);
    }

    sealed class ThrowingProvider : IAnalysisInterpretationProvider
    {
        public Task<AnalysisInterpretationProviderResponse> GenerateAsync(
            AnalysisInterpretationGenerationRequest request, CancellationToken cancellationToken) =>
            Task.FromException<AnalysisInterpretationProviderResponse>(new OperationCanceledException());
    }

    sealed class RelayHandler : HttpMessageHandler
    {
        public string RequestBody { get; private set; }
        public Uri RequestUri { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"responseSchemaVersion\":\"ft-itc-relay-response-2.0\",\"effectiveInputFingerprint\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"omissions\":[],\"knowledgeBaseIds\":[],\"retrievedSourceIds\":[],\"requestId\":\"client-1\",\"provider\":\"relay-provider\",\"model\":\"relay-model\",\"generatedAtUtc\":\"2026-09-03T09:00:00Z\",\"interpretationMarkdown\":\"## Overall interpretation\\nThe result supports binding.\"}", Encoding.UTF8, "application/json"),
            };
        }
    }

    sealed class StatusHandler : HttpMessageHandler
    {
        readonly HttpStatusCode status;
        readonly string body;
        readonly int? retryAfterSeconds;
        public int CallCount { get; private set; }
        public StatusHandler(HttpStatusCode status, string body, int? retryAfterSeconds = null)
        {
            this.status = status;
            this.body = body;
            this.retryAfterSeconds = retryAfterSeconds;
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            if (retryAfterSeconds.HasValue)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(retryAfterSeconds.Value));
            return Task.FromResult(response);
        }
    }

    sealed class CancellationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("simulated"));
    }
}
