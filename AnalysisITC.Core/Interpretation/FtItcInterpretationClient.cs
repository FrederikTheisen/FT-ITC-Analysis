using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AnalysisITC.Core.Application;

namespace AnalysisITC.Core.Interpretation
{
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
        public const string RequestSchemaVersion = "ft-itc-relay-request-3.0";
        public const string ResponseSchemaVersion = "ft-itc-relay-response-3.0";

        public const int MaximumRequestBytes = 2 * 1024 * 1024;
        public const int MaximumResponseBytes = 2 * 1024 * 1024;

        readonly HttpClient httpClient;
        readonly Uri endpoint;
        readonly Uri operatorOptionsEndpoint;
        static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        public FtItcInterpretationClient(HttpClient httpClient, Uri baseUri)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            if (baseUri == null || !baseUri.IsAbsoluteUri) throw new ArgumentException("An absolute relay base URI is required.", nameof(baseUri));
            endpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/api/interpretation/generate", UriKind.Absolute);
            operatorOptionsEndpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/api/interpretation/operator/options", UriKind.Absolute);
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
            if (AppSettings.UseInterpretationEvaluationSettings && string.IsNullOrWhiteSpace(request.OperatorCode))
            {
                var verificationCode = AppSettings.InterpretationOperatorCode ?? "";
                try
                {
                    var options = await GetOperatorOptionsAsync(verificationCode, cancellationToken).ConfigureAwait(false);
                    var selected = options.Models.FirstOrDefault(model => string.Equals(model.Id, AppSettings.InterpretationEvaluationModel, StringComparison.Ordinal));
                    if (selected == null || !selected.ReasoningEfforts.Contains(AppSettings.InterpretationEvaluationReasoningEffort))
                        throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.PayloadRejected,
                            "The saved evaluation model and reasoning combination is no longer available. Verify access again in Preferences.");
                }
                catch (AnalysisInterpretationProviderException ex) when (ex.Kind == AnalysisInterpretationFailureKind.AccessDenied)
                {
                    if (string.Equals(AppSettings.InterpretationOperatorCode, verificationCode, StringComparison.Ordinal))
                    {
                        AppSettings.ClearInterpretationAccessVerification();
                        AppSettings.UseInterpretationEvaluationSettings = false;
                        AppSettings.Save();
                    }
                    throw;
                }
            }
            var relay = new RelayRequest
            {
                RequestSchemaVersion = RequestSchemaVersion,
                OutputInstructions = request.Prompt.ResponseFormatInstructions,
                OutputFormatVersion = request.Prompt.OutputFormatVersion,
                GenerationProfile = string.IsNullOrWhiteSpace(request.GenerationProfile) ? "fast" : request.GenerationProfile,
                Package = AnalysisInterpretationThermograms.Copy(request.Package),
                ClientRequestId = request.ClientRequestId,
            };
            var body = JsonSerializer.Serialize(relay, JsonOptions);
            if (Encoding.UTF8.GetByteCount(body) > MaximumRequestBytes)
            {
                AnalysisInterpretationThermograms.OmitTraces(relay.Package, "All thermograms and sampled baselines omitted to meet the 2 MiB transport limit.");
                body = JsonSerializer.Serialize(relay, JsonOptions);
                if (Encoding.UTF8.GetByteCount(body) > MaximumRequestBytes)
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.PayloadRejected,
                        "The complete report evidence still exceeds 2 MiB without thermograms. Shorten background/context or create a smaller report selection.");
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            AnalysisInterpretationLog.Write("relay-send", request.ClientRequestId,
                $"host={AnalysisInterpretationLog.Token(endpoint.Host)} bytes={Encoding.UTF8.GetByteCount(body)} omissions={relay.Package.Omissions.Count}");
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            var evaluation = AppSettings.UseInterpretationEvaluationSettings;
            var bearer = request.OperatorCode ?? (evaluation ? AppSettings.InterpretationOperatorCode : null);
            var selectedModel = request.RequestedModel ?? (evaluation ? AppSettings.InterpretationEvaluationModel : null);
            var selectedReasoning = request.RequestedReasoningEffort ?? (evaluation ? AppSettings.InterpretationEvaluationReasoningEffort : null);
            if (!string.IsNullOrWhiteSpace(bearer))
                message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            if (!string.IsNullOrWhiteSpace(selectedModel))
                message.Headers.TryAddWithoutValidation("X-FTITC-Model", selectedModel);
            if (!string.IsNullOrWhiteSpace(selectedReasoning))
                message.Headers.TryAddWithoutValidation("X-FTITC-Reasoning-Effort", selectedReasoning);
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
                if (!response.IsSuccessStatusCode)
                {
                    try
                    {
                        using var problem = JsonDocument.Parse(content);
                        if (problem.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String) problemCode = code.GetString();
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
                    var verificationCode = AppSettings.InterpretationOperatorCode ?? "";
                    if (!string.IsNullOrWhiteSpace(bearer) && string.Equals(bearer, verificationCode, StringComparison.Ordinal))
                    {
                        AppSettings.ClearInterpretationAccessVerification();
                        AppSettings.UseInterpretationEvaluationSettings = false;
                        AppSettings.Save();
                    }
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.AccessDenied,
                        "Evaluation access is invalid, expired, or revoked. Evaluation settings were disabled; you can retry with the public defaults.");
                }
                if (response.StatusCode == HttpStatusCode.GatewayTimeout)
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.Timeout, "The model service timed out before returning an interpretation.");
                if (response.StatusCode == (HttpStatusCode)429)
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.RateLimited,
                        "The interpretation service rate limit was reached.", retryAfter: RetryAfter(response));
                if ((response.StatusCode == HttpStatusCode.BadRequest || (int)response.StatusCode == 422)
                    && (content.Contains("requestSchemaVersion") || content.Contains("promptProfileVersion") || content.Contains("packageSchemaVersion")))
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.IncompatibleSchema,
                        "This interpretation service does not support the report-wide AI contract (version 2). The interpretation service must be updated before generation can be used. Your approved interpretation is retained.");
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
                    GeneratedAtUtc = relayResponse.GeneratedAtUtc,
                    InterpretationMarkdown = relayResponse.InterpretationMarkdown,
                    EffectiveInputFingerprint = relayResponse.EffectiveInputFingerprint,
                    Omissions = relay.Package.Omissions.Concat(relayResponse.Omissions).Distinct().ToList(), KnowledgeBaseIds = relayResponse.KnowledgeBaseIds,
                    RetrievedSourceIds = relayResponse.RetrievedSourceIds,
                    ScientificGuidanceRevision = relayResponse.ScientificGuidanceRevision,
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

        sealed class RelayRequest
        {
            public string RequestSchemaVersion { get; set; }
            public string OutputInstructions { get; set; }
            public string OutputFormatVersion { get; set; }
            public string GenerationProfile { get; set; }
            public AnalysisInterpretationPackage Package { get; set; }
            public string ClientRequestId { get; set; }
        }

        sealed class RelayResponse
        {
            public string ResponseSchemaVersion { get; set; }
            public string RequestId { get; set; }
            public string Provider { get; set; }
            public string Model { get; set; }
            public string ReasoningEffort { get; set; }
            public DateTime GeneratedAtUtc { get; set; }
            public string InterpretationMarkdown { get; set; }
            public string EffectiveInputFingerprint { get; set; }
            public string ScientificGuidanceRevision { get; set; }
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
        public string DefaultModel { get; set; }
        public string DefaultReasoningEffort { get; set; }
        public List<InterpretationOperatorModelOption> Models { get; set; } = new List<InterpretationOperatorModelOption>();
    }

    public sealed class InterpretationOperatorModelOption
    {
        public string Id { get; set; }
        public List<string> ReasoningEfforts { get; set; } = new List<string>();
    }
}
