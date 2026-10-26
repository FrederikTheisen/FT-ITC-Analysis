using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnalysisITC.Core.Interpretation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class MistralInterpretationProviderTests
{
    [Theory]
    [InlineData("summary", "mistral-small-2603", "none")]
    [InlineData("instant", "mistral-small-2603", "none")]
    [InlineData("fast", "mistral-small-2603", "high")]
    [InlineData("standard", "zai-glm-5-3", "high")]
    [InlineData("in-depth", "zai-glm-5-3", "max")]
    public async Task EveryConfiguredPresetDispatchesItsOwnModelAndReasoning(string preset,string model,string reasoning)
    {
        using var fixture=new Fixture();
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null&&!File.Exists(Path.Combine(root.FullName,"AnalysisITC.Web","appsettings.json")))root=root.Parent;
        using var file=JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName,"AnalysisITC.Web","appsettings.json")));
        var configuration=JsonSerializer.Deserialize<InterpretationOptions>(file.RootElement.GetProperty("Interpretation"))!;
        var mapping=configuration.Mistral.PresetMappings[preset];
        Assert.Equal(model,mapping.Model);Assert.Equal(reasoning,mapping.ReasoningEffort);
        fixture.Options.AllowedModels[model]=configuration.AllowedModels[model];
        fixture.Options.Pricing[model]=configuration.Pricing[model];
        using var client=new HttpClient(new Handler(async(message,_)=>
        {
            using var sent=JsonDocument.Parse(await message.Content!.ReadAsStringAsync());
            Assert.Equal(model,sent.RootElement.GetProperty("model").GetString());
            Assert.Equal(reasoning!="default",sent.RootElement.TryGetProperty("reasoning_effort",out var effort));
            if(reasoning!="default")Assert.Equal(reasoning,effort.GetString());
            return Response(HttpStatusCode.OK,"{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"Invented evidence only.\"}}],\"usage\":{\"prompt_tokens\":20,\"completion_tokens\":5}}");
        }));
        var request=fixture.Request(reasoning);request.RequestedModel=model;
        request.TaskType=preset=="summary"?"summary":"interpretation";
        var result=await fixture.Provider(client).GenerateAsync(request,CancellationToken.None);
        Assert.Equal(model,result.Model);Assert.Equal("mistral",result.Provider);
        Assert.Equal(0,fixture.Store.ReadExecutionAccounting(request.ServerExecutionId).UnresolvedCount);
    }

    [Theory]
    [InlineData("default", false)]
    [InlineData("none", true)]
    [InlineData("high", true)]
    [InlineData("low", true)]
    [InlineData("max", true)]
    public async Task SendsOnlyConfiguredReasoningAndAccountsFinalText(string reasoning, bool sent)
    {
        using var fixture = new Fixture();
        string? body = null;
        var raw = """{"id":"mistral-receipt","model":"mistral-test","choices":[{"finish_reason":"stop","message":{"content":[{"type":"thinking","thinking":[{"type":"text","text":"private reasoning"}]},{"type":"text","text":"## Overall interpretation\nSynthetic evidence only."}]}}],"usage":{"prompt_tokens":1000,"completion_tokens":200,"total_tokens":1200,"prompt_tokens_details":{"cached_tokens":100}}}""";
        if (reasoning == "default")
        {
            var plain = JsonNode.Parse(raw)!;
            plain["choices"]![0]!["message"]!["content"] = "## Overall interpretation\nSynthetic evidence only.";
            raw = plain.ToJsonString();
        }
        using var client = new HttpClient(new Handler(async (request, _) =>
        {
            body = await request.Content!.ReadAsStringAsync();
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            return Response(HttpStatusCode.OK, raw);
        }));
        var request = fixture.Request(reasoning);
        var result = await fixture.Provider(client).GenerateAsync(request, CancellationToken.None);
        using var json = JsonDocument.Parse(body!);
        Assert.Equal(sent, json.RootElement.TryGetProperty("reasoning_effort", out var actual));
        if (sent) Assert.Equal(reasoning, actual.GetString());
        Assert.Equal(321, json.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("messages").GetArrayLength());
        Assert.False(json.RootElement.TryGetProperty("tools", out _));
        Assert.Equal("mistral", result.Provider);
        Assert.DoesNotContain("private reasoning", result.InterpretationMarkdown);
        Assert.Empty(result.KnowledgeBaseIds); Assert.Empty(result.RetrievedSourceIds);
        // 900 uncached input * $2/M + 100 cached * $0.2/M + 200 output * $12/M.
        Assert.Equal(.00422m, result.EstimatedCost);
        Assert.Equal(.00422m, fixture.Store.ReadExecutionAccounting(request.ServerExecutionId).KnownCost);
        using var db = fixture.Store.OpenForCommand(); using var command = db.CreateCommand();
        command.CommandText = "SELECT provider || ':' || provider_response_id FROM attempt_receipts";
        Assert.Equal("mistral:mistral-receipt", command.ExecuteScalar());
    }

    [Fact]
    public async Task MalformedOmissionsAreReadBeforeDispatchAndIgnored()
    {
        using var fixture = new Fixture();
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"id\":\"omissions\",\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"Synthetic draft\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1}}"))));
        var request = fixture.Request("none");
        var package = JsonNode.Parse(request.PackageJson!.Value.GetRawText())!.AsObject();
        package["omissions"] = new JsonArray("thermograms", 5, "thermograms");
        using var json = JsonDocument.Parse(package.ToJsonString());
        request.PackageJson = json.RootElement.Clone();
        var result = await fixture.Provider(client).GenerateAsync(request, CancellationToken.None);
        Assert.Equal(new[] { "thermograms" }, result.Omissions);
        using var db = fixture.Store.OpenForCommand(); using var command = db.CreateCommand();
        command.CommandText = "SELECT outcome FROM attempt_receipts";
        Assert.Equal("success", command.ExecuteScalar());
    }

    [Theory]
    [InlineData("length")]
    [InlineData("tool_calls")]
    [InlineData("error")]
    public async Task RejectsUnfinishedRepliesButRetainsTheirCharge(string reason)
    {
        using var fixture = new Fixture();
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"id\":\"unfinished\",\"choices\":[{\"finish_reason\":\"" + reason + "\",\"message\":{\"content\":\"partial\"}}],\"usage\":{\"prompt_tokens\":1000,\"completion_tokens\":200}}"))));
        var request = fixture.Request("high");
        var failure = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() => fixture.Provider(client).GenerateAsync(request, CancellationToken.None));
        Assert.Equal(AnalysisInterpretationFailureKind.InvalidResponse, failure.Kind);
        Assert.Equal(.0044m, fixture.Store.ReadExecutionAccounting(request.ServerExecutionId).KnownCost);
    }

    [Theory]
    [InlineData(429, AnalysisInterpretationFailureKind.RateLimited)]
    [InlineData(401, AnalysisInterpretationFailureKind.ServiceFailure)]
    [InlineData(503, AnalysisInterpretationFailureKind.ServiceFailure)]
    public async Task FailedCallsHaveUnresolvedCostWithoutBillingEvidence(int status, AnalysisInterpretationFailureKind expected)
    {
        using var fixture = new Fixture(); var calls = 0;
        using var client = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Response((HttpStatusCode)status,"{\"message\":\"unavailable\"}")); }));
        var request = fixture.Request("none");
        var failure = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() => fixture.Provider(client).GenerateAsync(request, CancellationToken.None));
        Assert.Equal(expected, failure.Kind); Assert.Equal(1, calls);
        Assert.Equal(1, fixture.Store.ReadExecutionAccounting(request.ServerExecutionId).UnresolvedCount);
    }

    [Fact]
    public async Task CancellationAndMalformedResponsesDoNotInventZeroCost()
    {
        foreach (var malformed in new[] { false, true })
        {
            using var fixture = new Fixture();
            using var client = new HttpClient(new Handler(async (_, token) =>
            {
                if (!malformed) await Task.Delay(Timeout.Infinite, token);
                return Response(HttpStatusCode.OK, "not json");
            }));
            var request = fixture.Request("default"); using var cancel = new CancellationTokenSource(100);
            var failure = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() => fixture.Provider(client).GenerateAsync(request, cancel.Token));
            Assert.Equal(malformed ? AnalysisInterpretationFailureKind.InvalidResponse : AnalysisInterpretationFailureKind.Cancelled, failure.Kind);
            Assert.Equal(1, fixture.Store.ReadExecutionAccounting(request.ServerExecutionId).UnresolvedCount);
        }
    }

    [Fact]
    public async Task MissingAdmissionNeverDispatches()
    {
        using var fixture = new Fixture(); var calls = 0;
        using var client = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(Response(HttpStatusCode.OK,"{}")); }));
        var request = fixture.Request("none"); request.ServerExecutionId = Guid.NewGuid().ToString("N");
        var failure = await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(() => fixture.Provider(client).GenerateAsync(request, CancellationToken.None));
        Assert.Equal(AnalysisInterpretationFailureKind.AccountingUnavailable, failure.Kind); Assert.Equal(0,calls);
    }

    [Theory]
    [InlineData(429, "not json", AnalysisInterpretationFailureKind.RateLimited)]
    [InlineData(503, "not json", AnalysisInterpretationFailureKind.ServiceFailure)]
    [InlineData(400, "{\"error\":{\"code\":\"context_length_exceeded\"}}", AnalysisInterpretationFailureKind.PayloadRejected)]
    public async Task ErrorClassificationDoesNotDependOnAValidCompletionBody(int status,string body,AnalysisInterpretationFailureKind kind)
    {
        using var fixture=new Fixture();
        using var client=new HttpClient(new Handler((_,_)=>Task.FromResult(Response((HttpStatusCode)status,body))));
        var request=fixture.Request("none");
        var failure=await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(()=>fixture.Provider(client).GenerateAsync(request,CancellationToken.None));
        Assert.Equal(kind,failure.Kind);
    }

    [Fact]
    public async Task TimeoutAndLateReceiptFailureRetainUnresolvedAccounting()
    {
        using var fixture=new Fixture(); fixture.Options.Mistral.TimeoutSeconds=1;
        using(var client=new HttpClient(new Handler(async(_,token)=>{await Task.Delay(Timeout.Infinite,token);return Response(HttpStatusCode.OK,"{}");})))
        {
            var request=fixture.Request("none");
            var error=await Assert.ThrowsAsync<AnalysisInterpretationProviderException>(()=>fixture.Provider(client).GenerateAsync(request,CancellationToken.None));
            Assert.Equal(AnalysisInterpretationFailureKind.Timeout,error.Kind);
            Assert.Equal(1,fixture.Store.ReadExecutionAccounting(request.ServerExecutionId).UnresolvedCount);
        }
        fixture.Store.FaultInjector=stage=>stage=="receipt"?new IOException("test"):null;
        using var successful=new HttpClient(new Handler((_,_)=>Task.FromResult(Response(HttpStatusCode.OK,"{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"Synthetic draft\"}}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1}}"))));
        var retained=fixture.Request("none");
        Assert.Equal("Synthetic draft",(await fixture.Provider(successful).GenerateAsync(retained,CancellationToken.None)).InterpretationMarkdown);
        Assert.Equal(1,fixture.Store.ReadExecutionAccounting(retained.ServerExecutionId).UnresolvedCount);
    }

    sealed class Fixture : IDisposable
    {
        readonly string directory = Path.Combine(Path.GetTempPath(),"ftitc-mistral-"+Guid.NewGuid().ToString("N"));
        public InterpretationOptions Options { get; }
        public InterpretationUsageStore Store { get; }
        public Fixture()
        {
            Options = new() { Mistral = new() { Enabled=true,ApiKey="fake-key",Endpoint="https://mistral.test/v1/chat/completions" },
                UsageLog = new() { DatabasePath=Path.Combine(directory,"usage.db") } };
            Options.AllowedModels["mistral-test"] = new() { Provider="mistral",ReasoningEfforts=["default","none","low","high","max"],MaxOutputTokens=321 };
            Options.Pricing["mistral-test"] = new() { Revision="test",InputPerMillion=2,CachedInputPerMillion=.2m,OutputPerMillion=12 };
            Store = new(Microsoft.Extensions.Options.Options.Create(Options),NullLogger<InterpretationUsageStore>.Instance);
        }
        public MistralInterpretationProvider Provider(HttpClient client) => new(client,Microsoft.Extensions.Options.Options.Create(Options),Store);
        public AnalysisInterpretationGenerationRequest Request(string reasoning)
        {
            var id=Guid.NewGuid().ToString("N"); var package=new AnalysisInterpretationPackage();
            var prompt=AnalysisInterpretationPromptBuilder.Build(package); using var json=JsonDocument.Parse(prompt.CanonicalPackageJson);
            Assert.Equal(InterpretationAdmissionStatus.Admitted,Store.TryAdmit(new() { RequestId=id,ServerExecutionId=id,ClientRequestId=id,Provider="mistral" },null,DateTime.MinValue).Status);
            return new() { ServerExecutionId=id,ClientRequestId=id,PackageJson=json.RootElement.Clone(),RequestedProvider="mistral",
                RequestedModel="mistral-test",RequestedReasoningEffort=reasoning,RequestedGuidanceVariant="3.7.0",
                OutputFormatVersion=AnalysisInterpretationPromptBuilder.OutputFormatVersion,
                OutputInstructions=AnalysisInterpretationPromptBuilder.BuildResponseFormatInstructions(package) };
        }
        public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory,true); }
    }
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token); }
    static HttpResponseMessage Response(HttpStatusCode code,string body)=>new(code) { Content=new StringContent(body,Encoding.UTF8,"application/json") };
}
