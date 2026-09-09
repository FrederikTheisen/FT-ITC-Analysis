using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnalysisITC.Core.Interpretation;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public sealed class OpenAIInterpretationProvider : IAnalysisInterpretationProvider
{
    const int MaximumResponseBytes = 1024 * 1024;
    readonly HttpClient client;
    readonly OpenAIInterpretationOptions options;
    readonly InterpretationUsageStore? usageStore;

    public OpenAIInterpretationProvider(HttpClient client, IOptions<InterpretationOptions> configured, InterpretationUsageStore? usageStore = null)
    { this.client = client; options = configured.Value.OpenAI; this.usageStore = usageStore; }

    public async Task<AnalysisInterpretationProviderResponse> GenerateAsync(
        AnalysisInterpretationGenerationRequest request, CancellationToken cancellationToken)
    {
        using var deadlineCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineCancellation.Token);
        var operationToken = operationCancellation.Token;
        ThrowIfOperationCancelled(cancellationToken, deadlineCancellation.Token);
        if (request.PackageJson is not { } raw) throw new InvalidOperationException("The server provider requires raw evidence JSON.");
        JsonNode rawPackage = JsonNode.Parse(raw.GetRawText()) ?? throw new InvalidOperationException("The evidence package is malformed.");
        var rawOmissions = RawOmissions(rawPackage);
        var retrieval = !string.IsNullOrWhiteSpace(options.VectorStoreId);
        var contextRetried = false;
        var retrievalRetried = false;
        var attemptNumber = 0;
        while (true)
        {
            ThrowIfOperationCancelled(cancellationToken, deadlineCancellation.Token);
            var prompt = ScientificGuidance.BuildPrompt(request.Prompt.OutputFormatVersion, request.Prompt.ResponseFormatInstructions, rawPackage.ToJsonString(), retrieval, request.ClientRequestId);
            try
            {
                var response = await GenerateAttemptAsync(request, prompt, retrieval, ++attemptNumber, contextRetried, retrievalRetried, operationToken, cancellationToken);
                ThrowIfOperationCancelled(cancellationToken, deadlineCancellation.Token);
                response.ProviderAttempts = attemptNumber;
                response.EffectiveInputFingerprint = prompt.InputFingerprint;
                response.ScientificGuidanceRevision = ScientificGuidance.Revision;
                response.ScientificInstructionsFingerprint = ScientificGuidance.Hash(prompt.SystemInstructions);
                response.OutputInstructionsFingerprint = prompt.OutputInstructionsFingerprint;
                response.Omissions = rawOmissions.Distinct().ToList();
                response.KnowledgeBaseIds = retrieval ? new List<string> { options.VectorStoreId } : new List<string>();
                return response;
            }
            catch (RetryableInputException exception) when (exception.ContextSize && !contextRetried)
            {
                ThrowIfOperationCancelled(cancellationToken, deadlineCancellation.Token);
                AnalysisInterpretationLog.Write("provider-fallback", request.ClientRequestId, "reason=context_size omitThermograms=true");
                contextRetried = true;
                var omittedRawTraces = OmitRawThermograms(rawPackage);
                if (!omittedRawTraces)
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.PayloadRejected,
                        "Report evidence exceeds the model context even without thermograms. Shorten background or create a smaller report selection.");
                if (omittedRawTraces) rawOmissions.Add("All thermograms and sampled baselines omitted after provider context-size rejection.");
            }
            catch (RetryableInputException exception) when (!exception.ContextSize && retrieval && !retrievalRetried)
            {
                ThrowIfOperationCancelled(cancellationToken, deadlineCancellation.Token);
                AnalysisInterpretationLog.Write("provider-fallback", request.ClientRequestId, "reason=retrieval_failed retrieval=false");
                retrievalRetried = true; retrieval = false;
                rawOmissions.Add("Knowledge retrieval failed; this attempt has no retrieved source evidence. Do not emit knowledge-base references.");
            }
            catch (RetryableInputException exception)
            {
                ThrowIfOperationCancelled(cancellationToken, deadlineCancellation.Token);
                throw new AnalysisInterpretationProviderException(exception.ContextSize ? AnalysisInterpretationFailureKind.PayloadRejected : AnalysisInterpretationFailureKind.ServiceFailure,
                    exception.ContextSize ? "Report evidence still exceeds the model context without thermograms. Shorten background or create a smaller report selection." : "Knowledge retrieval failed.");
            }
        }
    }

    static bool OmitRawThermograms(JsonNode node)
    {
        if (node is not JsonObject package) return false;
        var removed = false;
        void OmitFrom(JsonArray? experiments)
        {
            if (experiments is null) return;
            foreach (var experiment in experiments.OfType<JsonObject>())
            {
                if (experiment["thermogram"] is not null)
                {
                    experiment.Remove("thermogram");
                    removed = true;
                }
            }
        }
        if (package["results"] is JsonArray results)
            foreach (var result in results.OfType<JsonObject>()) OmitFrom(result["experiments"] as JsonArray);
        OmitFrom(package["supportingExperiments"] as JsonArray);
        if (!removed) return false;
        if (package["dataBoundary"] is not JsonObject boundary) package["dataBoundary"] = boundary = new JsonObject();
        boundary["containsRawThermogramSamples"] = false;
        boundary["containsBaselineArrays"] = false;
        boundary["modelObservationRestriction"] = "No raw thermogram or sampled fitted-baseline arrays were supplied. Assess available summaries, controls and injection evidence only; do not claim to observe peak shape or settling.";
        if (package["omissions"] is not JsonArray omissions) package["omissions"] = omissions = new JsonArray();
        const string reason = "All thermograms and sampled baselines omitted after provider context-size rejection.";
        if (!omissions.Any(value => value?.ToJsonString() == JsonValue.Create(reason).ToJsonString())) omissions.Add(reason);
        return true;
    }

    static List<string> RawOmissions(JsonNode? package) => package?["omissions"] is JsonArray omissions
        ? omissions.Select(value => value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) ? text : null)
            .Where(text => !string.IsNullOrWhiteSpace(text)).Cast<string>().ToList()
        : new List<string>();

    static void ThrowIfOperationCancelled(CancellationToken callerCancellationToken, CancellationToken deadlineToken)
    {
        if (callerCancellationToken.IsCancellationRequested)
            throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.Cancelled,
                "Interpretation generation was cancelled.");
        if (deadlineToken.IsCancellationRequested)
            throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.Timeout,
                "The model request timed out.");
    }

    async Task<AnalysisInterpretationProviderResponse> GenerateAttemptAsync(AnalysisInterpretationGenerationRequest request,
        AnalysisInterpretationPrompt prompt, bool retrieval, int attemptNumber, bool contextFallback, bool retrievalFallback,
        CancellationToken operationToken, CancellationToken callerCancellationToken)
    {
        var effectiveModel = string.IsNullOrWhiteSpace(request.RequestedModel) ? options.Model : request.RequestedModel;
        var effectiveReasoning = string.IsNullOrWhiteSpace(request.RequestedReasoningEffort) ? options.ReasoningEffort : request.RequestedReasoningEffort;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        AnalysisInterpretationLog.Write("provider-send", request.ClientRequestId,
            $"model={AnalysisInterpretationLog.Token(effectiveModel)} retrieval={retrieval} fingerprint={prompt.InputFingerprint}");
        using var message = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = effectiveModel,
            ["reasoning"] = new { effort = effectiveReasoning },
            ["instructions"] = prompt.SystemInstructions,
            ["input"] = prompt.UserMessage,
            ["max_output_tokens"] = options.MaxOutputTokens,
            ["store"] = false,
            ["truncation"] = "disabled",
        };
        if (retrieval)
        {
            payload["tools"] = new[]
            {
                new
                {
                    type = "file_search",
                    vector_store_ids = new[] { options.VectorStoreId },
                    max_num_results = options.MaxFileSearchResults,
                },
            };
            payload["tool_choice"] = "required";
            payload["include"] = new[] { "file_search_call.results" };
        }
        message.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try { response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, operationToken); }
        catch (OperationCanceledException ex)
        {
            RecordAttempt(request, attemptNumber, effectiveModel, effectiveReasoning, retrieval, contextFallback, retrievalFallback,
                timer.ElapsedMilliseconds, callerCancellationToken.IsCancellationRequested ? "cancelled" : "timeout", null, null, null, new(), null);
            throw new AnalysisInterpretationProviderException(
                callerCancellationToken.IsCancellationRequested ? AnalysisInterpretationFailureKind.Cancelled : AnalysisInterpretationFailureKind.Timeout,
                callerCancellationToken.IsCancellationRequested ? "Interpretation generation was cancelled." : "The model request timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            RecordAttempt(request, attemptNumber, effectiveModel, effectiveReasoning, retrieval, contextFallback, retrievalFallback,
                timer.ElapsedMilliseconds, "provider_error", null, "transport_error", null, new(), null);
            throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure, "The model service could not be reached.", ex);
        }

        using (response)
        {
            byte[] bytes;
            try { bytes = await response.Content.ReadAsByteArrayAsync(operationToken); }
            catch (OperationCanceledException ex)
            {
                RecordAttempt(request, attemptNumber, effectiveModel, effectiveReasoning, retrieval, contextFallback, retrievalFallback,
                    timer.ElapsedMilliseconds, callerCancellationToken.IsCancellationRequested ? "cancelled" : "timeout", (int)response.StatusCode, null, null, new(), null);
                throw new AnalysisInterpretationProviderException(
                    callerCancellationToken.IsCancellationRequested ? AnalysisInterpretationFailureKind.Cancelled : AnalysisInterpretationFailureKind.Timeout,
                    callerCancellationToken.IsCancellationRequested ? "Interpretation generation was cancelled." : "The model response timed out.", ex);
            }
            catch (HttpRequestException ex)
            {
                RecordAttempt(request, attemptNumber, effectiveModel, effectiveReasoning, retrieval, contextFallback, retrievalFallback,
                    timer.ElapsedMilliseconds, "provider_error", (int)response.StatusCode, "transport_error", null, new(), null);
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure, "The model response could not be read.", ex);
            }
            var errorCode = "none";
            var failedResponseUsage = new ProviderUsage();
            int? failedResponseSearchCalls = null;
            string? failedResponseId = null;
            if (!response.IsSuccessStatusCode && bytes.Length <= MaximumResponseBytes)
            {
                try
                {
                    using var errorDocument = JsonDocument.Parse(bytes);
                    var errorRoot = errorDocument.RootElement;
                    if (errorRoot.ValueKind == JsonValueKind.Object)
                    {
                        failedResponseUsage = ParseUsage(errorRoot);
                        failedResponseSearchCalls = CountFileSearchCalls(errorRoot);
                        failedResponseId = errorRoot.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
                    }
                    if (errorRoot.ValueKind == JsonValueKind.Object && errorRoot.TryGetProperty("error", out var error)
                        && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                    {
                        // Allowlisted codes explain failures without logging provider messages or echoed inputs.
                        errorCode = code.GetString() switch
                        {
                            "insufficient_quota" => "insufficient_quota",
                            "rate_limit_exceeded" => "rate_limit_exceeded",
                            "context_length_exceeded" => "context_length_exceeded",
                            "invalid_api_key" => "invalid_api_key",
                            _ => "other",
                        };
                    }
                }
                catch (JsonException) { errorCode = "invalid_json"; }
            }
            var providerRequestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null;
            AnalysisInterpretationLog.Write("provider-response", request.ClientRequestId,
                $"http={(int)response.StatusCode} code={errorCode} providerRequest={AnalysisInterpretationLog.Token(providerRequestId)} bytes={bytes.Length} elapsedMs={timer.ElapsedMilliseconds}");
            if (!response.IsSuccessStatusCode)
            {
                var failedResponseCost = usageStore?.Estimate(effectiveModel, failedResponseUsage.Input, failedResponseUsage.Cached,
                    failedResponseUsage.CacheWrite, failedResponseUsage.Output, failedResponseSearchCalls) ?? new InterpretationCost();
                RecordAttempt(request, attemptNumber, effectiveModel, effectiveReasoning, retrieval, contextFallback, retrievalFallback,
                    timer.ElapsedMilliseconds, "provider_error", (int)response.StatusCode, errorCode, providerRequestId,
                    failedResponseUsage, failedResponseSearchCalls, failedResponseId, failedResponseCost);
            }
            if (errorCode == "insufficient_quota")
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.QuotaExceeded,
                    "The model provider quota or billing limit has been reached.");
            if (response.StatusCode == (HttpStatusCode)429)
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.RateLimited,
                    "The model service rate limit was reached.", retryAfter: RetryAfter(response));
            if (!response.IsSuccessStatusCode)
            {
                // Only explicitly classified context/retrieval failures permit a retry. Authentication and generic failures do not.
                if (response.StatusCode != HttpStatusCode.Unauthorized && response.StatusCode != HttpStatusCode.Forbidden
                    && bytes.Length <= MaximumResponseBytes)
                {
                    try
                    {
                        using var errorJson = JsonDocument.Parse(bytes);
                        if (errorJson.RootElement.ValueKind == JsonValueKind.Object
                            && errorJson.RootElement.TryGetProperty("error", out var error)) ThrowIfRetryable(error);
                    }
                    catch (JsonException) { }
                }
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure,
                    "The model service returned HTTP " + (int)response.StatusCode + ".");
            }
            var usage = new ProviderUsage();
            int? searchCalls = null;
            string? responseId = null;
            var estimatedCost = new InterpretationCost();
            var recorded = false;
            void RecordParsedAttempt(string outcome, string? failureCode)
            {
                if (recorded) return;
                RecordAttempt(request, attemptNumber, effectiveModel, effectiveReasoning, retrieval, contextFallback, retrievalFallback,
                    timer.ElapsedMilliseconds, outcome, (int)response.StatusCode, failureCode, providerRequestId, usage, searchCalls, responseId, estimatedCost);
                recorded = true;
            }
            try
            {
                if (bytes.Length > MaximumResponseBytes)
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse,
                        "The model service response exceeded the size limit.");
                using var json = JsonDocument.Parse(bytes);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse,
                        "The model service returned an invalid response.");
                usage = ParseUsage(root);
                searchCalls = CountFileSearchCalls(root);
                responseId = root.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String ? idValue.GetString() : null;
                var model = root.TryGetProperty("model", out var modelValue) && modelValue.ValueKind == JsonValueKind.String ? modelValue.GetString() : null;
                estimatedCost = usageStore?.Estimate(effectiveModel, usage.Input, usage.Cached, usage.CacheWrite, usage.Output, searchCalls) ?? new InterpretationCost();
                void RecordInvalidResponse(string failureCode)
                {
                    RecordParsedAttempt("provider_error", failureCode);
                }
                if (root.TryGetProperty("error", out var providerError)) ThrowIfRetryable(providerError);
                if (root.TryGetProperty("output", out var toolOutput) && toolOutput.ValueKind == JsonValueKind.Array)
                    foreach (var tool in toolOutput.EnumerateArray())
                        if (tool.ValueKind == JsonValueKind.Object
                            && tool.TryGetProperty("type", out var toolType) && toolType.ValueKind == JsonValueKind.String && toolType.GetString() == "file_search_call"
                            && tool.TryGetProperty("status", out var toolStatus) && toolStatus.ValueKind == JsonValueKind.String && toolStatus.GetString() == "failed")
                            throw new RetryableInputException(false);
                if (!root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String || status.GetString() != "completed")
                {
                    RecordInvalidResponse(IncompleteReason(root));
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse,
                        "The model service did not complete the response.");
                }
                var markdown = ExtractText(root);
                var generated = root.TryGetProperty("created_at", out var created) && created.TryGetInt64(out var unix)
                    ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime : DateTime.UtcNow;
                RecordParsedAttempt("success", null);
                return new AnalysisInterpretationProviderResponse
                {
                    RequestId = responseId ?? request.ClientRequestId, Provider = "openai", Model = model ?? effectiveModel, ReasoningEffort = effectiveReasoning,
                    GeneratedAtUtc = generated, InterpretationMarkdown = markdown,
                    RetrievedSourceIds = SourceIds(root),
                    InputTokens = usage.Input, CachedInputTokens = usage.Cached, CacheWriteTokens = usage.CacheWrite,
                    OutputTokens = usage.Output, ReasoningTokens = usage.Reasoning, TotalTokens = usage.Total, EstimatedCost = estimatedCost.Combined,
                };

            }
            catch (RetryableInputException exception)
            {
                RecordParsedAttempt("provider_error", exception.ContextSize ? "context_length_exceeded" : "retrieval_failed");
                throw;
            }
            catch (AnalysisInterpretationProviderException)
            {
                RecordParsedAttempt("provider_error", "invalid_response");
                throw;
            }
            catch (InvalidOperationException ex)
            {
                RecordParsedAttempt("provider_error", "invalid_response");
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse, "The model service returned an invalid response.", ex);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                RecordParsedAttempt("provider_error", "invalid_response");
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse, "The model service returned an invalid response.", ex);
            }
            catch (JsonException ex)
            {
                RecordParsedAttempt("provider_error", "invalid_json");
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse, "The model service returned invalid JSON.", ex);
            }
        }
    }

    void RecordAttempt(AnalysisInterpretationGenerationRequest request, int number, string model, string reasoning, bool retrieval,
        bool contextFallback, bool retrievalFallback, long latency, string outcome, int? httpStatus, string? errorCode,
        string? providerRequestId, ProviderUsage usage, int? fileSearchCalls, string? responseId = null, InterpretationCost? estimated = null)
    {
        var cost = estimated ?? new InterpretationCost();
        usageStore?.RecordAttempt(new InterpretationUsageAttempt
        {
            RequestId=request.ClientRequestId, AttemptNumber=number, OpenAIResponseId=responseId, ProviderRequestId=providerRequestId,
            TimestampUtc=DateTime.UtcNow, LatencyMs=latency, Model=model, ReasoningEffort=reasoning, FileSearchEnabled=retrieval,
            FileSearchCalls=fileSearchCalls, InputTokens=usage.Input, CachedInputTokens=usage.Cached, CacheWriteTokens=usage.CacheWrite,
            OutputTokens=usage.Output, ReasoningTokens=usage.Reasoning,
            VisibleOutputTokens=usage.Output is int output && usage.Reasoning is int reasoningTokens ? Math.Max(0,output-reasoningTokens) : null,
            TotalTokens=usage.Total, ModelCost=cost.Model, FileSearchCost=cost.FileSearch, CombinedCost=cost.Combined,
            PricingRevision=cost.Revision, InputRate=cost.InputRate, CachedInputRate=cost.CachedRate, CacheWriteRate=cost.CacheWriteRate,
            OutputRate=cost.OutputRate, FileSearchRate=cost.FileSearchRate, Outcome=outcome, HttpStatus=httpStatus,
            ErrorCode=errorCode, ContextFallback=contextFallback, RetrievalFallback=retrievalFallback,
        });
    }

    static ProviderUsage ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return new();
        int? Value(JsonElement parent, string name) => parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number) && number >= 0 ? number : null;
        var inputDetails = usage.TryGetProperty("input_tokens_details", out var input) ? input : default;
        var outputDetails = usage.TryGetProperty("output_tokens_details", out var output) ? output : default;
        return new(Value(usage,"input_tokens"), Value(inputDetails,"cached_tokens"), Value(inputDetails,"cache_write_tokens"),
            Value(usage,"output_tokens"), Value(outputDetails,"reasoning_tokens"), Value(usage,"total_tokens"));
    }

    static string IncompleteReason(JsonElement root)
    {
        if (root.TryGetProperty("incomplete_details", out var details) && details.ValueKind == JsonValueKind.Object
            && details.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String)
            return reason.GetString() switch
            {
                "max_output_tokens" => "max_output_tokens",
                "content_filter" => "content_filter",
                _ => "incomplete",
            };
        return "incomplete";
    }

    static int CountFileSearchCalls(JsonElement root) => root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array
        ? output.EnumerateArray().Count(item => item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "file_search_call") : 0;

    readonly record struct ProviderUsage(int? Input = null, int? Cached = null, int? CacheWrite = null, int? Output = null, int? Reasoning = null, int? Total = null);

    sealed class RetryableInputException(bool contextSize) : Exception
    { public bool ContextSize { get; } = contextSize; }

    static void ThrowIfRetryable(JsonElement error)
    {
        if (error.ValueKind != JsonValueKind.Object) return;
        var code = error.TryGetProperty("code", out var codeValue) && codeValue.ValueKind == JsonValueKind.String ? codeValue.GetString() : null;
        if (code == "context_length_exceeded" || code == "input_tokens_exceeded") throw new RetryableInputException(true);
        if (code == "file_search_error" || code == "file_search_failed" || code == "vector_store_unavailable" || code == "retrieval_failed")
            throw new RetryableInputException(false);
    }

    static List<string> SourceIds(JsonElement root)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
                foreach (var property in element.EnumerateObject())
                {
                    if ((property.Name == "file_id" || property.Name == "source_id") && property.Value.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(property.Value.GetString())) ids.Add(property.Value.GetString()!);
                    else Visit(property.Value);
                }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Visit(item);
        }
        Visit(root);
        return ids.OrderBy(value => value, StringComparer.Ordinal).ToList();
    }

    static string ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return FailText();
        var messages = output.EnumerateArray().Where(item =>
            item.ValueKind == JsonValueKind.Object
            && (!item.TryGetProperty("type", out var type) || type.ValueKind == JsonValueKind.String && type.GetString() == "message")
            && (!item.TryGetProperty("role", out var role) || role.ValueKind == JsonValueKind.String && role.GetString() == "assistant")
            && (!item.TryGetProperty("phase", out var phase) || phase.ValueKind == JsonValueKind.String && phase.GetString() != "commentary")).ToList();
        var final = messages.LastOrDefault(item => item.TryGetProperty("phase", out var phase) && phase.ValueKind == JsonValueKind.String && phase.GetString() == "final_answer");
        if (final.ValueKind == JsonValueKind.Undefined) final = messages.LastOrDefault();
        if (final.ValueKind == JsonValueKind.Object && final.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var parts = content.EnumerateArray().Where(part => part.ValueKind == JsonValueKind.Object
                && part.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "output_text"
                && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                .Select(part => part.GetProperty("text").GetString()).ToList();
            if (parts.Count > 0) return string.Concat(parts);
        }
        return FailText();
    }

    static string FailText() => throw new AnalysisInterpretationProviderException(
        AnalysisInterpretationFailureKind.InvalidResponse, "The model service response contained no text output.");

    static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta) return delta;
        if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
        { var value = date - DateTimeOffset.UtcNow; return value > TimeSpan.Zero ? value : TimeSpan.Zero; }
        return null;
    }
}
