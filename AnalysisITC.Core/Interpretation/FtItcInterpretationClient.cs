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
        public const string RequestSchemaVersion = "ft-itc-relay-request-2.0";
        public const string ResponseSchemaVersion = "ft-itc-relay-response-2.0";

        public const int MaximumRequestBytes = 2 * 1024 * 1024;

        readonly HttpClient httpClient;
        readonly Uri endpoint;
        static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        public FtItcInterpretationClient(HttpClient httpClient, Uri baseUri)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            if (baseUri == null || !baseUri.IsAbsoluteUri) throw new ArgumentException("An absolute relay base URI is required.", nameof(baseUri));
            endpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/api/interpretation/generate", UriKind.Absolute);
        }

        public async Task<AnalysisInterpretationProviderResponse> GenerateAsync(
            AnalysisInterpretationGenerationRequest request,
            CancellationToken cancellationToken)
        {
            if (request?.Package == null || request.Prompt == null) throw new ArgumentNullException(nameof(request));
            var relay = new RelayRequest
            {
                RequestSchemaVersion = RequestSchemaVersion,
                PromptProfileVersion = request.Prompt.PromptVersion,
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
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                throw new AnalysisInterpretationProviderException(
                    cancellationToken.IsCancellationRequested ? AnalysisInterpretationFailureKind.Cancelled : AnalysisInterpretationFailureKind.Timeout,
                    cancellationToken.IsCancellationRequested ? "Interpretation generation was cancelled." : "The interpretation service timed out.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure, "The interpretation service could not be reached.", ex);
            }

            using (response)
            {
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
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
                            : "The interpretation service rejected the request payload. It may require the report-wide version 2 server update.");
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
                    || relayResponse.Omissions == null || relayResponse.KnowledgeBaseIds == null || relayResponse.RetrievedSourceIds == null)
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse,
                        "The version 2 interpretation response is missing valid effective-input provenance.");
                return new AnalysisInterpretationProviderResponse
                {
                    RequestId = relayResponse.RequestId,
                    Provider = relayResponse.Provider,
                    Model = relayResponse.Model,
                    GeneratedAtUtc = relayResponse.GeneratedAtUtc,
                    InterpretationMarkdown = relayResponse.InterpretationMarkdown,
                    EffectiveInputFingerprint = relayResponse.EffectiveInputFingerprint,
                    Omissions = relay.Package.Omissions.Concat(relayResponse.Omissions).Distinct().ToList(), KnowledgeBaseIds = relayResponse.KnowledgeBaseIds,
                    RetrievedSourceIds = relayResponse.RetrievedSourceIds,
                };
            }
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
            public string PromptProfileVersion { get; set; }
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
            public DateTime GeneratedAtUtc { get; set; }
            public string InterpretationMarkdown { get; set; }
            public string EffectiveInputFingerprint { get; set; }
            public List<string> Omissions { get; set; }
            public List<string> KnowledgeBaseIds { get; set; }
            public List<string> RetrievedSourceIds { get; set; }
        }
    }
}
