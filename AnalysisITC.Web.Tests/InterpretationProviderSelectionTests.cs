using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AnalysisITC.Core.Interpretation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class InterpretationProviderSelectionTests
{
    [Theory]
    [InlineData("ft-itc-relay-request-3.0", "openai")]
    [InlineData("ft-itc-relay-request-4.0", "openai")]
    [InlineData("ft-itc-relay-request-5.0", "openai")]
    [InlineData("ft-itc-relay-request-6.0", "openai")]
    [InlineData("ft-itc-relay-request-7.0", "mistral")]
    public async Task OnlyVersionSevenSelectsAnotherProvider(string schema,string expected)
    {
        var context=new DefaultHttpContext(); var json=JsonSerializer.Serialize(Envelope(schema,"mistral"));
        context.Request.ContentType="application/json"; context.Request.Body=new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result=await new InterpretationRequestReader().ReadAsync(context.Request,CancellationToken.None);
        Assert.Null(result.Failure); Assert.Equal(expected,result.Request!.Provider);
        Assert.Equal(schema,result.Request.RequestSchemaVersion);
    }

    [Theory]
    [InlineData("unknown", HttpStatusCode.BadRequest,"invalid_interpretation_provider")]
    [InlineData("mistral", HttpStatusCode.ServiceUnavailable,"interpretation_provider_unavailable")]
    public async Task InvalidOrDisabledProviderNeverAdmitsAnExecution(string provider,HttpStatusCode expected,string code)
    {
        using var factory=new InterpretationTestFactory(); using var client=factory.CreateClient();
        var publicCode=factory.Services.GetRequiredService<PublicAccessRegistry>().Create().Code;
        client.DefaultRequestHeaders.Authorization=new("Bearer",publicCode);
        using var response=await client.PostAsJsonAsync("/api/interpretation/generate",Envelope(FtItcInterpretationClient.RequestSchemaVersion,provider));
        Assert.Equal(expected,response.StatusCode); Assert.Contains(code,await response.Content.ReadAsStringAsync());
        using var db=factory.Services.GetRequiredService<InterpretationUsageStore>().OpenForCommand(); using var query=db.CreateCommand();
        query.CommandText="SELECT count(*) FROM executions"; Assert.Equal(0L,query.ExecuteScalar());
    }

    [Fact]
    public void RegistryKeepsOpenAiMappingAndSharedPermissionsWhenEditingMistral()
    {
        var directory=Path.Combine(Path.GetTempPath(),"ftitc-provider-registry-"+Guid.NewGuid().ToString("N"));
        try
        {
            var options=JsonSerializer.Deserialize<InterpretationOptions>(JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepository(),"AnalysisITC.Web","appsettings.json"))).RootElement.GetProperty("Interpretation"))!;
            options.OperatorAccess.PresetRegistryPath=Path.Combine(directory,"presets.json");
            var registry=new GenerationPresetRegistry(Options.Create(options)); var before=registry.Read();
            registry.Update("instant","mistral-small-2603","none","mistral"); var after=registry.Read();
            Assert.Equal(12,after.SchemaVersion);
            Assert.Equal(before.Presets[0].Model,after.Presets[0].Model);
            Assert.Equal(before.Presets[0].AllowedTiers,after.Presets[0].AllowedTiers);
            Assert.Equal(before.Presets[0].Description,after.Presets[0].Description);
            Assert.Equal("mistral-small-2603",after.Presets[0].MappingFor("mistral").Model);
            Assert.Equal("none",after.Presets[0].MappingFor("mistral").ReasoningEffort);
            Assert.Throws<ArgumentException>(()=>registry.Update("instant",before.Presets[0].Model,"low","mistral"));
        }
        finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
    }

    [Fact]
    public void StaleMistralMappingBlocksOnlyAnEnabledMistral()
    {
        var directory=Path.Combine(Path.GetTempPath(),"ftitc-provider-registry-"+Guid.NewGuid().ToString("N"));
        try
        {
            var options=JsonSerializer.Deserialize<InterpretationOptions>(JsonDocument.Parse(File.ReadAllText(Path.Combine(FindRepository(),"AnalysisITC.Web","appsettings.json"))).RootElement.GetProperty("Interpretation"))!;
            options.OperatorAccess.PresetRegistryPath=Path.Combine(directory,"presets.json");
            options.Mistral.PresetMappings["fast"]=new() { Model="retired-mistral-model",ReasoningEffort="high" };
            var registry=new GenerationPresetRegistry(Options.Create(options));
            // Disabled Mistral: OpenAI keeps working and the stale mapping is retained.
            var value=registry.Read();
            Assert.Equal("retired-mistral-model",value.Presets.Single(x=>x.Id=="fast").MappingFor("mistral").Model);
            Assert.False(string.IsNullOrWhiteSpace(value.Presets.Single(x=>x.Id=="fast").MappingFor("openai").Model));
            Assert.Throws<ArgumentException>(()=>registry.Update("fast","retired-mistral-model","high","mistral"));
            options.Mistral.Enabled=true; options.Mistral.ApiKey="fake-key";
            Assert.Throws<InvalidDataException>(()=>registry.Read());
        }
        finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
    }

    [Theory]
    [InlineData("public")]
    [InlineData("standard")]
    [InlineData("advanced")]
    [InlineData("administrator")]
    public async Task BothAdaptersKeepTierPermissionsAndOneSharedAllowance(string tier)
    {
        var calls=0;
        using var original=new InterpretationTestFactory();
        using var factory=original.WithWebHostBuilder(builder=>builder.ConfigureServices(services=>
        {
            services.PostConfigure<InterpretationOptions>(options=>
            {
                options.Enabled=true;options.OperatorAccess.Enabled=true;
                options.OpenAI.ApiKey="fake";options.OpenAI.Model="gpt-5.6-luna";options.OpenAI.VectorStoreId="";
                options.Mistral.Enabled=true;options.Mistral.ApiKey="fake";
                foreach(var model in options.AllowedModels.Keys)
                    options.Pricing[model]=new() { Revision="test",InputPerMillion=2,OutputPerMillion=12 };
            });
            services.RemoveAll<IAnalysisInterpretationProvider>();
            HttpClient Transport(bool mistral)=>new(new CallbackHandler(async request=>
            {
                calls++;using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                var model=body.RootElement.GetProperty("model").GetString();
                var response=mistral
                    ? new { id="fake-mistral",model,choices=new[]{new { finish_reason="stop",message=new { content="Invented evidence only." } }},usage=new { prompt_tokens=1000,completion_tokens=100,total_tokens=1100 } } as object
                    : new { id="fake-openai",model,status="completed",output=new[]{new { type="message",content=new[]{new { type="output_text",text="Invented evidence only." }} }},usage=new { input_tokens=1000,output_tokens=100,total_tokens=1100 } };
                return new HttpResponseMessage(HttpStatusCode.OK) { Content=JsonContent.Create(response) };
            }));
            services.AddSingleton<IAnalysisInterpretationProvider>(sp=>new OpenAIInterpretationProvider(Transport(false),sp.GetRequiredService<IOptions<InterpretationOptions>>(),sp.GetRequiredService<InterpretationUsageStore>()));
            services.AddSingleton<IAnalysisInterpretationProvider>(sp=>new MistralInterpretationProvider(Transport(true),sp.GetRequiredService<IOptions<InterpretationOptions>>(),sp.GetRequiredService<InterpretationUsageStore>()));
        }));
        using var client=factory.CreateClient();
        string code,id;
        if(tier=="public")
        { var account=factory.Services.GetRequiredService<PublicAccessRegistry>().Create();code=account.Code;id=account.Record.Id; }
        else
        { var account=factory.Services.GetRequiredService<OperatorCodeRegistry>().Create("Invented test",null,true,tier);code=account.Code;id=account.Record.Id; }
        client.DefaultRequestHeaders.Authorization=new("Bearer",code);
        var presets=factory.Services.GetRequiredService<GenerationPresetRegistry>();
        presets.UpdatePublicLimits(.004m,5,24,5m,100,24);
        if(tier is "standard" or "advanced")presets.UpdateQuota(tier,.004m);
        var available=await client.GetFromJsonAsync<JsonElement>("/api/interpretation/options?requestSchemaVersion=ft-itc-relay-request-7.0");
        Assert.Equal(new[]{"openai","mistral"},available.GetProperty("providers").EnumerateArray().Select(p=>p.GetProperty("id").GetString()));
        if(tier=="administrator")
        {
            var models=available.GetProperty("models").EnumerateArray().Where(m=>m.GetProperty("selectionType").GetString()=="model").ToArray();
            Assert.Contains(models,m=>m.GetProperty("provider").GetString()=="mistral");
            var legacy=await client.GetFromJsonAsync<JsonElement>("/api/interpretation/operator/options");
            Assert.DoesNotContain(legacy.GetProperty("models").EnumerateArray(),m=>m.GetProperty("id").GetString()!.StartsWith("mistral-"));
        }
        else Assert.Empty(available.GetProperty("models").EnumerateArray());
        object Summary(string provider)=>new { requestSchemaVersion=FtItcInterpretationClient.RequestSchemaVersion,provider,taskType="summary",generationProfile="summary",omitScientificGuidance=false,clientRequestId=Guid.NewGuid().ToString("N"),outputFormatVersion=AnalysisInterpretationPromptBuilder.OutputFormatVersion,outputInstructions="Use one short sentence.",package=new { packageSchemaVersion="2.2" } };
        foreach(var provider in new[]{"openai","mistral"})
        {
            using var response=await client.PostAsJsonAsync("/api/interpretation/generate",Summary(provider));
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            Assert.Equal(provider,(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("provider").GetString());
        }
        var usage=factory.Services.GetRequiredService<InterpretationUsageStore>().GetOperatorUsage(id,DateTime.MinValue);
        Assert.Equal(.0064m,usage.KnownCost);Assert.Equal(0,usage.UnresolvedCount);
        if(tier!="administrator")
        {
            using var denied=await client.PostAsJsonAsync("/api/interpretation/generate",Summary("openai"));
            Assert.False(denied.IsSuccessStatusCode);Assert.Equal(2,calls);
        }
        else Assert.Equal(2,calls);
    }

    sealed class CallbackHandler(Func<HttpRequestMessage,Task<HttpResponseMessage>> callback):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>callback(request); }

    static string FindRepository()
    { for(var path=new DirectoryInfo(AppContext.BaseDirectory);path!=null;path=path.Parent) if(File.Exists(Path.Combine(path.FullName,"AnalysisITC.Web","appsettings.json"))) return path.FullName; throw new DirectoryNotFoundException(); }
    static object Envelope(string schema,string provider)=>new { requestSchemaVersion=schema,provider,omitScientificGuidance=false,taskType="interpretation",
        generationProfile=schema==FtItcInterpretationClient.LegacyRequestSchemaVersion?"fast":"instant",clientRequestId="0123456789abcdef0123456789abcdef",
        outputFormatVersion=AnalysisInterpretationPromptBuilder.OutputFormatVersion,outputInstructions="Use Markdown.",package=new { packageSchemaVersion="2.2" } };
}
