using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AnalysisITC.Core.Application;

namespace AnalysisITC.Core.Interpretation
{
    public sealed class InterpretationServiceStatusResponse
    {
        public bool Available { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public string RequestSchemaVersion { get; set; }
        public string ResponseSchemaVersion { get; set; }
        public List<string> SupportedRequestSchemaVersions { get; set; } = new List<string>();
    }

    public enum AnalysisInterpretationFailureKind
    {
        Cancelled,
        Timeout,
        RateLimited,
        PayloadRejected,
        ServiceFailure,
        InvalidResponse,
        IncompatibleSchema,
        QuotaExceeded,
        AccessDenied,
        DuplicateRequest,
        AccountingUnavailable,
        AccountingUnresolved,
    }

    public sealed class AnalysisInterpretationProviderException : Exception
    {
        public AnalysisInterpretationFailureKind Kind { get; }
        public TimeSpan? RetryAfter { get; }

        public AnalysisInterpretationProviderException(
            AnalysisInterpretationFailureKind kind,
            string message,
            Exception innerException = null,
            TimeSpan? retryAfter = null)
            : base(message, innerException)
        {
            Kind = kind;
            RetryAfter = retryAfter;
        }
    }

    public sealed class FtItcInterpretationClient : IAnalysisInterpretationProvider
    {
        public const string RequestSchemaVersion = "ft-itc-relay-request-5.0";
        public const string ResponseSchemaVersion = "ft-itc-relay-response-5.0";
        public const string PreviousRequestSchemaVersion = "ft-itc-relay-request-4.0";
        public const string PreviousResponseSchemaVersion = "ft-itc-relay-response-4.0";
        public const string LegacyRequestSchemaVersion = "ft-itc-relay-request-3.0";
        public const string LegacyResponseSchemaVersion = "ft-itc-relay-response-3.0";

        public const int MaximumRequestBytes = 2 * 1024 * 1024;
        public const int MaximumResponseBytes = 2 * 1024 * 1024;

        readonly HttpClient httpClient;
        readonly Uri endpoint;
        readonly Uri operatorOptionsEndpoint;
        readonly Uri optionsEndpoint;
        readonly Uri accountEndpoint;
        readonly Uri statusEndpoint;
        static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        public FtItcInterpretationClient(HttpClient httpClient, Uri baseUri)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            if (baseUri == null || !baseUri.IsAbsoluteUri) throw new ArgumentException("An absolute relay base URI is required.", nameof(baseUri));
            endpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/api/interpretation/generate", UriKind.Absolute);
            operatorOptionsEndpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/api/interpretation/operator/options", UriKind.Absolute);
            optionsEndpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/api/interpretation/options", UriKind.Absolute);
            accountEndpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/api/interpretation/account", UriKind.Absolute);
            statusEndpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/api/interpretation/status", UriKind.Absolute);
        }

        public async Task<InterpretationServiceStatusResponse> GetInterpretationStatusAsync(CancellationToken cancellationToken = default)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            HttpResponseMessage response;
            try { response = await httpClient.GetAsync(statusEndpoint, timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.Timeout, "The interpretation service availability check timed out."); }
            using (response)
            {
            if (!response.IsSuccessStatusCode) throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure, "The interpretation service availability could not be checked.");
            try
            {
                var value = JsonSerializer.Deserialize<InterpretationServiceStatusResponse>(await response.Content.ReadAsStringAsync().ConfigureAwait(false), JsonOptions);
                if (value == null) throw new JsonException();
                if (string.IsNullOrWhiteSpace(value.Status)) value.Status = value.Available ? "available" : "temporarily_unavailable";
                if (value.Status is not ("available" or "temporarily_unavailable" or "retired")) throw new JsonException();
                return value;
            }
            catch (JsonException ex) { throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse, "The interpretation service availability response was invalid.", ex); }
            }
        }

        public async Task<InterpretationOperatorOptionsResponse> GetInterpretationOptionsAsync(string accessCode, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                new Uri(optionsEndpoint + "?requestSchemaVersion=" + Uri.EscapeDataString(RequestSchemaVersion)));
            if (!string.IsNullOrWhiteSpace(accessCode)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessCode);
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Forbidden) throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.AccessDenied, "The access code is invalid, expired, or revoked.");
            if (!response.IsSuccessStatusCode) throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure, "The interpretation service could not load access options.");
            try { return JsonSerializer.Deserialize<InterpretationOperatorOptionsResponse>(await response.Content.ReadAsStringAsync().ConfigureAwait(false), JsonOptions) ?? throw new JsonException(); }
            catch (JsonException ex) { throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse, "The interpretation options response was invalid.", ex); }
        }

        public async Task<InterpretationAccountResponse> GetInterpretationAccountAsync(string accessCode, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, accountEndpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessCode ?? "");
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.AccessDenied, "The access code is invalid, expired, or revoked.");
            if (!response.IsSuccessStatusCode)
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure, "The interpretation service could not load account details.");
            try { return JsonSerializer.Deserialize<InterpretationAccountResponse>(await response.Content.ReadAsStringAsync().ConfigureAwait(false), JsonOptions) ?? throw new JsonException(); }
            catch (JsonException ex) { throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse, "The interpretation account response was invalid.", ex); }
        }

        public async Task<InterpretationOperatorOptionsResponse> GetOperatorOptionsAsync(string operatorCode, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, operatorOptionsEndpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", operatorCode ?? "");
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.AccessDenied, "The operator code is invalid, expired, or revoked.");
            if (!response.IsSuccessStatusCode)
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure, "The interpretation service could not verify operator access.");
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            try { return JsonSerializer.Deserialize<InterpretationOperatorOptionsResponse>(json, JsonOptions) ?? throw new JsonException(); }
            catch (JsonException ex) { throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse, "The operator options response was invalid.", ex); }
        }

        public async Task<AnalysisInterpretationProviderResponse> GenerateAsync(
            AnalysisInterpretationGenerationRequest request,
            CancellationToken cancellationToken)
        {
            if (request?.Package == null || request.Prompt == null) throw new ArgumentNullException(nameof(request));
            var evaluation = string.IsNullOrWhiteSpace(request.OperatorCode)
                && !string.IsNullOrWhiteSpace(AppSettings.InterpretationOperatorCode);
            var bearer = request.OperatorCode;
            var selectedModel = request.RequestedModel;
            var selectedReasoning = request.RequestedReasoningEffort;
            var selectedGuidance = request.RequestedGuidanceVariant;
            var generationProfile = string.IsNullOrWhiteSpace(request.GenerationProfile) ? "instant" : request.GenerationProfile;
            var taskType = string.Equals(request.TaskType, "summary", StringComparison.Ordinal) ? "summary" : "interpretation";
            InterpretationOperatorOptionsResponse currentOptions;
            var accessCode = evaluation ? AppSettings.InterpretationOperatorCode ?? "" : request.OperatorCode ?? "";
            try { currentOptions = await GetInterpretationOptionsAsync(accessCode, cancellationToken).ConfigureAwait(false); }
            catch (AnalysisInterpretationProviderException ex) when (evaluation && ex.Kind == AnalysisInterpretationFailureKind.AccessDenied)
            {
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.AccessDenied,
                    "Interpretation access is invalid, expired, or revoked. Verify or replace the code in Preferences, or remove the code to use the default setting.", ex);
            }
            var maximumRequestBytes = currentOptions.MaximumRequestBytes is > 0 and <= MaximumRequestBytes
                ? currentOptions.MaximumRequestBytes : MaximumRequestBytes;
            if (evaluation)
            {
                var verificationCode = AppSettings.InterpretationOperatorCode ?? "";
                bearer = verificationCode;
                var options = currentOptions;
                if (options.Mode == "custom")
                {
                    var summarySelected = taskType == "summary"
                        || string.Equals(request.RequestedModel ?? AppSettings.InterpretationEvaluationModel, "summary", StringComparison.Ordinal);
                    if (summarySelected)
                    {
                        taskType = "summary"; generationProfile = "summary";
                        selectedModel = null; selectedReasoning = null;
                    }
                    else
                    {
                        generationProfile = "custom";
                        selectedModel = request.RequestedModel ?? AppSettings.InterpretationEvaluationModel;
                        selectedReasoning = request.RequestedReasoningEffort ?? AppSettings.InterpretationEvaluationReasoningEffort;
                        selectedGuidance = string.IsNullOrWhiteSpace(request.RequestedGuidanceVariant)
                            ? AppSettings.InterpretationEvaluationGuidanceVariant : request.RequestedGuidanceVariant;
                        var selected = options.Models.FirstOrDefault(model => string.Equals(model.Id, selectedModel, StringComparison.Ordinal));
                        if (selected == null || !selected.ReasoningEfforts.Contains(selectedReasoning)) throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.PayloadRejected, "The saved model and reasoning combination is no longer available. Verify access again in Preferences.");
                        if (string.IsNullOrWhiteSpace(selectedGuidance)) selectedGuidance = options.DefaultGuidanceVariant;
                        if (!options.GuidanceVariants.Any(item => string.Equals(item.Id, selectedGuidance, StringComparison.Ordinal)))
                            throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.PayloadRejected,
                                "The saved scientific-guidance option is no longer available. Verify access again in Preferences.");
                    }
                }
                else
                {
                    generationProfile = request.RequestedPreset ?? AppSettings.InterpretationGenerationPreset ?? "instant";
                    taskType = generationProfile == "summary" ? "summary" : "interpretation";
                    selectedModel = null; selectedReasoning = null;
                    if (!options.Presets.Any(preset => preset.Id == generationProfile)) throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.PayloadRejected, "The saved interpretation depth is not available with this access level. Choose another setting in Preferences.");
                }
            }
            else if (string.IsNullOrWhiteSpace(request.OperatorCode))
            {
                generationProfile = taskType == "summary" ? "summary" : "instant";
                selectedModel = null; selectedReasoning = null;
            }
            var modelPackageJson = request.Prompt.ModelPackageJson
                ?? AnalysisInterpretationModelInputWriter.Write(request.Prompt.CanonicalPackageJson);
            var relay = new RelayRequest
            {
                RequestSchemaVersion = RequestSchemaVersion,
                TaskType = taskType,
                OutputInstructions = request.Prompt.ResponseFormatInstructions,
                OutputFormatVersion = request.Prompt.OutputFormatVersion,
                GenerationProfile = generationProfile,
                Package = ParsePackage(modelPackageJson),
                ClientRequestId = request.ClientRequestId,
            };
            var body = JsonSerializer.Serialize(relay, JsonOptions);
            if (Encoding.UTF8.GetByteCount(body) > maximumRequestBytes)
            {
                // Start from a structurally cloned canonical document so unknown historical
                // fields survive the transport fallback. The caller's full package remains untouched.
                modelPackageJson = AnalysisInterpretationModelInputWriter.Write(OmitThermograms(
                    request.Prompt.CanonicalPackageJson,
                    $"All thermograms and sampled baselines omitted to meet the {FormatBytes(maximumRequestBytes)} access-level request limit."));
                relay.Package = ParsePackage(modelPackageJson);
                body = JsonSerializer.Serialize(relay, JsonOptions);
                var bodyBytes = Encoding.UTF8.GetByteCount(body);
                if (bodyBytes > maximumRequestBytes)
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.PayloadRejected,
                        $"The complete report evidence is {FormatBytes(bodyBytes)} and exceeds the {FormatBytes(maximumRequestBytes)} allowance for this access level, even without thermograms. Shorten background/context or create a smaller report selection.");
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            AnalysisInterpretationLog.Write("relay-send", request.ClientRequestId,
                $"host={AnalysisInterpretationLog.Token(endpoint.Host)} bytes={Encoding.UTF8.GetByteCount(body)} omissions={ReadOmissions(relay.Package).Count}");
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (!string.IsNullOrWhiteSpace(bearer))
                message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            if (!string.IsNullOrWhiteSpace(selectedModel))
                message.Headers.TryAddWithoutValidation("X-FTITC-Model", selectedModel);
            if (!string.IsNullOrWhiteSpace(selectedReasoning))
                message.Headers.TryAddWithoutValidation("X-FTITC-Reasoning-Effort", selectedReasoning);
            if (taskType == "interpretation" && !string.IsNullOrWhiteSpace(selectedGuidance))
                message.Headers.TryAddWithoutValidation("X-FTITC-Guidance-Variant", selectedGuidance);
            HttpResponseMessage response;
            using var timeoutCancellation = CreateTimeoutCancellation(httpClient.Timeout);
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
            var requestToken = requestCancellation.Token;
            try
            {
                response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                throw new AnalysisInterpretationProviderException(
                    CancellationKind(cancellationToken, timeoutCancellation.Token),
                    CancellationMessage(CancellationKind(cancellationToken, timeoutCancellation.Token)), ex);
            }
            catch (HttpRequestException ex)
            {
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure, "The interpretation service could not be reached.", ex);
            }

            using (response)
            {
                string content;
                try
                {
                    content = await ReadResponseContentAsync(response.Content, requestToken).ConfigureAwait(false);
                }
                catch (AnalysisInterpretationProviderException) { throw; }
                catch (OperationCanceledException ex)
                {
                    throw new AnalysisInterpretationProviderException(
                        CancellationKind(cancellationToken, timeoutCancellation.Token),
                        CancellationMessage(CancellationKind(cancellationToken, timeoutCancellation.Token)), ex);
                }
                catch (Exception ex) when (requestToken.IsCancellationRequested
                    && (ex is ObjectDisposedException || ex is System.IO.IOException || ex is HttpRequestException))
                {
                    throw new AnalysisInterpretationProviderException(
                        CancellationKind(cancellationToken, timeoutCancellation.Token),
                        CancellationMessage(CancellationKind(cancellationToken, timeoutCancellation.Token)), ex);
                }
                catch (System.IO.IOException ex)
                {
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure,
                        "The interpretation service response could not be read.", ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure,
                        "The interpretation service response could not be read.", ex);
                }
                string problemCode = null;
                string rejectedFields = "none";
                int? problemMaximumRequestBytes = null;
                int? requestBytes = null;
                if (!response.IsSuccessStatusCode)
                {
                    try
                    {
                        using var problem = JsonDocument.Parse(content);
                        if (problem.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String) problemCode = code.GetString();
                        if (problem.RootElement.TryGetProperty("maximumRequestBytes", out var maximum) && maximum.TryGetInt32(out var maximumValue)) problemMaximumRequestBytes = maximumValue;
                        if (problem.RootElement.TryGetProperty("requestBytes", out var actual) && actual.TryGetInt32(out var actualValue)) requestBytes = actualValue;
                        if (problem.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                            rejectedFields = string.Join(",", errors.EnumerateObject().Take(8).Select(field => AnalysisInterpretationLog.Token(field.Name)));
                    }
                    catch (JsonException) { }
                }
                AnalysisInterpretationLog.Write("relay-response", request.ClientRequestId,
                    $"http={(int)response.StatusCode} code={AnalysisInterpretationLog.Token(problemCode)} fields={rejectedFields} characters={content.Length} elapsedMs={timer.ElapsedMilliseconds}");
                if (problemCode == "interpretation_provider_quota")
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.QuotaExceeded,
                        "The model provider quota or billing limit has been reached. The interpretation service administrator must check the API account.");
                if (response.StatusCode == HttpStatusCode.Forbidden && problemCode == "operator_access_denied")
                {
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.AccessDenied,
                        "Interpretation access is invalid, expired, or revoked. Verify or replace the code in Preferences, or remove the code to use the default setting.");
                }
                if (response.StatusCode == HttpStatusCode.Forbidden && problemCode == "generation_preset_denied")
                {
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.AccessDenied,
                        "The selected interpretation depth is not available for this access level. Verify access again and choose one of the available depths.");
                }
                if (response.StatusCode == (HttpStatusCode)429 && problemCode == "interpretation_quota_exhausted")
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.QuotaExceeded,
                        "The monthly allowance for this interpretation depth has been used. Preferences shows when it resets.");
                if (response.StatusCode == (HttpStatusCode)429 && problemCode == "interpretation_quota_busy")
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.RateLimited,
                        "Another quota-limited interpretation is already running with this access code. Try again when it has completed.");
                if (response.StatusCode == HttpStatusCode.Conflict && problemCode == "interpretation_duplicate_request")
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.DuplicateRequest,
                        "This generation request has already been submitted. Check whether an interpretation was returned before starting another generation.");
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable && problemCode == "interpretation_accounting_unresolved")
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.AccountingUnresolved,
                        "Interpretation generation is temporarily unavailable because usage accounting needs reconciliation. Try again later.");
                if (response.StatusCode == HttpStatusCode.ServiceUnavailable && problemCode == "interpretation_accounting_unavailable")
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.AccountingUnavailable,
                        "Interpretation generation is temporarily unavailable because usage accounting is unavailable. Try again later.");
                if (response.StatusCode == HttpStatusCode.GatewayTimeout)
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.Timeout, "The model service timed out before returning an interpretation.");
                if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge && problemCode == "interpretation_tier_size_exceeded")
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.PayloadRejected,
                        $"The interpretation request{(requestBytes is int actual ? $" is {FormatBytes(actual)}" : "")} exceeds the {(problemMaximumRequestBytes is int maximum ? FormatBytes(maximum) : "configured")} allowance for this access level. Shorten background/context or create a smaller report selection.");
                if (response.StatusCode == (HttpStatusCode)429)
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.RateLimited,
                        "The interpretation service rate limit was reached.", retryAfter: RetryAfter(response));
                if ((response.StatusCode == HttpStatusCode.BadRequest || (int)response.StatusCode == 422)
                    && (content.Contains("requestSchemaVersion") || content.Contains("promptProfileVersion") || content.Contains("packageSchemaVersion")))
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.IncompatibleSchema,
                        "This interpretation service does not support the report-wide AI contract. Check for application or service updates before generating. Your approved interpretation is retained.");
                if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge
                    || response.StatusCode == HttpStatusCode.BadRequest
                    || (int)response.StatusCode == 422
                    || response.StatusCode == HttpStatusCode.UnsupportedMediaType)
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.PayloadRejected,
                        response.StatusCode == HttpStatusCode.RequestEntityTooLarge
                            ? "The report evidence exceeds the service size or model context limit. Shorten background or create a smaller report selection."
                            : $"The interpretation service rejected the request payload (HTTP {(int)response.StatusCode}; code {AnalysisInterpretationLog.Token(problemCode)}; fields {rejectedFields}). Request {AnalysisInterpretationLog.Token(request.ClientRequestId)}. These diagnostics are in the application log.");
                if (!response.IsSuccessStatusCode)
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure,
                        "The interpretation service returned HTTP " + (int)response.StatusCode + ".");

                RelayResponse relayResponse;
                try { relayResponse = JsonSerializer.Deserialize<RelayResponse>(content, JsonOptions); }
                catch (JsonException ex)
                {
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse,
                        "The interpretation service returned invalid JSON.", ex);
                }
                if (relayResponse == null || string.IsNullOrWhiteSpace(relayResponse.InterpretationMarkdown))
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse,
                        "The interpretation service response did not contain interpretation Markdown.");
                if (!string.Equals(relayResponse.ResponseSchemaVersion, ResponseSchemaVersion, StringComparison.Ordinal))
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.IncompatibleSchema,
                        "Unsupported interpretation relay response schema: " + (relayResponse.ResponseSchemaVersion ?? "<missing>"));
                if (!string.Equals(relayResponse.RequestId, request.ClientRequestId, StringComparison.Ordinal))
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse,
                        "The interpretation response request ID does not match the request.");
                if (string.IsNullOrWhiteSpace(relayResponse.Provider)
                    || string.IsNullOrWhiteSpace(relayResponse.Model)
                    || string.IsNullOrWhiteSpace(relayResponse.EffectivePreset)
                    || string.IsNullOrWhiteSpace(relayResponse.PresetRevision)
                    || string.IsNullOrWhiteSpace(relayResponse.TaskType)
                    || relayResponse.GeneratedAtUtc == default(DateTime))
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse,
                        "The interpretation response is missing provider, model, or generation provenance.");
                if (relayResponse.EffectiveInputFingerprint == null || relayResponse.EffectiveInputFingerprint.Length != 64
                    || relayResponse.EffectiveInputFingerprint.Any(character => !Uri.IsHexDigit(character))
                    || relayResponse.Omissions == null || relayResponse.KnowledgeBaseIds == null || relayResponse.RetrievedSourceIds == null
                    || string.IsNullOrWhiteSpace(relayResponse.ScientificGuidanceRevision)
                    || !IsSha256(relayResponse.ScientificInstructionsFingerprint)
                    || !IsSha256(relayResponse.OutputInstructionsFingerprint))
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse,
                        "The version 3 interpretation response is missing valid effective-input provenance.");
                return new AnalysisInterpretationProviderResponse
                {
                    RequestId = relayResponse.RequestId,
                    Provider = relayResponse.Provider,
                    Model = relayResponse.Model,
                    ReasoningEffort = relayResponse.ReasoningEffort,
                    EffectivePreset = relayResponse.EffectivePreset,
                    TaskType = relayResponse.TaskType,
                    PresetRevision = relayResponse.PresetRevision,
                    GeneratedAtUtc = relayResponse.GeneratedAtUtc,
                    InterpretationMarkdown = relayResponse.InterpretationMarkdown,
                    EffectiveInputFingerprint = relayResponse.EffectiveInputFingerprint,
                    Omissions = ReadOmissions(relay.Package).Concat(relayResponse.Omissions ?? new List<string>()).Distinct().ToList(), KnowledgeBaseIds = relayResponse.KnowledgeBaseIds,
                    RetrievedSourceIds = relayResponse.RetrievedSourceIds,
                    ScientificGuidanceRevision = relayResponse.ScientificGuidanceRevision,
                    ScientificGuidanceVariant = relayResponse.ScientificGuidanceVariant,
                    ScientificInstructionsFingerprint = relayResponse.ScientificInstructionsFingerprint,
                    OutputInstructionsFingerprint = relayResponse.OutputInstructionsFingerprint,
                };
            }
        }

        static CancellationTokenSource CreateTimeoutCancellation(TimeSpan timeout)
        {
            var source = new CancellationTokenSource();
            if (timeout != Timeout.InfiniteTimeSpan && timeout > TimeSpan.Zero)
                source.CancelAfter(timeout);
            return source;
        }

        static AnalysisInterpretationFailureKind CancellationKind(CancellationToken caller, CancellationToken timeout) =>
            caller.IsCancellationRequested ? AnalysisInterpretationFailureKind.Cancelled : AnalysisInterpretationFailureKind.Timeout;

        static string CancellationMessage(AnalysisInterpretationFailureKind kind) =>
            kind == AnalysisInterpretationFailureKind.Cancelled ? "Interpretation generation was cancelled." : "The interpretation service timed out.";

        static async Task<string> ReadResponseContentAsync(HttpContent content, CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength > MaximumResponseBytes)
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse,
                    "The interpretation service response exceeded the size limit.");
            // ReadAsStreamAsync has no cancellation-token overload on netstandard2.0.
            // Dispose the content when cancellation occurs so an in-flight buffering or
            // network operation is aborted before this method returns.
            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try { content.Dispose(); } catch (Exception) { }
            });
            using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
            using var buffer = new System.IO.MemoryStream();
            var chunk = new byte[81920];
            while (true)
            {
                var read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (buffer.Length > MaximumResponseBytes - read)
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse,
                        "The interpretation service response exceeded the size limit.");
                buffer.Write(chunk, 0, read);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        static TimeSpan? RetryAfter(HttpResponseMessage response)
        {
            var value = response.Headers.RetryAfter;
            if (value?.Delta != null) return value.Delta;
            if (value?.Date != null)
            {
                var delay = value.Date.Value - DateTimeOffset.UtcNow;
                return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
            }
            return null;
        }

        static bool IsSha256(string value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);

        static string FormatBytes(int bytes) => bytes % (1024 * 1024) == 0
            ? $"{bytes / (1024 * 1024)} MiB"
            : bytes % 1024 == 0 ? $"{bytes / 1024} KiB" : $"{bytes} bytes";

        static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false));
            return options;
        }

        static JsonElement ParsePackage(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        static List<string> ReadOmissions(JsonElement package)
        {
            if (package.ValueKind != JsonValueKind.Object || !package.TryGetProperty("omissions", out var omissions)
                || omissions.ValueKind != JsonValueKind.Array) return new List<string>();
            return omissions.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()).ToList();
        }

        static string OmitThermograms(string canonicalJson, string omission)
        {
            var root = JsonNode.Parse(canonicalJson) as JsonObject
                ?? throw new JsonException("Canonical evidence must be a JSON object.");
            var experiments = (root["supportingExperiments"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            foreach (var result in (root["results"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                experiments.AddRange((result["experiments"] as JsonArray ?? new JsonArray()).OfType<JsonObject>());
            // Shared compact payloads keep the thermogram in a root-level source
            // record.  Preserve the same fallback semantics for that layout while
            // leaving unrelated package properties untouched.
            experiments.AddRange((root["experimentEvidence"] as JsonArray ?? new JsonArray()).OfType<JsonObject>());
            var hadTraces = experiments.Any(experiment => experiment["thermogram"] != null);
            foreach (var experiment in experiments) experiment.Remove("thermogram");
            if (hadTraces)
            {
                var omissions = root["omissions"] as JsonArray;
                if (omissions == null) root["omissions"] = omissions = new JsonArray();
                if (!omissions.OfType<JsonValue>().Any(item => item.TryGetValue<string>(out var value)
                    && string.Equals(value, omission, StringComparison.Ordinal))) omissions.Add(omission);
                if (root["dataBoundary"] is not JsonObject boundary)
                    root["dataBoundary"] = boundary = new JsonObject();
                boundary["containsRawThermogramSamples"] = false;
                boundary["containsBaselineArrays"] = false;
                boundary["modelObservationRestriction"] = "No raw thermogram or sampled fitted-baseline arrays were supplied. Assess available summaries, controls and injection evidence only; do not claim to observe peak shape or settling.";
            }
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }

        sealed class RelayRequest
        {
            public string RequestSchemaVersion { get; set; }
            public string TaskType { get; set; }
            public string OutputInstructions { get; set; }
            public string OutputFormatVersion { get; set; }
            public string GenerationProfile { get; set; }
            public JsonElement Package { get; set; }
            public string ClientRequestId { get; set; }
        }

        sealed class RelayResponse
        {
            public string ResponseSchemaVersion { get; set; }
            public string TaskType { get; set; }
            public string RequestId { get; set; }
            public string Provider { get; set; }
            public string Model { get; set; }
            public string ReasoningEffort { get; set; }
            public string EffectivePreset { get; set; }
            public string PresetRevision { get; set; }
            public DateTime GeneratedAtUtc { get; set; }
            public string InterpretationMarkdown { get; set; }
            public string EffectiveInputFingerprint { get; set; }
            public string ScientificGuidanceRevision { get; set; }
            public string ScientificGuidanceVariant { get; set; }
            public string ScientificInstructionsFingerprint { get; set; }
            public string OutputInstructionsFingerprint { get; set; }
            public string OutputFormatVersion { get; set; }
            public List<string> Omissions { get; set; }
            public List<string> KnowledgeBaseIds { get; set; }
            public List<string> RetrievedSourceIds { get; set; }
        }
    }

    public sealed class InterpretationOperatorOptionsResponse
    {
        public InterpretationAccessDetails AccessDetails { get; set; }
        public string AccessTier { get; set; }
        public string AccessTierName { get; set; }
        public string Mode { get; set; }
        public string PresetRevision { get; set; }
        public string DefaultModel { get; set; }
        public string DefaultReasoningEffort { get; set; }
        public int MaximumRequestBytes { get; set; }
        public List<InterpretationPresetOption> Presets { get; set; } = new List<InterpretationPresetOption>();
        public List<InterpretationOperatorModelOption> Models { get; set; } = new List<InterpretationOperatorModelOption>();
        public List<InterpretationGuidanceVariantOption> GuidanceVariants { get; set; } = new List<InterpretationGuidanceVariantOption>();
        public string DefaultGuidanceVariant { get; set; }
    }

    public sealed class InterpretationAccountResponse
    {
        public string Status { get; set; }
        public string Label { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public string AccessTier { get; set; }
        public string AccessTierName { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public int MaximumRequestBytes { get; set; }
        public InterpretationAccountUsage Usage { get; set; }
        public int? TotalRequests { get; set; }
        public InterpretationMostRecentRequest MostRecentRequest { get; set; }
    }

    public sealed class InterpretationAccountUsage
    {
        public bool Limited { get; set; }
        public int? RemainingPercent { get; set; }
        public decimal? SpentUsd { get; set; }
        public decimal? LimitUsd { get; set; }
        public DateTime? ResetsAtUtc { get; set; }
    }

    public sealed class InterpretationMostRecentRequest
    {
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string Outcome { get; set; }
        public int? HttpStatus { get; set; }
    }

    public sealed class InterpretationAccessDetails
    {
        public string Name { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
    }

    public sealed class InterpretationPresetOption
    {
        public string Id { get; set; }
        public string Description { get; set; }
        string serverName;
        public string Name
        {
            get => CanonicalName(Id) ?? serverName;
            set => serverName = value;
        }
        public string TaskType { get; set; } = "interpretation";
        public InterpretationPresetQuota Quota { get; set; }
        public override string ToString() => Name ?? Id ?? "";

        static string CanonicalName(string id) => id?.ToLowerInvariant() switch
        {
            "instant" => "Fast",
            "fast" => "Default",
            "standard" => "Advanced",
            "in-depth" => "Comprehensive",
            _ => null
        };
    }

    public sealed class InterpretationPresetQuota
    {
        public bool Limited { get; set; }
        public int RemainingPercent { get; set; }
        public DateTime ResetsAtUtc { get; set; }
    }

    public sealed class InterpretationOperatorModelOption
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string SelectionType { get; set; } = "model";
        public List<string> ReasoningEfforts { get; set; } = new List<string>();
    }

    public sealed class InterpretationGuidanceVariantOption
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Revision { get; set; }
        public override string ToString() => DisplayName ?? Id ?? "";
    }

    public static class InterpretationAccessDisplay
    {
        public static string GenerationOptionDescription(
            InterpretationOperatorOptionsResponse options,
            string presetId,
            string modelId)
        {
            if (options == null) return null;
            var description = options.Mode == "custom"
                ? options.Models?.FirstOrDefault(item => item.Id == modelId)?.Description
                : options.Presets?.FirstOrDefault(item => item.Id == presetId)?.Description;
            return string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        }

        /// <summary>
        /// Returns whether the locally verified capability permits the optional
        /// compressed thermogram input. Standard and public access deliberately
        /// do not expose this relatively large, experimental evidence channel.
        /// </summary>
        public static bool CanIncludeThermograms()
        {
            if (string.IsNullOrWhiteSpace(AppSettings.InterpretationOperatorCode)
                || !AppSettings.TryGetInterpretationAccessOptions(AppSettings.InterpretationOperatorCode, out var options))
                return false;
            return CanIncludeThermograms(options);
        }

        public static bool CanIncludeThermograms(InterpretationOperatorOptionsResponse options)
        {
            return options != null && (string.Equals(options.AccessTier, "advanced", StringComparison.OrdinalIgnoreCase)
                || string.Equals(options.AccessTier, "administrator", StringComparison.OrdinalIgnoreCase));
        }

        public static bool CanIncludeInjectionTables(InterpretationOperatorOptionsResponse options) =>
            options != null && !string.Equals(options.AccessTier, "public", StringComparison.OrdinalIgnoreCase);

        public static bool CanIncludeProcessingInformation(InterpretationOperatorOptionsResponse options) =>
            options != null && (string.Equals(options.AccessTier, "advanced", StringComparison.OrdinalIgnoreCase)
                || string.Equals(options.AccessTier, "administrator", StringComparison.OrdinalIgnoreCase));

        public static string CurrentSetting()
        {
            if (string.IsNullOrWhiteSpace(AppSettings.InterpretationOperatorCode))
                return "Selected interpretation: Default";
            if (!AppSettings.TryGetInterpretationAccessOptions(AppSettings.InterpretationOperatorCode, out var options))
                return "Selected interpretation: unavailable (verify access in Preferences)";
            if (options.Mode == "custom")
            {
                var model = string.IsNullOrWhiteSpace(AppSettings.InterpretationEvaluationModel)
                    ? options.DefaultModel ?? "model unavailable"
                    : AppSettings.InterpretationEvaluationModel;
                var reasoning = string.IsNullOrWhiteSpace(AppSettings.InterpretationEvaluationReasoningEffort)
                    ? options.DefaultReasoningEffort ?? "reasoning unavailable"
                    : AppSettings.InterpretationEvaluationReasoningEffort;
                var guidance = options.GuidanceVariants.FirstOrDefault(item => item.Id == AppSettings.InterpretationEvaluationGuidanceVariant)
                    ?? options.GuidanceVariants.FirstOrDefault(item => item.Id == options.DefaultGuidanceVariant);
                var guidanceText = guidance == null ? "" : $" · {guidance.DisplayName} guidance";
                return $"Selected interpretation: {model} model · {reasoning} reasoning{guidanceText}";
            }
            var preset = options.Presets.FirstOrDefault(x => x.Id == AppSettings.InterpretationGenerationPreset);
            var presetName = preset?.Name ?? AppSettings.InterpretationGenerationPreset;
            return "Selected interpretation: " + (string.IsNullOrWhiteSpace(presetName) ? "Default" : presetName + " preset");
        }

        /// <summary>Compact account identity, tier and quota text for generation views.</summary>
        public static string AccountSummary(InterpretationAccountResponse account, InterpretationOperatorOptionsResponse options = null)
        {
            var identity = account?.Label ?? account?.Name;
            if (string.IsNullOrWhiteSpace(identity)) identity = options?.AccessDetails?.Name;
            if (string.IsNullOrWhiteSpace(identity)) identity = "Account";
            var tier = account?.AccessTierName ?? account?.AccessTier ?? options?.AccessTierName ?? options?.AccessTier;
            if (string.IsNullOrWhiteSpace(tier)) tier = "Not available";
            var usage = account?.Usage;
            string remaining;
            if (usage != null && !usage.Limited) remaining = "Unlimited";
            else if (usage?.RemainingPercent is int percent) remaining = percent + "%";
            else
            {
                var preset = options?.Presets?.FirstOrDefault(item => item.Id == AppSettings.InterpretationGenerationPreset);
                remaining = preset?.Quota?.Limited == true ? preset.Quota.RemainingPercent + "%" : "Not available";
            }
            return $"Account: {identity} · {tier} · Usage left: {remaining}";
        }
    }
}
