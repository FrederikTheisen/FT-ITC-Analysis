using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnalysisITC.Core.Interpretation;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public sealed class MistralInterpretationProvider(HttpClient client, IOptions<InterpretationOptions> configured,
    InterpretationUsageStore usageStore) : IHostedInterpretationProvider
{
    public string ProviderId => "mistral";
    const int MaximumResponseBytes = 1024 * 1024;

    public async Task<AnalysisInterpretationProviderResponse> GenerateAsync(
        AnalysisInterpretationGenerationRequest request, CancellationToken cancellationToken)
    {
        var options = configured.Value.Mistral;
        if (request.PackageJson is not { } raw || string.IsNullOrWhiteSpace(request.OutputInstructions)
            || string.IsNullOrWhiteSpace(request.OutputFormatVersion) || string.IsNullOrWhiteSpace(request.RequestedModel))
            throw new InvalidOperationException("The provider requires resolved model, evidence and output instructions.");
        if (!configured.Value.AllowedModels.TryGetValue(request.RequestedModel, out var model)
            || model.Provider != ProviderId || !model.ReasoningEfforts.Contains(request.RequestedReasoningEffort, StringComparer.Ordinal))
            throw new InvalidOperationException("The model and reasoning setting are not configured for Mistral.");
        var package = JsonNode.Parse(raw.GetRawText()) ?? throw new InvalidOperationException("Invalid evidence.");
        var omissions = OpenAIInterpretationProvider.RawOmissions(package).Distinct().ToList();
        var summary = request.TaskType == "summary";
        var prompt = summary
            ? SummaryGuidance.BuildPrompt(request.OutputFormatVersion, request.OutputInstructions, package.ToJsonString(), request.ClientRequestId)
            : ScientificGuidance.BuildPrompt(request.OutputFormatVersion, request.OutputInstructions, package.ToJsonString(), false,
                request.ClientRequestId, request.RequestedGuidanceVariant, request.OmitScientificGuidance);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.RequestedModel,
            ["messages"] = new[] { new { role = "system", content = prompt.SystemInstructions }, new { role = "user", content = prompt.UserMessage } },
            ["max_tokens"] = model.MaxOutputTokens ?? options.MaxOutputTokens,
            ["stream"] = false,
        };
        if (request.RequestedReasoningEffort != "default") payload["reasoning_effort"] = request.RequestedReasoningEffort;
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var message = new HttpRequestMessage(HttpMethod.Post, options.Endpoint)
        { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        if (string.IsNullOrWhiteSpace(request.ServerExecutionId) || !usageStore.IsEnabled)
            throw Failure(AnalysisInterpretationFailureKind.AccountingUnavailable, "Usage accounting is unavailable before provider dispatch.");
        try { usageStore.BeginAttempt(request.ServerExecutionId, 1); }
        catch (Exception ex) { throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.AccountingUnavailable, "Usage accounting could not start the provider attempt.", ex); }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var receipt = new InterpretationUsageAttempt
        {
            ServerExecutionId = request.ServerExecutionId, RequestId = request.ServerExecutionId, TaskType = request.TaskType,
            Provider = ProviderId, AttemptNumber = 1, TimestampUtc = DateTime.UtcNow, Model = request.RequestedModel,
            ReasoningEffort = request.RequestedReasoningEffort, GuidanceVariant = summary ? null : request.RequestedGuidanceVariant,
            GuidanceRevision = request.EffectiveGuidanceRevision, FileSearchEnabled = false, FileSearchCalls = 0,
            Outcome = "provider_error",
        };
        try
        {
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            receipt.HttpStatus = (int)response.StatusCode;
            receipt.ProviderRequestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : null;
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var limited = new SizeLimitedReadStream(stream, MaximumResponseBytes);
            JsonDocument parsed;
            try { parsed = await JsonDocument.ParseAsync(limited, cancellationToken: timeout.Token); }
            catch (JsonException) when (!response.IsSuccessStatusCode)
            { throw HttpFailure(response, default); }
            using var json = parsed;
            var root = json.RootElement;
            receipt.ProviderResponseId = Text(root, "id");
            receipt.Model = Text(root, "model") ?? receipt.Model;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                receipt.InputTokens = Count(usage, "prompt_tokens");
                receipt.OutputTokens = Count(usage, "completion_tokens");
                receipt.TotalTokens = Count(usage, "total_tokens");
                receipt.CachedInputTokens = Count(usage, "cached_tokens");
                if (usage.TryGetProperty("prompt_tokens_details", out var inputDetails))
                    receipt.CachedInputTokens ??= Count(inputDetails, "cached_tokens");
                if (usage.TryGetProperty("completion_tokens_details", out var outputDetails))
                    receipt.ReasoningTokens = Count(outputDetails, "reasoning_tokens");
            }
            var cost = usageStore.Estimate(request.RequestedModel, receipt.InputTokens, receipt.CachedInputTokens,
                receipt.CacheWriteTokens, receipt.OutputTokens, 0);
            receipt.ModelCost = cost.Model; receipt.FileSearchCost = cost.FileSearch; receipt.CombinedCost = cost.Combined;
            receipt.PricingRevision = cost.Revision; receipt.InputRate = cost.InputRate; receipt.CachedInputRate = cost.CachedRate;
            receipt.CacheWriteRate = cost.CacheWriteRate; receipt.OutputRate = cost.OutputRate; receipt.FileSearchRate = cost.FileSearchRate;
            if (!response.IsSuccessStatusCode)
            {
                throw HttpFailure(response, root);
            }
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1)
                throw Failure(AnalysisInterpretationFailureKind.InvalidResponse, "The model service returned invalid choices.");
            var choice = choices[0];
            if (Text(choice, "finish_reason") != "stop")
                throw Failure(AnalysisInterpretationFailureKind.InvalidResponse, "The model reply did not finish; increase its configured output-token limit if necessary.");
            var content = choice.GetProperty("message").GetProperty("content");
            var markdown = content.ValueKind == JsonValueKind.String ? content.GetString() : content.ValueKind == JsonValueKind.Array
                ? string.Concat(content.EnumerateArray().Where(value => Text(value, "type") == "text").Select(value => Text(value, "text"))) : null;
            if (string.IsNullOrWhiteSpace(markdown)) throw Failure(AnalysisInterpretationFailureKind.InvalidResponse, "The model service returned no final answer.");
            timeout.Token.ThrowIfCancellationRequested();
            receipt.Outcome = "success";
            return new AnalysisInterpretationProviderResponse
            {
                Provider = ProviderId, Model = receipt.Model, RequestId = receipt.ProviderResponseId ?? request.ClientRequestId,
                ReasoningEffort = request.RequestedReasoningEffort, GeneratedAtUtc = DateTime.UtcNow, TaskType = request.TaskType,
                InterpretationMarkdown = markdown, EffectiveInputFingerprint = prompt.InputFingerprint,
                ScientificGuidanceRevision = summary ? SummaryGuidance.Revision : prompt.PromptVersion,
                ScientificGuidanceVariant = summary ? null : request.RequestedGuidanceVariant,
                ScientificInstructionsFingerprint = ScientificGuidance.Hash(prompt.SystemInstructions),
                OutputInstructionsFingerprint = prompt.OutputInstructionsFingerprint,
                Omissions = omissions,
                ProviderAttempts = 1, InputTokens = receipt.InputTokens, OutputTokens = receipt.OutputTokens,
                CachedInputTokens = receipt.CachedInputTokens, ReasoningTokens = receipt.ReasoningTokens,
                TotalTokens = receipt.TotalTokens, EstimatedCost = cost.Combined,
            };
        }
        catch (OperationCanceledException ex)
        {
            var kind = cancellationToken.IsCancellationRequested ? AnalysisInterpretationFailureKind.Cancelled : AnalysisInterpretationFailureKind.Timeout;
            receipt.Outcome = kind == AnalysisInterpretationFailureKind.Cancelled ? "cancelled" : "timeout";
            throw new AnalysisInterpretationProviderException(kind, "The model request was cancelled or timed out.", ex);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or RequestBodyTooLargeException)
        { throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.InvalidResponse, "The model service returned an invalid response.", ex); }
        catch (HttpRequestException ex)
        { throw new AnalysisInterpretationProviderException(AnalysisInterpretationFailureKind.ServiceFailure, "The model service could not be reached.", ex); }
        finally
        {
            receipt.LatencyMs = timer.ElapsedMilliseconds;
            try { usageStore.RecordAttempt(receipt); }
            catch (Exception ex)
            {
                // The durable start retains the unresolved charge; keep a successful draft.
                AnalysisInterpretationLog.Write("usage-receipt-failed", request.ServerExecutionId, "type=" + ex.GetType().Name);
            }
        }
    }

    static AnalysisInterpretationProviderException HttpFailure(HttpResponseMessage response, JsonElement root)
    {
        var code = Text(root, "code");
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error)) code ??= Text(error, "code");
        var context = code is "context_length_exceeded" or "context_window_exceeded";
        var kind = response.StatusCode == HttpStatusCode.TooManyRequests ? AnalysisInterpretationFailureKind.RateLimited
            : context || response.StatusCode == HttpStatusCode.RequestEntityTooLarge ? AnalysisInterpretationFailureKind.PayloadRejected
            : AnalysisInterpretationFailureKind.ServiceFailure;
        var retry = response.Headers.RetryAfter;
        var after = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
        if (after < TimeSpan.Zero) after = TimeSpan.Zero;
        return new AnalysisInterpretationProviderException(kind, "The selected model service rejected the request.", retryAfter: after);
    }

    static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var result) && result.ValueKind == JsonValueKind.String ? result.GetString() : null;
    static int? Count(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var result) && result.TryGetInt32(out var number) && number >= 0 ? number : null;
    static AnalysisInterpretationProviderException Failure(AnalysisInterpretationFailureKind kind, string message) => new(kind, message);
}
