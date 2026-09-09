using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public sealed class InterpretationUsageStore
{
    readonly InterpretationOptions options;
    readonly ILogger<InterpretationUsageStore> logger;
    readonly object initializeLock = new();
    bool initialized;

    public InterpretationUsageStore(IOptions<InterpretationOptions> options, ILogger<InterpretationUsageStore> logger)
    { this.options = options.Value; this.logger = logger; }

    public bool IsEnabled => options.UsageLog.Enabled;

    public void RecordRequest(InterpretationUsageRequest record) => Safe(() =>
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT OR REPLACE INTO requests
        (request_id,trace_id,started_utc,completed_utc,authenticated_user_id,operator_code_id,report_id,analysis_ids,request_bytes,generation_profile,requested_preset,effective_preset,access_tier,preset_revision,requested_model,requested_reasoning,effective_model,effective_reasoning,request_version,response_version,package_version,prompt_version,output_version,knowledge_base_ids,latency_ms,outcome,http_status,error_code,provider_attempts,input_tokens,cached_input_tokens,cache_write_tokens,output_tokens,reasoning_tokens,visible_output_tokens,total_tokens,estimated_cost)
        VALUES ($request,$trace,$started,$completed,NULL,$operator,$report,$analyses,$bytes,$profile,$requestedPreset,$effectivePreset,$tier,$presetRevision,$requestedModel,$requestedReasoning,$model,$reasoning,$requestVersion,$responseVersion,$packageVersion,$promptVersion,$outputVersion,$knowledge,$latency,$outcome,$http,$error,$attempts,$input,$cached,$write,$output,$reasoningTokens,$visible,$total,$cost)
        """;
        Add(command, "$request", record.RequestId); Add(command, "$trace", record.TraceId); Add(command, "$started", Iso(record.StartedUtc));
        Add(command, "$completed", Iso(record.CompletedUtc)); Add(command, "$operator", record.OperatorCodeId); Add(command, "$report", record.ReportId);
        Add(command, "$analyses", record.AnalysisIds); Add(command, "$bytes", record.RequestBytes); Add(command, "$profile", record.GenerationProfile);
        Add(command, "$requestedPreset", record.RequestedPreset); Add(command, "$effectivePreset", record.EffectivePreset);
        Add(command, "$tier", record.AccessTier); Add(command, "$presetRevision", record.PresetRevision);
        Add(command, "$requestedModel", record.RequestedModel); Add(command, "$requestedReasoning", record.RequestedReasoning);
        Add(command, "$model", record.EffectiveModel); Add(command, "$reasoning", record.EffectiveReasoning);
        Add(command, "$requestVersion", record.RequestVersion); Add(command, "$responseVersion", record.ResponseVersion);
        Add(command, "$packageVersion", record.PackageVersion); Add(command, "$promptVersion", record.PromptVersion); Add(command, "$outputVersion", record.OutputVersion);
        Add(command, "$knowledge", record.KnowledgeBaseIds); Add(command, "$latency", record.LatencyMs); Add(command, "$outcome", record.Outcome);
        Add(command, "$http", record.HttpStatus); Add(command, "$error", record.ErrorCode); Add(command, "$attempts", record.ProviderAttempts);
        Add(command, "$input", record.InputTokens); Add(command, "$cached", record.CachedInputTokens); Add(command, "$write", record.CacheWriteTokens);
        Add(command, "$output", record.OutputTokens); Add(command, "$reasoningTokens", record.ReasoningTokens); Add(command, "$visible", record.VisibleOutputTokens);
        Add(command, "$total", record.TotalTokens); Add(command, "$cost", record.EstimatedCost); command.ExecuteNonQuery();
    });

    public void RecordAttempt(InterpretationUsageAttempt record) => Safe(() =>
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT OR REPLACE INTO attempts
        (request_id,attempt_number,openai_response_id,provider_request_id,timestamp_utc,latency_ms,model,reasoning,file_search_enabled,file_search_calls,input_tokens,cached_input_tokens,cache_write_tokens,output_tokens,reasoning_tokens,visible_output_tokens,total_tokens,model_cost,file_search_cost,combined_cost,pricing_revision,input_rate,cached_input_rate,cache_write_rate,output_rate,file_search_rate,outcome,http_status,error_code,context_fallback,retrieval_fallback)
        VALUES ($request,$number,$response,$provider,$time,$latency,$model,$reasoning,$search,$searchCalls,$input,$cached,$write,$output,$reasoningTokens,$visible,$total,$modelCost,$searchCost,$cost,$revision,$inputRate,$cachedRate,$writeRate,$outputRate,$searchRate,$outcome,$http,$error,$context,$retrieval)
        """;
        foreach (var value in new (string, object?)[] { ("$request",record.RequestId),("$number",record.AttemptNumber),("$response",record.OpenAIResponseId),("$provider",record.ProviderRequestId),("$time",Iso(record.TimestampUtc)),("$latency",record.LatencyMs),("$model",record.Model),("$reasoning",record.ReasoningEffort),("$search",record.FileSearchEnabled?1:0),("$searchCalls",record.FileSearchCalls),("$input",record.InputTokens),("$cached",record.CachedInputTokens),("$write",record.CacheWriteTokens),("$output",record.OutputTokens),("$reasoningTokens",record.ReasoningTokens),("$visible",record.VisibleOutputTokens),("$total",record.TotalTokens),("$modelCost",record.ModelCost),("$searchCost",record.FileSearchCost),("$cost",record.CombinedCost),("$revision",record.PricingRevision),("$inputRate",record.InputRate),("$cachedRate",record.CachedInputRate),("$writeRate",record.CacheWriteRate),("$outputRate",record.OutputRate),("$searchRate",record.FileSearchRate),("$outcome",record.Outcome),("$http",record.HttpStatus),("$error",record.ErrorCode),("$context",record.ContextFallback?1:0),("$retrieval",record.RetrievalFallback?1:0) }) Add(command,value.Item1,value.Item2);
        command.ExecuteNonQuery();
    });

    public InterpretationCost Estimate(string model, int? input, int? cached, int? cacheWrite, int? output, int? fileSearchCalls)
    {
        if (!options.Pricing.TryGetValue(model, out var price) || input is null || output is null) return new();
        var isLong = price.LongContextThreshold is long threshold && input > threshold;
        var inputRate = isLong ? price.LongInputPerMillion ?? price.InputPerMillion : price.InputPerMillion;
        var cachedRate = isLong ? price.LongCachedInputPerMillion ?? price.CachedInputPerMillion : price.CachedInputPerMillion;
        var writeRate = isLong ? price.LongCacheWritePerMillion ?? price.CacheWritePerMillion : price.CacheWritePerMillion;
        var outputRate = isLong ? price.LongOutputPerMillion ?? price.OutputPerMillion : price.OutputPerMillion;
        var cachedCount = cached ?? 0; var writeCount = cacheWrite ?? 0; var uncached = Math.Max(0, input.Value - cachedCount - writeCount);
        var modelCost = (uncached * inputRate + cachedCount * cachedRate + writeCount * writeRate + output.Value * outputRate) / 1_000_000m;
        var searchCost = (fileSearchCalls ?? 0) * price.FileSearchPerCall;
        return new(modelCost, searchCost, modelCost + searchCost, price.Revision, inputRate, cachedRate, writeRate, outputRate, price.FileSearchPerCall);
    }

    public SqliteConnection OpenForCommand() => Open();

    public InterpretationUsageAggregate Aggregate(string requestId)
    {
        try
        {
            using var connection=Open(); using var command=connection.CreateCommand();
            command.CommandText="SELECT count(*),sum(input_tokens),sum(cached_input_tokens),sum(cache_write_tokens),sum(output_tokens),sum(reasoning_tokens),sum(visible_output_tokens),sum(total_tokens),sum(combined_cost) FROM attempts WHERE request_id=$id";
            command.Parameters.AddWithValue("$id",requestId); using var reader=command.ExecuteReader(); reader.Read();
            int? Int(int i)=>reader.IsDBNull(i)?null:Convert.ToInt32(reader.GetValue(i),CultureInfo.InvariantCulture);
            return new(reader.GetInt32(0),Int(1),Int(2),Int(3),Int(4),Int(5),Int(6),Int(7),reader.IsDBNull(8)?null:Convert.ToDecimal(reader.GetValue(8),CultureInfo.InvariantCulture));
        }
        catch(Exception ex){logger.LogWarning(ex,"Interpretation usage attempts could not be aggregated.");return new();}
    }

    SqliteConnection Open()
    {
        EnsureInitialized();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = options.UsageLog.DatabasePath }.ToString());
        connection.Open(); using var pragma = connection.CreateCommand(); pragma.CommandText = "PRAGMA busy_timeout=5000;"; pragma.ExecuteNonQuery(); return connection;
    }

    void EnsureInitialized()
    {
        if (initialized || !options.UsageLog.Enabled) return;
        lock (initializeLock)
        {
            if (initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(options.UsageLog.DatabasePath)!);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = options.UsageLog.DatabasePath }.ToString()); connection.Open();
            using var command = connection.CreateCommand(); command.CommandText = Schema; command.ExecuteNonQuery();
            EnsureColumn(connection,"requested_preset","TEXT"); EnsureColumn(connection,"effective_preset","TEXT");
            EnsureColumn(connection,"access_tier","TEXT"); EnsureColumn(connection,"preset_revision","TEXT");
            using var version = connection.CreateCommand(); version.CommandText = "UPDATE schema_info SET version=2"; version.ExecuteNonQuery(); initialized = true;
        }
    }

    void Safe(Action action) { if (!options.UsageLog.Enabled) return; try { action(); } catch (Exception ex) { logger.LogWarning(ex, "Interpretation usage metadata could not be recorded."); } }
    static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    static string Iso(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    static void EnsureColumn(SqliteConnection connection,string name,string type)
    {
        using var query=connection.CreateCommand(); query.CommandText="SELECT count(*) FROM pragma_table_info('requests') WHERE name=$name"; query.Parameters.AddWithValue("$name",name);
        if (Convert.ToInt64(query.ExecuteScalar(),CultureInfo.InvariantCulture)>0) return;
        using var alter=connection.CreateCommand(); alter.CommandText=$"ALTER TABLE requests ADD COLUMN {name} {type}"; alter.ExecuteNonQuery();
    }

    const string Schema = """
    PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;
    CREATE TABLE IF NOT EXISTS schema_info(version INTEGER NOT NULL); INSERT INTO schema_info(version) SELECT 2 WHERE NOT EXISTS(SELECT 1 FROM schema_info);
    CREATE TABLE IF NOT EXISTS requests(request_id TEXT PRIMARY KEY,trace_id TEXT,started_utc TEXT,completed_utc TEXT,authenticated_user_id TEXT,operator_code_id TEXT,report_id TEXT,analysis_ids TEXT,request_bytes INTEGER,generation_profile TEXT,requested_preset TEXT,effective_preset TEXT,access_tier TEXT,preset_revision TEXT,requested_model TEXT,requested_reasoning TEXT,effective_model TEXT,effective_reasoning TEXT,request_version TEXT,response_version TEXT,package_version TEXT,prompt_version TEXT,output_version TEXT,knowledge_base_ids TEXT,latency_ms INTEGER,outcome TEXT,http_status INTEGER,error_code TEXT,provider_attempts INTEGER,input_tokens INTEGER,cached_input_tokens INTEGER,cache_write_tokens INTEGER,output_tokens INTEGER,reasoning_tokens INTEGER,visible_output_tokens INTEGER,total_tokens INTEGER,estimated_cost REAL);
    CREATE TABLE IF NOT EXISTS attempts(request_id TEXT NOT NULL,attempt_number INTEGER NOT NULL,openai_response_id TEXT,provider_request_id TEXT,timestamp_utc TEXT,latency_ms INTEGER,model TEXT,reasoning TEXT,file_search_enabled INTEGER,file_search_calls INTEGER,input_tokens INTEGER,cached_input_tokens INTEGER,cache_write_tokens INTEGER,output_tokens INTEGER,reasoning_tokens INTEGER,visible_output_tokens INTEGER,total_tokens INTEGER,model_cost REAL,file_search_cost REAL,combined_cost REAL,pricing_revision TEXT,input_rate REAL,cached_input_rate REAL,cache_write_rate REAL,output_rate REAL,file_search_rate REAL,outcome TEXT,http_status INTEGER,error_code TEXT,context_fallback INTEGER,retrieval_fallback INTEGER,PRIMARY KEY(request_id,attempt_number));
    CREATE INDEX IF NOT EXISTS ix_requests_timestamp ON requests(started_utc); CREATE INDEX IF NOT EXISTS ix_requests_report ON requests(report_id); CREATE INDEX IF NOT EXISTS ix_requests_operator ON requests(operator_code_id); CREATE INDEX IF NOT EXISTS ix_requests_model ON requests(effective_model); CREATE INDEX IF NOT EXISTS ix_requests_outcome ON requests(outcome); CREATE INDEX IF NOT EXISTS ix_attempts_request ON attempts(request_id);
    """;
}

public sealed class InterpretationUsageRequest
{
    public string RequestId="", TraceId="", ReportId="", AnalysisIds="", GenerationProfile="", EffectiveModel="", EffectiveReasoning="", RequestVersion="", ResponseVersion="", PackageVersion="", PromptVersion="", OutputVersion="", KnowledgeBaseIds="", Outcome="";
    public string? OperatorCodeId, RequestedPreset, EffectivePreset, AccessTier, PresetRevision, RequestedModel, RequestedReasoning, ErrorCode; public DateTime StartedUtc, CompletedUtc; public long RequestBytes, LatencyMs; public int HttpStatus, ProviderAttempts; public int? InputTokens,CachedInputTokens,CacheWriteTokens,OutputTokens,ReasoningTokens,VisibleOutputTokens,TotalTokens; public decimal? EstimatedCost;
}

public sealed class InterpretationUsageAttempt
{
    public string RequestId="", Model="", ReasoningEffort="", Outcome=""; public string? OpenAIResponseId, ProviderRequestId, PricingRevision, ErrorCode;
    public int AttemptNumber; public int? HttpStatus; public DateTime TimestampUtc; public long LatencyMs; public bool FileSearchEnabled, ContextFallback, RetrievalFallback;
    public int? FileSearchCalls, InputTokens, CachedInputTokens, CacheWriteTokens, OutputTokens, ReasoningTokens, VisibleOutputTokens, TotalTokens;
    public decimal? ModelCost, FileSearchCost, CombinedCost, InputRate, CachedInputRate, CacheWriteRate, OutputRate, FileSearchRate;
}

public readonly record struct InterpretationCost(decimal? Model = null, decimal? FileSearch = null, decimal? Combined = null,
    string? Revision = null, decimal? InputRate = null, decimal? CachedRate = null, decimal? CacheWriteRate = null,
    decimal? OutputRate = null, decimal? FileSearchRate = null);
public readonly record struct InterpretationUsageAggregate(int Attempts=0,int? Input=null,int? Cached=null,int? CacheWrite=null,int? Output=null,int? Reasoning=null,int? Visible=null,int? Total=null,decimal? Cost=null);
