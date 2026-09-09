using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AnalysisITC.Core.Interpretation;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public sealed class OpenAIInterpretationProvider : IAnalysisInterpretationProvider
{
    const int MaximumResponseBytes = 1024 * 1024;
    readonly HttpClient client;
    readonly OpenAIInterpretationOptions options;

    public OpenAIInterpretationProvider(HttpClient client, IOptions<InterpretationOptions> configured)
    { this.client = client; options = configured.Value.OpenAI; }

    public async Task<AnalysisInterpretationProviderResponse> GenerateAsync(
        AnalysisInterpretationGenerationRequest request, CancellationToken cancellationToken)
    {
        var package = AnalysisInterpretationThermograms.Copy(request.Package);
        var retrieval = !string.IsNullOrWhiteSpace(options.VectorStoreId);
        var contextRetried = false;
        var retrievalRetried = false;
        while (true)
        {
            var prompt = AnalysisInterpretationPromptBuilder.Build(package, request.ClientRequestId);
            try
            {
                var response = await GenerateAttemptAsync(request, prompt, retrieval, cancellationToken);
                response.EffectiveInputFingerprint = prompt.InputFingerprint;
                response.Omissions = package.Omissions.ToList();
                response.KnowledgeBaseIds = retrieval ? new List<string> { options.VectorStoreId } : new List<string>();
                return response;
            }
            catch (RetryableInputException exception) when (exception.ContextSize && !contextRetried)
            {
                AnalysisInterpretationLog.Write("provider-fallback", request.ClientRequestId, "reason=context_size omitThermograms=true");
                contextRetried = true;
                if (!AnalysisInterpretationThermograms.OmitTraces(package, "All thermograms and sampled baselines omitted after provider context-size rejection."))
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.PayloadRejected,
                        "Report evidence exceeds the model context even without thermograms. Shorten background or create a smaller report selection.");
            }
            catch (RetryableInputException exception) when (!exception.ContextSize && retrieval && !retrievalRetried)
            {
                AnalysisInterpretationLog.Write("provider-fallback", request.ClientRequestId, "reason=retrieval_failed retrieval=false");
                retrievalRetried = true; retrieval = false;
                package.Omissions.Add("Knowledge retrieval failed; this attempt has no retrieved source evidence. Do not emit knowledge-base references.");
            }
            catch (RetryableInputException exception)
            {
                throw new AnalysisInterpretationProviderException(exception.ContextSize ? AnalysisInterpretationFailureKind.PayloadRejected : AnalysisInterpretationFailureKind.ServiceFailure,
                    exception.ContextSize ? "Report evidence still exceeds the model context without thermograms. Shorten background or create a smaller report selection." : "Knowledge retrieval failed.");
            }
        }
    }

    async Task<AnalysisInterpretationProviderResponse> GenerateAttemptAsync(AnalysisInterpretationGenerationRequest request,
        AnalysisInterpretationPrompt prompt, bool retrieval, CancellationToken cancellationToken)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        AnalysisInterpretationLog.Write("provider-send", request.ClientRequestId,
            $"model={AnalysisInterpretationLog.Token(options.Model)} retrieval={retrieval} fingerprint={prompt.InputFingerprint}");
        using var message = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = options.Model,
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
        try { response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (OperationCanceledException ex)
        {
            throw new AnalysisInterpretationProviderException(
                cancellationToken.IsCancellationRequested ? AnalysisInterpretationFailureKind.Cancelled : AnalysisInterpretationFailureKind.Timeout,
                cancellationToken.IsCancellationRequested ? "Interpretation generation was cancelled." : "The model request timed out.", ex);
        }
        catch (HttpRequestException ex)
        { throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure, "The model service could not be reached.", ex); }

        using (response)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var errorCode = "none";
            if (!response.IsSuccessStatusCode && bytes.Length <= MaximumResponseBytes)
            {
                try
                {
                    using var errorDocument = JsonDocument.Parse(bytes);
                    if (errorDocument.RootElement.TryGetProperty("error", out var error)
                        && error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
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
                        if (errorJson.RootElement.TryGetProperty("error", out var error)) ThrowIfRetryable(error);
                    }
                    catch (JsonException) { }
                }
                throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure,
                    "The model service returned HTTP " + (int)response.StatusCode + ".");
            }
            try
            {
                if (bytes.Length > MaximumResponseBytes)
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse,
                        "The model service response exceeded the size limit.");
                using var json = JsonDocument.Parse(bytes);
                var root = json.RootElement;
                if (root.TryGetProperty("error", out var providerError)) ThrowIfRetryable(providerError);
                if (root.TryGetProperty("output", out var toolOutput) && toolOutput.ValueKind == JsonValueKind.Array)
                    foreach (var tool in toolOutput.EnumerateArray())
                        if (tool.TryGetProperty("type", out var toolType) && toolType.GetString() == "file_search_call"
                            && tool.TryGetProperty("status", out var toolStatus) && toolStatus.GetString() == "failed")
                            throw new RetryableInputException(false);
                if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed")
                    throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse,
                        "The model service did not complete the response.");
                var markdown = ExtractText(root);
                var id = root.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
                var model = root.TryGetProperty("model", out var modelValue) ? modelValue.GetString() : null;
                var generated = root.TryGetProperty("created_at", out var created) && created.TryGetInt64(out var unix)
                    ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime : DateTime.UtcNow;
                return new AnalysisInterpretationProviderResponse
                {
                    RequestId = id ?? request.ClientRequestId, Provider = "openai", Model = model ?? options.Model,
                    GeneratedAtUtc = generated, InterpretationMarkdown = markdown,
                    RetrievedSourceIds = SourceIds(root),
                };
            }
            catch (JsonException ex)
            { throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse, "The model service returned invalid JSON.", ex); }
        }
    }

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
            (!item.TryGetProperty("type", out var type) || type.GetString() == "message")
            && (!item.TryGetProperty("role", out var role) || role.GetString() == "assistant")
            && (!item.TryGetProperty("phase", out var phase) || phase.GetString() != "commentary")).ToList();
        var final = messages.LastOrDefault(item => item.TryGetProperty("phase", out var phase) && phase.GetString() == "final_answer");
        if (final.ValueKind == JsonValueKind.Undefined) final = messages.LastOrDefault();
        if (final.ValueKind == JsonValueKind.Object && final.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var parts = content.EnumerateArray().Where(part => part.TryGetProperty("type", out var type) && type.GetString() == "output_text"
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
