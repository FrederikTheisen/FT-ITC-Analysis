using System.Text.Json;
using System.Text.Json.Serialization;
using AnalysisITC.Core.Interpretation;

namespace AnalysisITC.Web;

public sealed class InterpretationRequestReader
{
    public const long MaxRequestBytes = 2L * 1024 * 1024;

    static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    static readonly string[] RequiredPackageProperties =
    {
        "packageSchemaVersion",
        "report",
        "results",
        "supportingExperiments",
        "studyContext",
        "requestedInterpretation",
        "evidenceCatalog",
        "dataBoundary",
    };

    public async Task<InterpretationRequestReadResult> ReadAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasJsonContentType())
        {
            return Failed(
                StatusCodes.Status415UnsupportedMediaType,
                "invalid_content_type",
                "Invalid content type",
                "Send the interpretation request as application/json.");
        }

        if (request.ContentLength > MaxRequestBytes)
            return TooLarge();

        InterpretationRelayRequest? envelope;
        try
        {
            using var limitedBody = new SizeLimitedReadStream(request.Body, MaxRequestBytes);
            envelope = await JsonSerializer.DeserializeAsync<InterpretationRelayRequest>(
                limitedBody,
                JsonOptions,
                cancellationToken);
        }
        catch (InterpretationRequestTooLargeException)
        {
            return TooLarge();
        }
        catch (JsonException exception)
        {
            return InvalidJson(JsonPath(exception.Path));
        }

        if (envelope is null)
            return InvalidJson("$");

        AnalysisInterpretationPackage? package = null;
        if (envelope.Package.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            if (envelope.Package.ValueKind != JsonValueKind.Object)
                return InvalidJson("package");

            try
            {
                package = envelope.Package.Deserialize<AnalysisInterpretationPackage>(JsonOptions);
            }
            catch (JsonException exception)
            {
                return InvalidJson(JsonPath(exception.Path, "package"));
            }

            if (package is null)
                return InvalidJson("package");
        }

        var errors = Validate(envelope, package);
        if (errors.Count > 0)
        {
            return Failed(
                StatusCodes.Status422UnprocessableEntity,
                "invalid_interpretation_request",
                "Invalid interpretation request",
                "The interpretation request failed validation.",
                errors);
        }

        return InterpretationRequestReadResult.Valid(new ValidatedInterpretationRequest(
            envelope.ClientRequestId!,
            envelope.GenerationProfile!,
            package!));
    }

    static IReadOnlyDictionary<string, string[]> Validate(
        InterpretationRelayRequest request,
        AnalysisInterpretationPackage? package)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        RequiredVersion(
            errors,
            "requestSchemaVersion",
            request.RequestSchemaVersion,
            FtItcInterpretationClient.RequestSchemaVersion);
        RequiredVersion(
            errors,
            "promptProfileVersion",
            request.PromptProfileVersion,
            AnalysisInterpretationPromptBuilder.PromptVersion);
        RequiredVersion(
            errors,
            "outputFormatVersion",
            request.OutputFormatVersion,
            AnalysisInterpretationPromptBuilder.OutputFormatVersion);
        RequiredVersion(errors, "generationProfile", request.GenerationProfile, "fast");

        if (string.IsNullOrWhiteSpace(request.ClientRequestId))
        {
            Add(errors, "clientRequestId", "This field is required.");
        }
        else if (!IsLowercaseHexRequestId(request.ClientRequestId))
        {
            Add(errors, "clientRequestId", "Use exactly 32 lowercase hexadecimal characters.");
        }

        if (request.Package.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            Add(errors, "package", "This field is required.");
            return Freeze(errors);
        }

        foreach (var propertyName in RequiredPackageProperties)
        {
            if (!request.Package.TryGetProperty(propertyName, out _))
                Add(errors, "package." + propertyName, "This field is required.");
        }

        if (package is null)
            return Freeze(errors);

        RequiredVersion(
            errors,
            "package.packageSchemaVersion",
            package.PackageSchemaVersion,
            AnalysisInterpretationPackageBuilder.PackageSchemaVersion);

        if (package.Report is null)
            Add(errors, "package.report", "This field is required.");
        if (package.Results is null || package.Results.Count == 0)
            Add(errors, "package.results", "At least one result is required.");
        if (package.SupportingExperiments is null) Add(errors, "package.supportingExperiments", "This field is required.");
        if (package.StudyContext is null)
            Add(errors, "package.studyContext", "This field is required.");
        if (package.RequestedInterpretation is null)
            Add(errors, "package.requestedInterpretation", "This field is required.");
        if (package.EvidenceCatalog is null)
            Add(errors, "package.evidenceCatalog", "This field is required.");
        if (package.DataBoundary is null)
            Add(errors, "package.dataBoundary", "This field is required.");

        if (package.Omissions == null || package.Omissions.Any(item => item == null)) Add(errors, "package.omissions", "A nonnull omission collection is required.");
        ValidateCollections(errors, package);
        ValidateStructuredPaths(errors, request.Package, "package");
        ValidateDataBoundary(errors, package.DataBoundary);

        return Freeze(errors);
    }

    static void ValidateCollections(Dictionary<string, List<string>> errors, AnalysisInterpretationPackage package)
    {
        var report = package.Report;
        var results = package.Results;
        if (report is null || results is null || package.SupportingExperiments is null || package.EvidenceCatalog is null) return;
        Required(errors, "package.report.reportId", report.ReportId);
        if (results.Any(item => item == null)) { Add(errors, "package.results", "Null results are not accepted."); return; }
        if (report.ResultIds == null || !report.ResultIds.SequenceEqual(results.Select(item => item.ResultId))
            || report.ResultIds.Distinct(StringComparer.Ordinal).Count() != report.ResultIds.Count)
            Add(errors, "package.report.resultIds", "Ordered result IDs must uniquely match the supplied results.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in package.EvidenceCatalog)
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id))
                Add(errors, "package.evidenceCatalog", "Evidence IDs must be nonempty and unique.");
        foreach (var entry in package.EvidenceCatalog.Where(item => item != null))
            if (entry.ParentId != null && !ids.Contains(entry.ParentId))
                Add(errors, "package.evidenceCatalog", "Every parent ID must resolve.");
        void Evidence(string? id, string path)
        { if (id == null || !ids.Contains(id)) Add(errors, path, "The evidence ID must exist in the evidence catalog."); }
        Evidence(report.EvidenceId, "package.report.evidenceId");
        var expectedReferences = new List<(string Label, string Kind, string Id, string? Parent)>();
        var members = new HashSet<string>(StringComparer.Ordinal);
        var experiments = new List<InterpretationExperimentEvidence>();
        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            var label = AnalysisITC.Core.Presentation.AnalysisReportReferenceLabels.Result(index);
            var unmatched = result.ValidityStatus != "Valid";
            if (unmatched && result.InformationCriteria != null) Add(errors, "package.results.informationCriteria", "Unmatched historical inputs cannot carry current-input information criteria.");
            if (result.HistoricalFitInputs == null) Add(errors, "package.results.historicalFitInputs", "Historical evidence must be a collection.");
            else foreach (var snapshot in result.HistoricalFitInputs)
                ValidateHistoricalSnapshot(errors, snapshot, typeof(AnalysisITC.Core.Data.ExperimentFitInputSnapshot), "package.results.historicalFitInputs");
            Required(errors, "package.results.resultId", result.ResultId);
            Evidence(result.EvidenceId, "package.results.evidenceId");
            if (result.ReportReference != label) Add(errors, "package.results.reportReference", "Result labels must match report order.");
            expectedReferences.Add((label, "result", result.ResultId, null));
            if (result.Model == null || result.Solver == null || result.Experiments == null || result.Experiments.Count == 0)
            { Add(errors, "package.results", "Each result requires model, solver and member evidence."); continue; }
            for (var memberIndex = 0; memberIndex < result.Experiments.Count; memberIndex++)
            {
                var member = result.Experiments[memberIndex];
                if (member == null) { Add(errors, "package.results.experiments", "Null members are not accepted."); continue; }
                var memberLabel = AnalysisITC.Core.Presentation.AnalysisReportReferenceLabels.Experiment(index, memberIndex);
                if (member.ReportReference != memberLabel) Add(errors, "package.results.experiments.reportReference", "Member labels must match result and member order.");
                expectedReferences.Add((memberLabel, "result-experiment", member.ExperimentId, label));
                members.Add(member.ExperimentId); experiments.Add(member);
                if (result.Model.IsGlobal && member.InformationCriteria != null)
                    Add(errors, "package.results.experiments.informationCriteria", "Global-fit members have no independently fitted information criteria.");
                if ((unmatched || !string.IsNullOrWhiteSpace(member.MatchedFitDiagnosticsUnavailableReason))
                    && (member.InformationCriteria != null || member.ResidualDiagnostics?.IsAvailable == true
                        || member.Injections?.Any(item => item != null && (item.FittedHeatJoulesPerMole.HasValue || item.ResidualJoulesPerMole.HasValue
                            || item.Confidence95LowerJoulesPerMole.HasValue || item.Confidence95UpperJoulesPerMole.HasValue)) == true))
                    Add(errors, "package.results.experiments", "Unmatched historical inputs cannot carry matched-fit diagnostics against current observations.");
            }
        }
        var supportingIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < package.SupportingExperiments.Count; index++)
        {
            var item = package.SupportingExperiments[index];
            if (item == null) { Add(errors, "package.supportingExperiments", "Null experiments are not accepted."); continue; }
            var label = AnalysisITC.Core.Presentation.AnalysisReportReferenceLabels.SupportingExperiment(index);
            if (item.ReportReference != label || members.Contains(item.ExperimentId) || !supportingIds.Add(item.ExperimentId))
                Add(errors, "package.supportingExperiments", "Supporting labels must follow report order, without duplicate members or supporting experiments.");
            if (item.Solver != null || item.Parameters?.Count > 0 || item.InformationCriteria != null || item.ResidualDiagnostics?.IsAvailable == true
                || item.Injections?.Any(injection => injection != null && (injection.FittedHeatJoulesPerMole.HasValue || injection.ResidualJoulesPerMole.HasValue || injection.Confidence95LowerJoulesPerMole.HasValue || injection.Confidence95UpperJoulesPerMole.HasValue)) == true)
                Add(errors, "package.supportingExperiments", "Supporting experiments must not contain invented fit evidence.");
            expectedReferences.Add((label, "supporting-experiment", item.ExperimentId, null)); experiments.Add(item);
        }
        if (report.References == null || report.References.Any(item => item == null)
            || !report.References.Select(item => (item.ReportReference, item.Kind, item.Id, (string?)item.ParentReference)).SequenceEqual(expectedReferences))
            Add(errors, "package.report.references", "References must match the ordered result, member and supporting evidence.");
        foreach (var experiment in experiments)
        {
            Evidence(experiment.EvidenceId, "package.experiments.evidenceId");
            Required(errors, "package.experiments.experimentId", experiment.ExperimentId);
            if (experiment.SourceStateFingerprint == null || experiment.SourceStateFingerprint.Length != 64
                || experiment.SourceStateFingerprint.Any(character => !Uri.IsHexDigit(character)))
                Add(errors, "package.experiments.sourceStateFingerprint", "A SHA-256 source-state fingerprint is required, including when traces are omitted.");
            if (experiment.Injections == null || experiment.Injections.Any(item => item == null))
                Add(errors, "package.experiments.injections", "An injection collection without null entries is required.");
            else foreach (var injection in experiment.Injections) Evidence(injection.EvidenceId, "package.experiments.injections.evidenceId");
            if (experiment.Thermogram != null) ValidateTrace(errors, experiment.Thermogram);
        }
        if (package.DataBoundary != null)
        {
            var traces = experiments.Select(item => item.Thermogram).Where(item => item != null).ToList();
            if (package.DataBoundary.ContainsRawThermogramSamples != (traces.Count > 0))
                Add(errors, "package.dataBoundary.containsRawThermogramSamples", "The boundary must match transmitted trace evidence.");
            var hasBaseline = traces.Any(trace => (trace.Samples ?? new()).Concat(trace.Endpoints ?? new()).Any(sample => sample?.RelativeBaselineMicrowatts != null));
            if (package.DataBoundary.ContainsBaselineArrays != hasBaseline)
                Add(errors, "package.dataBoundary.containsBaselineArrays", "The boundary must match sampled baseline evidence.");
        }
    }

    static void ValidateTrace(Dictionary<string, List<string>> errors, InterpretationThermogramEvidence trace)
    {
        const string path = "package.experiments.thermogram";
        if (trace.BinWidthSeconds != 15 || trace.PowerUnit != "µW" || string.IsNullOrWhiteSpace(trace.ReversalFormula)
            || !double.IsFinite(trace.PowerOffsetWatts) || !double.IsFinite(trace.AnchorTimeSeconds)
            || trace.SourceSampleCount < trace.FiniteSampleCount || trace.FiniteSampleCount < trace.RetainedSampleCount
            || trace.Samples == null || trace.Endpoints == null || trace.Endpoints.Count > 2)
        { Add(errors, path, "Invalid compression metadata or missing arrays."); return; }
        var all = trace.Samples.Concat(trace.Endpoints).ToList();
        if (all.Any(sample => sample == null)) { Add(errors, path, "Null samples are not accepted."); return; }
        if (all.Count != trace.RetainedSampleCount || all.Count == 0 || all.Select(sample => sample.SourceIndex).Distinct().Count() != all.Count)
            Add(errors, path, "Retained counts and unique sample indices must agree.");
        foreach (var sample in all)
            if (sample.SourceIndex < 0 || sample.SourceIndex >= trace.SourceSampleCount || !double.IsFinite(sample.TimeSeconds)
                || !double.IsFinite(sample.RelativePowerMicrowatts) || sample.RelativeBaselineMicrowatts is double baseline && !double.IsFinite(baseline))
                Add(errors, path, "Each retained sample requires a valid source index and finite measurements.");
        foreach (var array in new[] { trace.Samples, trace.Endpoints })
            if (!array.SequenceEqual(array.OrderBy(sample => sample.TimeSeconds).ThenBy(sample => sample.SourceIndex)))
                Add(errors, path, "Samples must be chronologically ordered.");
        if (trace.Samples.GroupBy(sample => Math.Floor((sample.TimeSeconds - trace.AnchorTimeSeconds) / 15)).Any(bin => bin.Count() > 2))
            Add(errors, path, "A compression bin can contain at most two distinct extrema samples.");
    }

    static void ValidateHistoricalSnapshot(Dictionary<string, List<string>> errors, JsonElement value, Type type, string path)
    {
        if (value.ValueKind == JsonValueKind.Null) return; // unavailable historical value, never measured zero
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(string)) { if (value.ValueKind != JsonValueKind.String) Add(errors, path, "Expected historical text."); return; }
        if (type == typeof(bool)) { if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False) Add(errors, path, "Expected historical boolean."); return; }
        if (type.IsEnum) { if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var id) || !Enum.IsDefined(type, id)) Add(errors, path, "Invalid historical enum value."); return; }
        if (type.IsPrimitive || type == typeof(decimal))
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
                Add(errors, path, "Historical measurements must be finite numbers or null when unavailable.");
            return;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            if (value.ValueKind != JsonValueKind.Array) { Add(errors, path, "Expected historical collection."); return; }
            foreach (var item in value.EnumerateArray()) ValidateHistoricalSnapshot(errors, item, type.GetGenericArguments()[0], path);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) { Add(errors, path, "Expected historical snapshot object."); return; }
        var properties = type.GetProperties().ToDictionary(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name), StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!properties.TryGetValue(property.Name, out var expected)) Add(errors, path + "." + property.Name, "Unknown historical snapshot field.");
            else ValidateHistoricalSnapshot(errors, property.Value, expected.PropertyType, path + "." + property.Name);
    }

    static void ValidateStructuredPaths(Dictionary<string, List<string>> errors, JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                var key = property.Name.ToLowerInvariant();
                if ((key.Contains("path") && key != "containslocalpaths") || key.Contains("bootstrapreplicates") || key == "bootstrapsolutions")
                    Add(errors, path + "." + property.Name, "Structured local paths and full bootstrap replicate arrays are not accepted.");
                if (key == "sourcefilebasename" && property.Value.ValueKind == JsonValueKind.String
                    && (property.Value.GetString()!.Contains('/') || property.Value.GetString()!.Contains('\\')))
                    Add(errors, path + "." + property.Name, "Only a source basename is accepted.");
                ValidateStructuredPaths(errors, property.Value, path + "." + property.Name);
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ValidateStructuredPaths(errors, item, path);
    }

    static void ValidateDataBoundary(
        Dictionary<string, List<string>> errors,
        InterpretationDataBoundary? boundary)
    {
        if (boundary is null)
            return;

        MustBeFalse(errors, "package.dataBoundary.containsBootstrapReplicateArrays", boundary.ContainsBootstrapReplicateArrays);
        MustBeFalse(errors, "package.dataBoundary.containsLocalPaths", boundary.ContainsLocalPaths);
    }

    static void RequiredVersion(
        Dictionary<string, List<string>> errors,
        string path,
        string? value,
        string supportedValue)
    {
        if (string.IsNullOrWhiteSpace(value))
            Add(errors, path, "This field is required.");
        else if (!string.Equals(value, supportedValue, StringComparison.Ordinal))
            Add(errors, path, "The supplied value is not supported by this API version.");
    }

    static void Required(Dictionary<string, List<string>> errors, string path, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            Add(errors, path, "This field is required.");
    }

    static void MustBeFalse(Dictionary<string, List<string>> errors, string path, bool value)
    {
        if (value)
            Add(errors, path, "This data category is not accepted by the interpretation service.");
    }

    static bool IsLowercaseHexRequestId(string value) =>
        value.Length == 32 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    static void Add(Dictionary<string, List<string>> errors, string path, string message)
    {
        if (!errors.TryGetValue(path, out var messages))
        {
            messages = new List<string>();
            errors[path] = messages;
        }
        messages.Add(message);
    }

    static IReadOnlyDictionary<string, string[]> Freeze(Dictionary<string, List<string>> errors) =>
        errors.ToDictionary(item => item.Key, item => item.Value.ToArray(), StringComparer.Ordinal);

    static InterpretationRequestReadResult InvalidJson(string path) => Failed(
        StatusCodes.Status400BadRequest,
        "invalid_interpretation_json",
        "Invalid interpretation JSON",
        "The request body is malformed or does not match the interpretation JSON contract.",
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [path] = new[] { "Provide valid JSON using the documented field names and value types." },
        });

    static InterpretationRequestReadResult TooLarge() => Failed(
        StatusCodes.Status413PayloadTooLarge,
        "interpretation_request_too_large",
        "Interpretation request too large",
        "The interpretation request must be 2 MB or smaller.");

    static InterpretationRequestReadResult Failed(
        int statusCode,
        string code,
        string title,
        string detail,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        InterpretationRequestReadResult.Failed(
            new InterpretationRequestFailure(statusCode, code, title, detail, errors));

    static string JsonPath(string? path, string? prefix = null)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "$")
            return prefix ?? "$";

        var normalized = path.StartsWith("$.", StringComparison.Ordinal) ? path[2..] : path;
        return prefix is null ? normalized : prefix + "." + normalized;
    }

    static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            AllowDuplicateProperties = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 64,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    sealed class InterpretationRelayRequest
    {
        public string? RequestSchemaVersion { get; set; }
        public string? PromptProfileVersion { get; set; }
        public string? OutputFormatVersion { get; set; }
        public string? GenerationProfile { get; set; }
        public JsonElement Package { get; set; }
        public string? ClientRequestId { get; set; }
    }
}

public sealed record InterpretationRequestFailure(
    int StatusCode,
    string Code,
    string Title,
    string Detail,
    IReadOnlyDictionary<string, string[]>? Errors);

public sealed class InterpretationRequestReadResult
{
    InterpretationRequestReadResult(
        ValidatedInterpretationRequest? request,
        InterpretationRequestFailure? failure)
    {
        Request = request;
        Failure = failure;
    }

    public ValidatedInterpretationRequest? Request { get; }
    public InterpretationRequestFailure? Failure { get; }

    public static InterpretationRequestReadResult Valid(ValidatedInterpretationRequest request) => new(request, null);

    public static InterpretationRequestReadResult Failed(InterpretationRequestFailure failure) => new(null, failure);
}

public sealed record ValidatedInterpretationRequest(
    string ClientRequestId,
    string GenerationProfile,
    AnalysisInterpretationPackage Package);

sealed class InterpretationRequestTooLargeException : Exception;

sealed class SizeLimitedReadStream : Stream
{
    readonly Stream inner;
    readonly long maximumBytes;
    long bytesRead;

    public SizeLimitedReadStream(Stream inner, long maximumBytes)
    {
        this.inner = inner;
        this.maximumBytes = maximumBytes;
    }

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, AllowedLength(count));
        Count(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer[..AllowedLength(buffer.Length)]);
        Count(read);
        return read;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var read = await inner.ReadAsync(buffer, offset, AllowedLength(count), cancellationToken);
        Count(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer[..AllowedLength(buffer.Length)], cancellationToken);
        Count(read);
        return read;
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    int AllowedLength(int requestedLength)
    {
        var remainingWithSentinel = maximumBytes - bytesRead + 1;
        return (int)Math.Min(requestedLength, remainingWithSentinel);
    }

    void Count(int count)
    {
        bytesRead += count;
        if (bytesRead > maximumBytes)
            throw new InterpretationRequestTooLargeException();
    }
}
