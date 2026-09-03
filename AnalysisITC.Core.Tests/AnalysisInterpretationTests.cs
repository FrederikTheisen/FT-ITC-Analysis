using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class AnalysisInterpretationTests
{
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
        Assert.Contains("https://json-schema.org/draft/2020-12/schema", prompt.OutputJsonSchema, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageAndPromptAreDeterministicAndSeparateKdFromFittedLogAffinity()
    {
        var result = await LoadResult();
        result.Solution.Solutions[0].Data.SetFileName("/private/studies/secret/source.itc");
        var report = ReportFor(result);

        var firstPackage = AnalysisInterpretationPackageBuilder.Build(report, result);
        var secondPackage = AnalysisInterpretationPackageBuilder.Build(report, result);
        var first = AnalysisInterpretationPromptBuilder.Build(firstPackage);
        var second = AnalysisInterpretationPromptBuilder.Build(secondPackage);

        Assert.Equal(first.CanonicalPackageJson, second.CanonicalPackageJson);
        Assert.Equal(first.SystemInstructions, second.SystemInstructions);
        Assert.Equal(first.InputFingerprint, second.InputFingerprint);
        Assert.DoesNotContain("/private/studies", first.CanonicalPackageJson, StringComparison.Ordinal);
        Assert.DoesNotContain("dataPoints", first.CanonicalPackageJson, StringComparison.OrdinalIgnoreCase);
        Assert.False(firstPackage.DataBoundary.ContainsRawThermogramSamples);
        Assert.Equal("source.itc", firstPackage.Result.Experiments[0].SourceFileBasename);

        var affinity = firstPackage.Result.Experiments[0].Parameters.Single(parameter => parameter.QuantityId == "kd-1");
        Assert.Equal("affinity-log10-1", affinity.FittedCoordinateId);
        Assert.Equal("mol/L", affinity.SiUnit);
        Assert.True(affinity.IsDerived);
        Assert.NotEmpty(firstPackage.Result.Experiments[0].Injections);
        Assert.Equal(result.Solution.Solutions[0].Data.Injections.Count,
            firstPackage.Result.Experiments[0].Injections.Count);
        Assert.All(firstPackage.Result.Experiments[0].Injections, injection =>
            Assert.StartsWith("result-1/experiment-1/injection-", injection.EvidenceId, StringComparison.Ordinal));
        var sourceInjection = result.Solution.Solutions[0].Data.Injections[0];
        var packagedInjection = firstPackage.Result.Experiments[0].Injections[0];
        var independentlyCalculatedMass = result.Solution.Solutions[0].Data.SyringeConcentration.Value * sourceInjection.Volume;
        Assert.Equal(sourceInjection.PeakArea.Value / independentlyCalculatedMass,
            packagedInjection.ObservedHeatJoulesPerMole.Value, 10);
        Assert.Equal(sourceInjection.PeakArea.SD / independentlyCalculatedMass,
            packagedInjection.ObservedUncertaintyJoulesPerMole.Value, 10);
        Assert.Equal(packagedInjection.ObservedHeatJoulesPerMole.Value - packagedInjection.FittedHeatJoulesPerMole.Value,
            packagedInjection.ResidualJoulesPerMole.Value, 8);
        Assert.Equal(sourceInjection.Include, packagedInjection.Included);

        result.Solution.UseWeightedFitting = true;
        var weighted = AnalysisInterpretationPackageBuilder.Build(report, result);
        Assert.True(weighted.Result.Model.UsesWeightedFitting);
        Assert.True(weighted.Result.Solver.UsesWeightedObjective);
        Assert.NotNull(weighted.Result.Solver.UnweightedRmsdMicrojoules);
    }

    [Fact]
    public async Task ParserAcceptsOptionalSectionsAndRejectsInventedEvidenceAndUnverifiedKnowledge()
    {
        var result = await LoadResult();
        var package = AnalysisInterpretationPackageBuilder.Build(ReportFor(result), result);
        const string valid = """
        {"fitQualityObservations":[{"text":"The fit converged.","kind":"observation","confidence":"high","knowledgeBasis":"experimentalData","requiresExternalVerification":false,"evidenceIds":["result-1"]}]}
        """;
        var parsed = AnalysisInterpretationResponseParser.Parse(valid, package);
        Assert.Single(parsed.FitQualityObservations);

        var unknownEvidence = valid.Replace("result-1", "result-99", StringComparison.Ordinal);
        Assert.Throws<AnalysisInterpretationValidationException>(() =>
            AnalysisInterpretationResponseParser.Parse(unknownEvidence, package));

        var general = valid.Replace("experimentalData", "generalKnowledge", StringComparison.Ordinal);
        Assert.Throws<AnalysisInterpretationValidationException>(() =>
            AnalysisInterpretationResponseParser.Parse(general, package));

        Assert.Throws<AnalysisInterpretationValidationException>(() =>
            AnalysisInterpretationResponseParser.Parse("{\"inventedHeading\":[]}", package));
        Assert.Throws<AnalysisInterpretationValidationException>(() =>
            AnalysisInterpretationResponseParser.Parse(valid.Replace("The fit converged.", "**The fit converged.**"), package));
    }

    [Fact]
    public async Task NonFinitePackageValuesBecomeExplicitUnavailableNulls()
    {
        var result = await LoadResult();
        result.Solution.Solutions[0].Data.CellVolume = double.NaN;
        var prompt = AnalysisInterpretationPromptBuilder.Build(
            AnalysisInterpretationPackageBuilder.Build(ReportFor(result), result));
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
    public async Task Ftxtc16RoundTripsReportAndRetainsUnresolvedResultReference()
    {
        var result = await LoadResult();
        var report = ReportFor(result);
        report.SetResultIds(new[] { "missing-result-id" });
        report.ApproveInterpretation(new AnalysisInterpretationRecord
        {
            Interpretation = new AnalysisInterpretationDocument(),
            InputFingerprint = "saved-fingerprint",
            PromptVersion = AnalysisInterpretationPromptBuilder.PromptVersion,
            OutputSchemaVersion = AnalysisInterpretationPromptBuilder.OutputSchemaVersion,
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
        Assert.Equal("Does the ligand bind as expected?", restoredReport.StudyContext.ScientificQuestion);
        Assert.Equal("test-provider", restoredReport.ApprovedInterpretation.Provider);
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
        Assert.Equal(new[] { "requestSchemaVersion", "promptProfileVersion", "outputSchemaVersion", "generationProfile", "package", "clientRequestId" }, names);
        Assert.Equal("/api/interpretation/generate", handler.RequestUri.AbsolutePath);
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

        const string incompatible = "{\"responseSchemaVersion\":\"future\",\"requestId\":\"client-1\",\"provider\":\"p\",\"model\":\"m\",\"generatedAtUtc\":\"2026-09-03T09:00:00Z\",\"document\":{}}";
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
                DocumentJson = "{\"fitQualityObservations\":[{\"text\":\"The stored fit is available.\",\"kind\":\"observation\",\"confidence\":\"high\",\"knowledgeBasis\":\"experimentalData\",\"requiresExternalVerification\":false,\"evidenceIds\":[\"result-1\"]}]}",
            });
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
                Content = new StringContent("{\"responseSchemaVersion\":\"ft-itc-relay-response-1.0\",\"requestId\":\"client-1\",\"provider\":\"relay-provider\",\"model\":\"relay-model\",\"generatedAtUtc\":\"2026-09-03T09:00:00Z\",\"document\":{}}", Encoding.UTF8, "application/json"),
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
