using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisInterpretationCollectionTests
{
    [Fact]
    public void CompressionHasIndependentExtremaTiesEndpointsAndBaselineExpectations()
    {
        var values = new[] { (5d, 10d), (6d, 8d), (7d, 8d), (19d, 14d), (20d, 12d), (24d, 11d), (35d, 10d), (36d, 15d), (37d, 12d) };
        var trace = AnalysisInterpretationThermograms.Compress(values.Select(item => (item.Item1, item.Item2 * 1e-6, (double?)(9e-6))));
        Assert.Equal(new[] { 1, 3, 4, 5, 6, 7 }, trace.Samples.Select(item => item.SourceIndex));
        Assert.Equal(new[] { 0, 8 }, trace.Endpoints.Select(item => item.SourceIndex));
        Assert.Equal(5, trace.AnchorTimeSeconds);
        Assert.Equal(11e-6, trace.PowerOffsetWatts, 15);
        Assert.Equal(9, trace.SourceSampleCount);
        Assert.Equal(8, trace.RetainedSampleCount);
        Assert.All(trace.Samples.Concat(trace.Endpoints), sample =>
        {
            Assert.Equal(values[sample.SourceIndex].Item2, sample.RelativePowerMicrowatts + 11, 10);
            Assert.Equal(-2, sample.RelativeBaselineMicrowatts.Value, 10);
            Assert.Equal(values[sample.SourceIndex].Item1, sample.TimeSeconds);
        });
    }

    [Theory]
    [InlineData("PRLR1")]
    [InlineData("PRLR2")]
    [InlineData("Rocu")]
    [InlineData("Tandem")]
    public void SuppliedTracesRetainExactOriginalExtremaAndReconstructPower(string name)
    {
        var source = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ThermogramCompression", name + ".csv")).Skip(1)
            .Select(line => line.Split(',')).Select(values => (Time: double.Parse(values[0], CultureInfo.InvariantCulture), Power: double.Parse(values[1], CultureInfo.InvariantCulture))).ToList();
        var trace = AnalysisInterpretationThermograms.Compress(source.Select(item => (item.Time, item.Power, (double?)null)));
        // Independent scan: compare every retained extreme with every source sample in its bin.
        var expected = new HashSet<int>();
        for (var start = source[0].Time; start <= source[^1].Time; start += 15)
        {
            int low = -1, high = -1;
            for (var i = 0; i < source.Count; i++)
                if (source[i].Time >= start && source[i].Time < start + 15)
                {
                    if (low < 0 || source[i].Power < source[low].Power) low = i;
                    if (high < 0 || source[i].Power > source[high].Power) high = i;
                }
            if (low >= 0) { expected.Add(low); expected.Add(high); }
        }
        Assert.Equal(expected.OrderBy(index => index), trace.Samples.Select(item => item.SourceIndex));
        Assert.All(trace.Samples.Concat(trace.Endpoints), sample =>
            Assert.Equal(source[sample.SourceIndex].Power, sample.RelativePowerMicrowatts / 1e6 + trace.PowerOffsetWatts, 14));
        Assert.Equal(trace.RetainedSampleCount, trace.Samples.Concat(trace.Endpoints).Select(item => item.SourceIndex).Distinct().Count());
    }

    [Fact]
    public async Task CollectionsPreserveRepeatedResultsAndSuppressSupportingMemberDuplicates()
    {
        var result = await Load();
        var alternative = new AnalysisResult(result.Solution) { Name = "Separate analysis of the same observations" };
        var support = Supporting();
        var report = Report(result, alternative);
        var member = result.Solution.Solutions[0].Data;
        report.SetSupportingExperimentIds(new[] { member.UniqueID, support.UniqueID });
        var package = AnalysisInterpretationPackageBuilder.Build(report, id => id == result.UniqueID ? result : alternative,
            id => id == support.UniqueID ? support : member);
        Assert.Equal(2, package.Results.Count);
        Assert.Single(package.SupportingExperiments);
        Assert.Equal("1A", package.Results[0].Experiments[0].ReportReference);
        Assert.Equal("2A", package.Results[1].Experiments[0].ReportReference);
        Assert.Equal(package.Results[0].Experiments[0].ExperimentId, package.Results[1].Experiments[0].ExperimentId);
        var supporting = package.SupportingExperiments[0];
        Assert.Equal("S1", supporting.ReportReference);
        Assert.Empty(supporting.Parameters);
        Assert.Null(supporting.InformationCriteria);
        Assert.False(supporting.ResidualDiagnostics.IsAvailable);
        Assert.All(supporting.Injections, item => Assert.Null(item.FittedHeatJoulesPerMole));
        Assert.Equal(package.EvidenceCatalog.Count, package.EvidenceCatalog.Select(item => item.Id).Distinct().Count());
        Assert.Throws<InvalidOperationException>(() => AnalysisInterpretationPackageBuilder.Build(report, _ => null, _ => support));
        Assert.Throws<InvalidOperationException>(() => AnalysisInterpretationPackageBuilder.Build(report, _ => result, _ => null));
    }

    [Fact]
    public async Task StaleConcentrationsKeepHistoricalInputsSeparateAndSuppressMatchedDiagnostics()
    {
        var result = await Load();
        result.SetValiditySnapshot(AnalysisResultValiditySnapshot.Capture(result.Solution));
        var data = result.Solution.Solutions[0].Data;
        var originalConcentration = data.CellConcentration.Value;
        var originalEstimate = result.Solution.Solutions[0].ReportParameters.First().Value.Value;
        data.CellConcentration = new FloatWithError(originalConcentration * 2);
        var package = AnalysisInterpretationPackageBuilder.Build(Report(result), result);
        Assert.NotEmpty(package.Result.ValidityReasons);
        Assert.NotEmpty(package.Result.HistoricalFitInputs);
        Assert.Equal(originalConcentration, package.Result.HistoricalFitInputs[0].GetProperty("cellConcentration").GetDouble());
        Assert.Equal(2 * originalConcentration, package.Result.Experiments[0].CellConcentrationMolar);
        Assert.Null(package.Result.InformationCriteria);
        Assert.All(package.Result.Experiments, item =>
        {
            Assert.Null(item.InformationCriteria);
            Assert.False(item.ResidualDiagnostics.IsAvailable);
            Assert.All(item.Injections, injection => { Assert.Null(injection.ResidualJoulesPerMole); Assert.Null(injection.Confidence95LowerJoulesPerMole); });
        });
        Assert.Equal(originalEstimate, result.Solution.Solutions[0].ReportParameters.First().Value.Value);
    }

    [Fact]
    public async Task OmittedTracesStillAffectMultiResultFreshnessAndProvenanceRoundTrips()
    {
        var result = await Load(); var other = new AnalysisResult(result.Solution); var support = Supporting();
        var report = Report(result, other); report.SetSupportingExperimentIds(new[] { support.UniqueID });
        var options = report.InterpretationSettings.Copy(); options.IncludeThermograms = false;
        report.UpdateInterpretationSettings(options);
        AnalysisResult Resolve(string id) => id == result.UniqueID ? result : other;
        var package = AnalysisInterpretationPackageBuilder.Build(report, Resolve, _ => support);
        Assert.All(package.Results.SelectMany(item => item.Experiments).Concat(package.SupportingExperiments), item => Assert.Null(item.Thermogram));
        var prompt = AnalysisInterpretationPromptBuilder.Build(package);
        report.ApproveInterpretation(new AnalysisInterpretationRecord
        {
            InterpretationMarkdown = "## Overall interpretation\nStored scientific assessment.",
            InputFingerprint = prompt.InputFingerprint, EffectiveInputFingerprint = "effective-test",
            PromptVersion = prompt.PromptVersion, OutputFormatVersion = prompt.OutputFormatVersion,
            EvidenceFingerprintScheme = AnalysisInterpretationPromptBuilder.EvidenceFingerprintScheme,
            ScientificGuidanceRevision = "science-r1", ScientificInstructionsFingerprint = new string('b', 64),
            OutputInstructionsFingerprint = prompt.OutputInstructionsFingerprint,
            Omissions = package.Omissions, KnowledgeBaseIds = new List<string> { "vs_test" }, RetrievedSourceIds = new List<string> { "file_test" },
        });
        Assert.Equal(AnalysisInterpretationFreshness.Current, AnalysisInterpretationService.EvaluateFreshness(report, Resolve, _ => support).Status);
        // Server-owned guidance revisions are provenance only; unchanged local evidence remains current.
        var revised = report.ApprovedInterpretation.Copy();
        revised.ScientificGuidanceRevision = "science-r2";
        report.ApproveInterpretation(revised);
        Assert.Equal(AnalysisInterpretationFreshness.Current, AnalysisInterpretationService.EvaluateFreshness(report, Resolve, _ => support).Status);
        support.DataPoints[1] = new DataPoint(1, 0.1f);
        Assert.Equal(AnalysisInterpretationFreshness.Stale, AnalysisInterpretationService.EvaluateFreshness(report, Resolve, _ => support).Status);
        using var stream = new MemoryStream();
        var data = result.Solution.Solutions.Select(item => item.Data).Concat(new[] { support }).ToList();
        await FTXTCWriter.WriteStream(stream, data, new[] { result, other }, data.Cast<ITCDataContainer>().Concat(new[] { result, other }), new[] { report });
        stream.Position = 0;
        var restored = await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.Strict);
        var saved = Assert.Single(restored.Reports);
        Assert.Equal("effective-test", saved.ApprovedInterpretation.EffectiveInputFingerprint);
        Assert.Equal(AnalysisInterpretationPromptBuilder.EvidenceFingerprintScheme, saved.ApprovedInterpretation.EvidenceFingerprintScheme);
        Assert.Equal("science-r2", saved.ApprovedInterpretation.ScientificGuidanceRevision);
        Assert.Equal(new string('b', 64), saved.ApprovedInterpretation.ScientificInstructionsFingerprint);
        Assert.Equal(prompt.OutputInstructionsFingerprint, saved.ApprovedInterpretation.OutputInstructionsFingerprint);
        Assert.Equal("vs_test", Assert.Single(saved.ApprovedInterpretation.KnowledgeBaseIds));
        Assert.Equal("file_test", Assert.Single(saved.ApprovedInterpretation.RetrievedSourceIds));
        Assert.False(saved.InterpretationSettings.IncludeThermograms);
        Assert.NotEmpty(saved.ApprovedInterpretation.Omissions);
    }

    [Theory]
    [InlineData(851)]
    [InlineData(1200)]
    public void GeneratedDraftAboveEditorialTargetCanBeApproved(int words)
    {
        var markdown = "## Overall interpretation\n" + string.Join(" ", Enumerable.Repeat("evidence", words));
        Assert.Equal(markdown, AnalysisInterpretationResponseParser.Parse(markdown));
        Assert.Equal(markdown, AnalysisInterpretationResponseParser.ParseForApproval(markdown));
        Assert.Contains("evidence", AnalysisInterpretationResponseParser.ParseManual(markdown));
    }

    [Fact]
    public void GeneratedDraftWithUnusualMarkdownRemainsAcceptable()
    {
        var markdown = "A long plain response with **bold evidence** and a pipe | preserved\n" +
            string.Join("\n", Enumerable.Repeat("- observation with unusual spacing", 1_200));
        Assert.Equal(markdown, AnalysisInterpretationResponseParser.Parse(markdown));
        Assert.Equal(markdown, AnalysisInterpretationResponseParser.ParseForApproval(markdown));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(30)]
    public async Task ReportScaleDoesNotRemoveResultsOrRefit(int count)
    {
        var loaded = await Load();
        var results = Enumerable.Range(0, count).Select(index => new AnalysisResult(loaded.Solution) { Name = "Analysis " + (index + 1) }).ToList();
        var report = Report(results.ToArray());
        var before = loaded.Solution.Solutions[0].ReportParameters.ToDictionary(item => item.Key, item => item.Value.Value);
        var package = AnalysisInterpretationPackageBuilder.Build(report, id => results.Single(item => item.UniqueID == id), _ => null);
        Assert.Equal(count, package.Results.Count);
        var initialHash = AnalysisInterpretationPromptBuilder.Build(package).InputFingerprint;
        AnalysisInterpretationThermograms.OmitTraces(package, "Local size exercise");
        Assert.Equal(count, package.Results.Count);
        Assert.NotEqual(initialHash, AnalysisInterpretationPromptBuilder.Build(package).InputFingerprint);
        Assert.Equal(before, loaded.Solution.Solutions[0].ReportParameters.ToDictionary(item => item.Key, item => item.Value.Value));
    }

    [Fact]
    public async Task ProfileCoordinatesRetainOneSidedOutcomesBoundsAndWarnings()
    {
        var result = await Load();
        var member = result.Solution.Solutions[0];
        var coordinate = new ProfileCoordinateResult(new ProfileCoordinateId(ParameterType.Affinity1, ParameterBoundaryScope.Local, member.Data.UniqueID),
            6, 1, 12, new ProfileSideResult(ProfileSideOutcome.BoundReachedBeforeCrossing, warnings: new[] { "Lower search reached a bound." }),
            new ProfileSideResult(ProfileSideOutcome.EndpointFound, 8), new[] { "Broad likelihood profile." });
        result.Solution.ProfileLikelihoodRun = new ProfileLikelihoodRunResult(.95, ProfileLikelihoodCalibration.UnweightedFCalibratedRss,
            20, 4, 1, 16, 1, 1, SolverAlgorithm.NelderMead, false, 1, 10, 24, 40, TimeSpan.Zero,
            ErrorEstimationOutcome.PartialFailure, new[] { coordinate });
        var package = AnalysisInterpretationPackageBuilder.Build(Report(result), result);
        var profile = package.Result.Solver.ProfileLikelihood;
        var exported = Assert.Single(profile.Coordinates);
        Assert.Equal("affinity-log10-1", exported.FittedCoordinateId);
        Assert.Equal(6, exported.BestFitValue);
        Assert.Equal(1, exported.LowerBound); Assert.Equal(12, exported.UpperBound);
        Assert.Equal("BoundReachedBeforeCrossing", exported.Lower.Outcome); Assert.Null(exported.Lower.Endpoint);
        Assert.Equal(8, exported.Upper.Endpoint);
        Assert.Contains("Lower search reached a bound.", exported.Lower.Warnings);
        Assert.Contains("not empirical Gaussian", profile.StandardDeviationSemantics);
        Assert.Equal("PartialFailure", profile.Outcome);
    }

    [Fact]
    public async Task BootstrapAndBroadProfileEvidenceRemainSeparateForRepeatedExperiments()
    {
        var bootstrap = await Load(); var profile = await Load(); profile.SetID("profile-comparison");
        var member = profile.Solution.Solutions[0];
        var logK = member.Parameters[ParameterType.Affinity1].Value;
        profile.Solution.Model.ModelCloneOptions.ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood;
        member.ErrorMethod = ErrorEstimationMethod.ProfileLikelihood;
        var run = new ProfileLikelihoodRunResult(.95, ProfileLikelihoodCalibration.UnweightedFCalibratedRss,
            20, 4, 1, 16, 1, 1, SolverAlgorithm.NelderMead, false, 1, 10, 24, 40, TimeSpan.Zero,
            ErrorEstimationOutcome.Completed, new[] { new ProfileCoordinateResult(
                new ProfileCoordinateId(ParameterType.Affinity1, ParameterBoundaryScope.Local, member.Data.UniqueID), logK, logK - 5, logK + 5,
                new ProfileSideResult(ProfileSideOutcome.EndpointFound, logK - 4), new ProfileSideResult(ProfileSideOutcome.EndpointFound, logK + 4)) });
        member.ProfileLikelihoodRun = run; profile.Solution.ProfileLikelihoodRun = run;
        var report = Report(bootstrap, profile);
        var package = AnalysisInterpretationPackageBuilder.Build(report, id => id == bootstrap.UniqueID ? bootstrap : profile, _ => null);
        Assert.Equal("BootstrapResiduals", package.Results[0].Solver.ErrorEstimationMethod);
        Assert.Equal("ProfileLikelihood", package.Results[1].Solver.ErrorEstimationMethod);
        Assert.Equal(package.Results[0].Experiments[0].ExperimentId, package.Results[1].Experiments[0].ExperimentId);
        var kd = package.Results[1].Experiments[0].Parameters.Single(item => item.QuantityId == "kd-1");
        Assert.Equal(Math.Pow(10, -logK), kd.BestFitValue.Value, 14);
        Assert.Equal(Math.Pow(10, -logK - 4), kd.Confidence95Lower.Value, 14);
        Assert.Equal(Math.Pow(10, -logK + 4), kd.Confidence95Upper.Value, 10);
        Assert.NotNull(package.Results[1].Experiments[0].Solver.ProfileLikelihood);
        Assert.NotEqual(package.Results[0].Experiments[0].ReportReference, package.Results[1].Experiments[0].ReportReference);
    }

    [Fact]
    public async Task MissingOptimizerMetadataKeepsStoredEstimatesAndWarning()
    {
        var result = await Load(); result.Solution.Convergence = null;
        var report = Report(result);
        var package = AnalysisInterpretationPackageBuilder.Build(report, result);
        Assert.Null(package.Result.Solver.Termination); Assert.Null(package.Result.Solver.UnweightedRmsdMicrojoules);
        Assert.NotEmpty(package.Result.Experiments[0].Parameters);
        Assert.Contains(AnalysisInterpretationService.GetGenerationWarnings(report, _ => result, _ => null), warning => warning.Contains("termination metadata is unavailable"));
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    public async Task StaleCompetitiveFitRetainsStoredKdWithoutEvaluatingCurrentEquilibrium(double concentration)
    {
        var result = await Load("competitive.ftxtc");
        var member = result.Solution.Solutions[0];
        var logK = member.Parameters[ParameterType.Affinity1].Value;
        member.Data.CellConcentration = new FloatWithError(concentration);
        var package = AnalysisInterpretationPackageBuilder.Build(Report(result), result);
        var evidence = package.Result.Experiments[0];
        Assert.Equal(Math.Pow(10, -logK), evidence.Parameters.Single(item => item.QuantityId == "kd-1").BestFitValue.Value, 14);
        Assert.DoesNotContain(evidence.Parameters, item => item.QuantityId == "apparent-kd");
        Assert.False(evidence.ResidualDiagnostics.IsAvailable);
    }

    [Fact]
    public async Task FixedStoichiometryAndFailedMemberUncertaintyRemainExplicit()
    {
        var result = await Load(); var member = result.Solution.Solutions[0];
        member.Model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, member.Parameters[ParameterType.Nvalue1].Value, islocked: true);
        member.Convergence.SetErrorEstimationOutcome(ErrorEstimationOutcome.CompleteFailure);
        var report = Report(result); var package = AnalysisInterpretationPackageBuilder.Build(report, result);
        Assert.True(package.Result.Experiments[0].Parameters.Single(item => item.FittedCoordinateId == "stoichiometry-1").IsLocked);
        Assert.Equal("CompleteFailure", package.Result.Experiments[0].Solver.ErrorEstimationOutcome);
        Assert.Contains(AnalysisInterpretationService.GetGenerationWarnings(report, _ => result, _ => null), item => item.Contains("uncertainty — CompleteFailure"));
    }

    [Fact]
    public async Task StaleTwoSiteSyringeFractionPreservesOneAlphaAndFixedSiteOptions()
    {
        var result = await Load("two-sites.ftxtc"); var member = result.Solution.Solutions[0];
        member.ModelOptions[AttributeKey.UseSyringeActiveFraction] = ExperimentAttribute.Bool(AttributeKey.UseSyringeActiveFraction, "Syringe fraction", true);
        member.Data.CellConcentration = new FloatWithError(double.NaN);
        var package = AnalysisInterpretationPackageBuilder.Build(Report(result), result);
        var parameters = package.Result.Experiments[0].Parameters;
        var alpha = Assert.Single(parameters, item => item.QuantityId.StartsWith("syringe-active-fraction"));
        Assert.Equal("stoichiometry-1", alpha.FittedCoordinateId);
        Assert.Equal(member.Parameters[ParameterType.Nvalue1].Value, alpha.BestFitValue);
        Assert.DoesNotContain(parameters, item => item.FittedCoordinateId == "stoichiometry-2");
    }

    [Fact]
    public async Task BuildsLocalFullPackageEvaluationBundleWithoutGeneration()
    {
        var first = await Load(); var alternate = await Load("two-sites.ftxtc"); var global = await Load("jors.ftxtc", 1);
        var support = SyntheticSupporting("Supporting A", jump: false, shortInterval: false, tandem: true);
        var jump = SyntheticSupporting("Supporting B", jump: true, shortInterval: false, tandem: false);
        var shortInterval = SyntheticSupporting("Supporting C", jump: false, shortInterval: true, tandem: false);
        var supporting = new[] { support, jump, shortInterval };
        var report = Report(first, alternate, global); report.SetSupportingExperimentIds(supporting.Select(item => item.UniqueID));
        report.UpdateStudyContext(new AnalysisStudyContext
        {
            ScientificQuestion = "Assess what these separate stored analyses establish and whether any model comparisons are justified.",
            AdditionalNotes = "Local evaluation uses stored fits from separate experimental systems plus three synthetic supporting experiments with acquisition and processing evidence. Preparation history is unverified.",
        });
        var results = new[] { first, alternate, global };
        var package = AnalysisInterpretationPackageBuilder.Build(report, id => results.Single(item => item.UniqueID == id), id => supporting.Single(item => item.UniqueID == id));
        Assert.Equal(3, package.Results.Count); Assert.Equal(3, package.SupportingExperiments.Count);
        var destination = Environment.GetEnvironmentVariable("FTITC_EVALUATION_OUTPUT");
        if (string.IsNullOrWhiteSpace(destination)) return;
        Directory.CreateDirectory(destination);
        var prompt = AnalysisInterpretationPromptBuilder.Build(package);
        File.WriteAllText(Path.Combine(destination, "canonical-package.json"), prompt.CanonicalPackageJson);
        File.WriteAllText(Path.Combine(destination, "system-instructions.txt"), prompt.SystemInstructions);
        File.WriteAllText(Path.Combine(destination, "user-message.txt"), prompt.UserMessage);
        File.WriteAllText(Path.Combine(destination, "output-format.txt"), prompt.ResponseFormatInstructions);
        File.WriteAllText(Path.Combine(destination, "manifest.json"), JsonSerializer.Serialize(new
        { prompt.InputFingerprint, prompt.PromptVersion, prompt.OutputFormatVersion, package.PackageSchemaVersion,
            ResultCount = package.Results.Count, MemberCount = package.Results.Sum(item => item.Experiments.Count),
            Source = "Stored local JORS individual/global and two-sites fixtures; three synthetic supporting experiments. No fitting or generation was run." }));
    }

    [Fact]
    public async Task TransportOmitsWholeReportTracesAndRequiresEffectiveProvenance()
    {
        var package = new AnalysisInterpretationPackage();
        var samples = Enumerable.Range(0, 24000).Select(index => new InterpretationThermogramSample
        { SourceIndex = index, TimeSeconds = index * 15, RelativePowerMicrowatts = index / 7.0, RelativeBaselineMicrowatts = 2 }).ToList();
        for (var index = 0; index < 2; index++) package.Results.Add(new InterpretationResultEvidence
        { ResultId = "result-" + index, Experiments = new List<InterpretationExperimentEvidence>
        { new() { Thermogram = new InterpretationThermogramEvidence { Samples = samples, SourceSampleCount = samples.Count, RetainedSampleCount = samples.Count } } } });
        string body = null;
        using var http = new HttpClient(new CaptureHandler(async request =>
        {
            body = await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            {
                responseSchemaVersion = FtItcInterpretationClient.ResponseSchemaVersion, requestId = "test", provider = "mock", model = "mock",
                effectivePreset = "instant", presetRevision = "test-1",
                generatedAtUtc = DateTime.UtcNow, interpretationMarkdown = "## Overall interpretation\nRetained evidence.",
                effectiveInputFingerprint = new string('a', 64), omissions = Array.Empty<string>(), knowledgeBaseIds = Array.Empty<string>(), retrievedSourceIds = Array.Empty<string>(),
                scientificGuidanceRevision = "test-revision", scientificInstructionsFingerprint = new string('b', 64), outputInstructionsFingerprint = new string('c', 64),
            })) };
        }));
        var request = new AnalysisInterpretationGenerationRequest { ClientRequestId = "test", Package = package, Prompt = AnalysisInterpretationPromptBuilder.Build(package) };
        var response = await new FtItcInterpretationClient(http, new Uri("https://mock.invalid")).GenerateAsync(request, CancellationToken.None);
        using var sent = JsonDocument.Parse(body);
        var results = sent.RootElement.GetProperty("package").GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());
        foreach (var result in results.EnumerateArray()) Assert.False(result.GetProperty("experiments")[0].TryGetProperty("thermogram", out _));
        Assert.Contains(response.Omissions, item => item.Contains("2 MiB"));
        Assert.All(package.Results, item => Assert.NotNull(item.Experiments[0].Thermogram));
        Assert.True(Encoding.UTF8.GetByteCount(body) < FtItcInterpretationClient.MaximumRequestBytes);
    }

    [Fact]
    public async Task VersionFourReplyWithoutEffectiveProvenanceIsRejected()
    {
        using var http = new HttpClient(new CaptureHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new
        { responseSchemaVersion = FtItcInterpretationClient.ResponseSchemaVersion, requestId = "test", provider = "mock", model = "mock",
            effectivePreset = "instant", presetRevision = "test-1",
            generatedAtUtc = DateTime.UtcNow, interpretationMarkdown = "## Overall interpretation\nText." })) })));
        var package = new AnalysisInterpretationPackage();
        var error = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() => new FtItcInterpretationClient(http, new Uri("https://mock.invalid")).GenerateAsync(
            new AnalysisInterpretationGenerationRequest { ClientRequestId = "test", Package = package, Prompt = AnalysisInterpretationPromptBuilder.Build(package) }, CancellationToken.None));
        Assert.Equal(AnalysisInterpretationFailureKind.InvalidResponse, error.Kind);
        Assert.Contains("effective-input provenance", error.Message);
    }

    [Fact]
    public async Task TransportSizeOverflowWithoutTracesFailsBeforeSending()
    {
        var package = new AnalysisInterpretationPackage { StudyContext = new AnalysisStudyContext { AdditionalNotes = new string('x', 2 * 1024 * 1024) } };
        var calls = 0;
        using var http = new HttpClient(new CaptureHandler(_ => { calls++; throw new Exception("Must not send"); }));
        var error = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() => new FtItcInterpretationClient(http, new Uri("https://mock.invalid")).GenerateAsync(
            new AnalysisInterpretationGenerationRequest { ClientRequestId = "test", Package = package, Prompt = AnalysisInterpretationPromptBuilder.Build(package) }, CancellationToken.None));
        Assert.Equal(0, calls); Assert.Equal(AnalysisInterpretationFailureKind.PayloadRejected, error.Kind); Assert.Contains("without thermograms", error.Message);
    }

    sealed class CaptureHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request); }

    internal static async Task<AnalysisResult> Load(string file = "jors.ftxtc", int resultIndex = 0)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", file));
        return (await FTXTCReader.ReadStream(stream)).OfType<AnalysisResult>().ElementAt(resultIndex);
    }
    internal static AnalysisReport Report(params AnalysisResult[] results)
    { var report = new AnalysisReport { Name = "Complete report evaluation" }; report.SetResultIds(results.Select(item => item.UniqueID)); return report; }
    static ExperimentData SyntheticSupporting(string name, bool jump, bool shortInterval, bool tandem)
    {
        var data = new ExperimentData("synthetic-support.itc") { Name = name, CellConcentration = new FloatWithError(1e-5), SyringeConcentration = new FloatWithError(1e-4), CellVolume = 2e-4 };
        var times = shortInterval ? new[] { 30, 70, 110 } : new[] { 30, 100, 170 };
        var raw = new List<DataPoint>(); var baseline = new List<Energy>();
        for (var time = 0; time <= 240; time++)
        {
            var background = 40e-6 + time * 1e-8 + (jump && time >= 100 ? 3e-6 : 0);
            var signal = background;
            for (var injection = 0; injection < times.Length; injection++)
                if (time >= times[injection]) signal -= (tandem && injection == 1 ? 0.4e-6 : 4e-6) * Math.Exp(-(time - times[injection]) / 8.0);
            raw.Add(new DataPoint(time, (float)signal, 25)); baseline.Add(new Energy(background));
        }
        data.DataPoints = raw; data.Processor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
        data.Processor.Interpolator.Baseline = baseline;
        for (var i = 0; i < times.Length; i++)
        {
            var duration = tandem && i == 1 ? 0.1 : 1;
            var injection = InjectionData.FromPEAQFile(data, i, true, times[i], duration * 1e-6, shortInterval ? 40 : 70, duration, 25);
            injection.SetIntegrationLengthByTime(shortInterval ? 38 : 40);
            injection.SetPeakArea(new FloatWithError(-(tandem && i == 1 ? 0.4e-6 : 4e-6) * 8 * (1 - Math.Exp(-(shortInterval ? 38 : 40) / 8.0)), 0.2e-6));
            injection.IsIntegrated = true;
            data.Injections.Add(injection);
        }
        if (tandem) { data.AddSegment(new TandemExperimentSegment(0, 1e-5, 0)); data.AddSegment(new TandemExperimentSegment(1, 9.9e-6, 0.5e-6)); }
        return data;
    }

    internal static ExperimentData Supporting()
    {
        var data = new ExperimentData("supporting-blank.itc");
        for (var i = 0; i < 60; i++) data.DataPoints.Add(new DataPoint(i, (float)(40e-6 + i * 1e-9)));
        data.Processor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
        data.Processor.Interpolator.Baseline = data.DataPoints.Select(item => new Energy(item.Power)).ToList();
        var injection = new InjectionData(data, 1e-6f, 20, 0, 1); injection.Time = 10; data.Injections.Add(injection);
        return data;
    }
}
