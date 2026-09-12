using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

/// <summary>Durable accounting ledger for hosted interpretation executions.</summary>
public sealed partial class InterpretationUsageStore
{
    readonly InterpretationOptions options;
    readonly ILogger<InterpretationUsageStore> logger;
    readonly object initializeLock = new();
    bool initialized;

    /// <summary>Test seam for deterministic required-write failures; never configured by production code.</summary>
    internal Func<string, Exception?>? FaultInjector { get; set; }

    public InterpretationUsageStore(IOptions<InterpretationOptions> options, ILogger<InterpretationUsageStore> logger)
    { this.options = options.Value; this.logger = logger; }

    public bool IsEnabled => options.UsageLog.Enabled;

    public InterpretationAdmissionResult TryAdmit(InterpretationUsageRequest request, decimal? quotaLimitUsd, DateTime sinceUtc)
    {
        if (!IsEnabled) return InterpretationAdmissionResult.Unavailable(request.ServerExecutionIdOrRequestId, "usage_log_disabled");
        try
        {
            EnsureInitialized();
            InjectFault("admission");
            using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
            var execution = request.ServerExecutionIdOrRequestId;
            if (string.IsNullOrWhiteSpace(execution)) throw new ArgumentException("A server execution ID is required.", nameof(request));
            if (!string.IsNullOrWhiteSpace(request.OperatorCodeId) && string.IsNullOrWhiteSpace(request.ClientRequestId))
                throw new ArgumentException("Authenticated admission requires a client request ID.", nameof(request));
            if (!string.IsNullOrWhiteSpace(request.OperatorCodeId) && !string.IsNullOrWhiteSpace(request.ClientRequestId))
            {
                using var claim = Cmd(db, tx, "SELECT execution_id FROM submission_claims WHERE operator_code_id=$a AND client_request_id=$c");
                Add(claim,"$a",request.OperatorCodeId); Add(claim,"$c",request.ClientRequestId);
                var existing = claim.ExecuteScalar() as string;
                if (existing is not null) { tx.Commit(); return InterpretationAdmissionResult.Duplicate(execution, existing); }
            }
            if (MigrationActivationBlocked(db, tx)) { tx.Commit(); return InterpretationAdmissionResult.Unavailable(execution, "migration_activation_blocked"); }
            var gate = ReadMaintenance(db, tx);
            if (gate.Active) { tx.Commit(); return InterpretationAdmissionResult.Busy(execution, "maintenance"); }
            if (!string.IsNullOrWhiteSpace(request.OperatorCodeId))
            {
                var usage = UsageInTransaction(db, tx, request.OperatorCodeId!, sinceUtc);
                if (quotaLimitUsd is not null)
                {
                    using var hold = Cmd(db, tx, "SELECT execution_id FROM account_holds WHERE operator_code_id=$a"); Add(hold,"$a",request.OperatorCodeId);
                    if (hold.ExecuteScalar() is string active && !string.Equals(active, execution, StringComparison.Ordinal))
                    {
                        using var activeState=Cmd(db,tx,"SELECT lifecycle FROM executions WHERE execution_id=$e");Add(activeState,"$e",active);var lifecycle=activeState.ExecuteScalar() as string;
                        tx.Commit(); return lifecycle=="admitted" ? InterpretationAdmissionResult.Busy(execution, active) : InterpretationAdmissionResult.Unresolved(execution);
                    }
                    if (usage.UnresolvedCount > 0) { tx.Commit(); return InterpretationAdmissionResult.Unresolved(execution); }
                    if (quotaLimitUsd is decimal limit && usage.KnownCost >= limit) { tx.Commit(); return InterpretationAdmissionResult.Exhausted(execution); }
                }
            }
            InsertExecution(db, tx, request, execution);
            if (!string.IsNullOrWhiteSpace(request.OperatorCodeId) && !string.IsNullOrWhiteSpace(request.ClientRequestId))
            {
                using var insert = Cmd(db, tx, "INSERT INTO submission_claims(operator_code_id,client_request_id,execution_id,claimed_utc) VALUES($a,$c,$e,$t)");
                Add(insert,"$a",request.OperatorCodeId); Add(insert,"$c",request.ClientRequestId); Add(insert,"$e",execution); Add(insert,"$t",Iso(DateTime.UtcNow)); insert.ExecuteNonQuery();
                if (quotaLimitUsd is not null) { using var h=Cmd(db,tx,"INSERT INTO account_holds(operator_code_id,execution_id,acquired_utc) VALUES($a,$e,$t)"); Add(h,"$a",request.OperatorCodeId); Add(h,"$e",execution); Add(h,"$t",Iso(DateTime.UtcNow)); h.ExecuteNonQuery(); }
            }
            tx.Commit(); return InterpretationAdmissionResult.Admitted(execution);
        }
        catch (SqliteException ex) { logger.LogError(ex,"Interpretation accounting admission failed."); return InterpretationAdmissionResult.Unavailable(request.ServerExecutionIdOrRequestId, "database_unavailable"); }
        catch (IOException ex) { logger.LogError(ex,"Interpretation accounting admission failed."); return InterpretationAdmissionResult.Unavailable(request.ServerExecutionIdOrRequestId, "database_unavailable"); }
    }

    public void BeginAttempt(string serverExecutionId, int attemptNumber)
    {
        EnsureEnabled();
        if (attemptNumber < 1) throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        using var db=Open(); using var tx=db.BeginTransaction();
        InjectFault("attempt_start");
        if (ReadMaintenance(db,tx).Active) throw new InvalidOperationException("Interpretation generation is paused for maintenance.");
        using var lifecycle=Cmd(db,tx,"SELECT lifecycle FROM executions WHERE execution_id=$e"); Add(lifecycle,"$e",serverExecutionId);
        if (!string.Equals(lifecycle.ExecuteScalar() as string,"admitted",StringComparison.Ordinal)) throw new InvalidOperationException("Execution is not admitted or has already been finalized.");
        using var count=Cmd(db,tx,"SELECT count(*) FROM attempt_starts WHERE execution_id=$e"); Add(count,"$e",serverExecutionId);
        var priorCount=Convert.ToInt32(count.ExecuteScalar(),CultureInfo.InvariantCulture);
        if (attemptNumber != priorCount + 1) throw new InvalidOperationException($"Attempt numbers must be contiguous; expected {priorCount + 1}.");
        if (attemptNumber > 1)
        {
            using var prior=Cmd(db,tx,"SELECT billing_state FROM effective_attempt_accounting WHERE execution_id=$e AND attempt_number=$n"); Add(prior,"$e",serverExecutionId); Add(prior,"$n",attemptNumber-1);
            var priorState=prior.ExecuteScalar() as string;
            if (!string.Equals(priorState,"known",StringComparison.Ordinal) && !string.Equals(priorState,"known_zero",StringComparison.Ordinal))
                throw new InvalidOperationException("The previous attempt must have resolved billing before another attempt starts.");
        }
        using var c=Cmd(db,tx,"INSERT INTO attempt_starts(execution_id,attempt_number,started_utc) VALUES($e,$n,$t)"); Add(c,"$e",serverExecutionId); Add(c,"$n",attemptNumber); Add(c,"$t",Iso(DateTime.UtcNow)); c.ExecuteNonQuery(); tx.Commit();
    }

    public void RecordAttempt(InterpretationUsageAttempt record)
    {
        EnsureEnabled(); EnsureInitialized(); var execution=record.ServerExecutionIdOrRequestId;
        if (string.IsNullOrWhiteSpace(execution)) throw new ArgumentException("A server execution ID is required.", nameof(record));
        if (record.AttemptNumber < 1) throw new ArgumentOutOfRangeException(nameof(record.AttemptNumber));
        using var db=Open(); using var tx=db.BeginTransaction();
        var state=record.BillingState ?? (record.CombinedCost is null ? "unresolved" : record.CombinedCost.Value == 0 ? "known_zero" : "known");
        if (state is not ("known" or "known_zero" or "unresolved")) throw new ArgumentException("Provider receipts may only be known, known_zero, or unresolved.", nameof(record));
        if (state == "unresolved" && record.CombinedCost is not null) throw new ArgumentException("Unresolved billing cannot include a cost.", nameof(record));
        if (state is "known" or "known_zero" && record.CombinedCost is null) throw new ArgumentException("Known billing requires a cost.", nameof(record));
        if (record.CombinedCost is decimal amount && (amount < 0 || state == "known_zero" && amount != 0)) throw new ArgumentException("Billing cost must be nonnegative and known_zero must be zero.", nameof(record));
        InjectFault("receipt");
        using var find=Cmd(db,tx,"SELECT billing_state,combined_cost,receipt_fingerprint FROM attempt_receipts WHERE execution_id=$e AND attempt_number=$n"); Add(find,"$e",execution); Add(find,"$n",record.AttemptNumber);
        using var reader=find.ExecuteReader();
        if (reader.Read())
        {
            var same=string.Equals(reader.GetString(0),state,StringComparison.Ordinal) && ((reader.IsDBNull(1)&&record.CombinedCost is null)||(!reader.IsDBNull(1)&&record.CombinedCost is decimal cost&&reader.GetDecimal(1)==cost));
            var fingerprint=reader.IsDBNull(2)?null:reader.GetString(2); reader.Close();
            if (!same || !string.Equals(fingerprint,Fingerprint(record),StringComparison.Ordinal)) throw new AccountingConflictException(execution,record.AttemptNumber);
            tx.Commit(); return;
        }
        reader.Close();
        using (var exists=Cmd(db,tx,"SELECT e.lifecycle FROM executions e WHERE e.execution_id=$e AND e.lifecycle='admitted' AND EXISTS(SELECT 1 FROM attempt_starts s WHERE s.execution_id=$e AND s.attempt_number=$n)")){Add(exists,"$e",execution);Add(exists,"$n",record.AttemptNumber);if(exists.ExecuteScalar() is not string)throw new InvalidOperationException("An admitted execution and durable attempt start are required before recording its receipt.");}
        using var insert=Cmd(db,tx,"INSERT INTO attempt_receipts(execution_id,attempt_number,billing_state,combined_cost,receipt_fingerprint,recorded_utc,task_type,guidance_variant,guidance_revision,model,reasoning,file_search_enabled,file_search_calls,latency_ms,model_cost,file_search_cost,pricing_revision,input_rate,cached_input_rate,cache_write_rate,output_rate,file_search_rate,context_fallback,retrieval_fallback,http_status,provider_request_id,openai_response_id,outcome,error_code,input_tokens,cached_input_tokens,cache_write_tokens,output_tokens,reasoning_tokens,visible_output_tokens,total_tokens) VALUES($e,$n,$s,$cost,$f,$t,$task,$gv,$gr,$m,$re,$fs,$fc,$lat,$mc,$sc,$pr,$ir,$cr,$wr,$or,$fr,$cf,$rf,$h,$p,$o,$u,$x,$i,$ci,$cw,$q,$rt,$v,$z)");
        Add(insert,"$e",execution); Add(insert,"$n",record.AttemptNumber); Add(insert,"$s",state); Add(insert,"$cost",record.CombinedCost); Add(insert,"$f",Fingerprint(record)); Add(insert,"$t",Iso(record.TimestampUtc)); Add(insert,"$task",record.TaskType); Add(insert,"$gv",record.GuidanceVariant); Add(insert,"$gr",record.GuidanceRevision); Add(insert,"$m",record.Model); Add(insert,"$re",record.ReasoningEffort); Add(insert,"$fs",record.FileSearchEnabled?1:0); Add(insert,"$fc",record.FileSearchCalls); Add(insert,"$lat",record.LatencyMs); Add(insert,"$mc",record.ModelCost); Add(insert,"$sc",record.FileSearchCost); Add(insert,"$pr",record.PricingRevision); Add(insert,"$ir",record.InputRate); Add(insert,"$cr",record.CachedInputRate); Add(insert,"$wr",record.CacheWriteRate); Add(insert,"$or",record.OutputRate); Add(insert,"$fr",record.FileSearchRate); Add(insert,"$cf",record.ContextFallback?1:0); Add(insert,"$rf",record.RetrievalFallback?1:0); Add(insert,"$h",record.HttpStatus); Add(insert,"$p",record.ProviderRequestId); Add(insert,"$o",record.OpenAIResponseId); Add(insert,"$u",record.Outcome); Add(insert,"$x",record.ErrorCode); Add(insert,"$i",record.InputTokens); Add(insert,"$ci",record.CachedInputTokens); Add(insert,"$cw",record.CacheWriteTokens); Add(insert,"$q",record.OutputTokens); Add(insert,"$rt",record.ReasoningTokens); Add(insert,"$v",record.VisibleOutputTokens); Add(insert,"$z",record.TotalTokens); insert.ExecuteNonQuery();
        using var legacy=Cmd(db,tx,"INSERT OR IGNORE INTO attempts(request_id,attempt_number,task_type,guidance_variant,guidance_revision,openai_response_id,provider_request_id,timestamp_utc,latency_ms,model,reasoning,file_search_enabled,file_search_calls,input_tokens,cached_input_tokens,cache_write_tokens,output_tokens,reasoning_tokens,visible_output_tokens,total_tokens,model_cost,file_search_cost,combined_cost,pricing_revision,input_rate,cached_input_rate,cache_write_rate,output_rate,file_search_rate,outcome,http_status,error_code,context_fallback,retrieval_fallback) VALUES($e,$n,$task,$gv,$gr,$o,$p,$t,$lat,$m,$re,$fs,$fc,$i,$ci,$cw,$q,$rt,$v,$z,$mc,$sc,$c,$pr,$ir,$cr,$wr,$or,$fr,$u,$h,$x,$cf,$rf)"); Add(legacy,"$e",execution); Add(legacy,"$n",record.AttemptNumber); Add(legacy,"$task",record.TaskType); Add(legacy,"$gv",record.GuidanceVariant); Add(legacy,"$gr",record.GuidanceRevision); Add(legacy,"$o",record.OpenAIResponseId); Add(legacy,"$p",record.ProviderRequestId); Add(legacy,"$t",Iso(record.TimestampUtc)); Add(legacy,"$lat",record.LatencyMs); Add(legacy,"$m",record.Model); Add(legacy,"$re",record.ReasoningEffort); Add(legacy,"$fs",record.FileSearchEnabled?1:0); Add(legacy,"$fc",record.FileSearchCalls); Add(legacy,"$i",record.InputTokens); Add(legacy,"$ci",record.CachedInputTokens); Add(legacy,"$cw",record.CacheWriteTokens); Add(legacy,"$q",record.OutputTokens); Add(legacy,"$rt",record.ReasoningTokens); Add(legacy,"$v",record.VisibleOutputTokens); Add(legacy,"$z",record.TotalTokens); Add(legacy,"$mc",record.ModelCost); Add(legacy,"$sc",record.FileSearchCost); Add(legacy,"$c",record.CombinedCost); Add(legacy,"$pr",record.PricingRevision); Add(legacy,"$ir",record.InputRate); Add(legacy,"$cr",record.CachedInputRate); Add(legacy,"$wr",record.CacheWriteRate); Add(legacy,"$or",record.OutputRate); Add(legacy,"$fr",record.FileSearchRate); Add(legacy,"$u",record.Outcome); Add(legacy,"$h",record.HttpStatus); Add(legacy,"$x",record.ErrorCode); Add(legacy,"$cf",record.ContextFallback?1:0); Add(legacy,"$rf",record.RetrievalFallback?1:0); legacy.ExecuteNonQuery();
        tx.Commit();
    }

    public bool CanContinueExecution(string serverExecutionId)
    {
        EnsureEnabled();
        using var db = Open(); using var query = db.CreateCommand();
        query.CommandText = "SELECT 1 FROM executions e JOIN execution_accounting a ON a.execution_id=e.execution_id WHERE e.execution_id=$id AND e.lifecycle='admitted' AND a.attempt_count>0 AND a.unresolved_cost_count=0 AND a.waived_unknown_count=0";
        Add(query,"$id",serverExecutionId);
        return query.ExecuteScalar() is not null;
    }
    public InterpretationExecutionAccounting ReadExecutionAccounting(string serverExecutionId)
    {
        EnsureEnabled(); EnsureInitialized(); using var db=Open(); using var c=db.CreateCommand(); c.CommandText="SELECT known_cost,unresolved_cost_count,waived_unknown_count,attempt_count FROM execution_accounting WHERE execution_id=$e"; Add(c,"$e",serverExecutionId); using var r=c.ExecuteReader(); if(!r.Read()) return new(serverExecutionId,0,0,0,0); return new(serverExecutionId,r.GetDecimal(0),r.GetInt32(1),r.GetInt32(2),r.GetInt32(3));
    }

    public void RecordRequest(InterpretationUsageRequest record) => FinalizeRequest(record);
    public void FinalizeRequest(InterpretationUsageRequest record)
    {
        EnsureEnabled(); EnsureInitialized(); var execution=record.ServerExecutionIdOrRequestId;
        using var db=Open(); using var tx=db.BeginTransaction();
        InjectFault("finalize");
        using (var owner=Cmd(db,tx,"SELECT client_request_id,operator_code_id,lifecycle FROM executions WHERE execution_id=$e"))
        {
            Add(owner,"$e",execution); using var row=owner.ExecuteReader();
            if(!row.Read()) throw new InvalidOperationException("The execution was not durably admitted.");
            var client=row.IsDBNull(0)?null:row.GetString(0); var account=row.IsDBNull(1)?null:row.GetString(1); var lifecycle=row.IsDBNull(2)?null:row.GetString(2);
            if(!string.Equals(client,record.ClientRequestId,StringComparison.Ordinal) || !string.Equals(account,record.OperatorCodeId,StringComparison.Ordinal)) throw new AccountingConflictException(execution,0);
            if(lifecycle is not "admitted") throw new InvalidOperationException("The execution is already terminal.");
        }
        InsertRequest(db,tx,record);
        using var accounting=Cmd(db,tx,"SELECT unresolved_cost_count FROM execution_accounting WHERE execution_id=$e"); Add(accounting,"$e",execution); var unresolved=Convert.ToInt32(accounting.ExecuteScalar()??0,CultureInfo.InvariantCulture);
        var completed=record.CompletedUtc==default?DateTime.UtcNow:record.CompletedUtc;
        using var terminal=Cmd(db,tx,"UPDATE executions SET lifecycle='finalized',completed_utc=$t WHERE execution_id=$e AND lifecycle='admitted'"); Add(terminal,"$t",Iso(completed)); Add(terminal,"$e",execution); terminal.ExecuteNonQuery();
        if(unresolved==0){using var hold=Cmd(db,tx,"DELETE FROM account_holds WHERE execution_id=$e");Add(hold,"$e",execution);hold.ExecuteNonQuery();}
        tx.Commit();
    }
    public void RecordRejectedRequest(InterpretationUsageRequest record)
    {
        EnsureEnabled(); EnsureInitialized(); using var db=Open(); using var tx=db.BeginTransaction();
        using(var existing=Cmd(db,tx,"SELECT count(*) FROM executions WHERE execution_id=$e")){Add(existing,"$e",record.ServerExecutionIdOrRequestId);if(Convert.ToInt32(existing.ExecuteScalar(),CultureInfo.InvariantCulture)!=0)throw new AccountingConflictException(record.ServerExecutionIdOrRequestId,0);}
        using var execution=Cmd(db,tx,"INSERT INTO executions(execution_id,client_request_id,operator_code_id,task_type,effective_preset,started_utc,lifecycle,completed_utc,trace_id) VALUES($e,$c,$a,$t,$p,$s,'rejected',$d,$trace)");
        Add(execution,"$e",record.ServerExecutionIdOrRequestId); Add(execution,"$c",record.ClientRequestId); Add(execution,"$a",record.OperatorCodeId); Add(execution,"$t",record.TaskType); Add(execution,"$p",record.EffectivePreset); Add(execution,"$s",Iso(record.StartedUtc)); Add(execution,"$d",Iso(record.CompletedUtc==default?DateTime.UtcNow:record.CompletedUtc)); Add(execution,"$trace",record.TraceId); execution.ExecuteNonQuery();
        InsertRequest(db,tx,record); tx.Commit();
    }

    public InterpretationOperatorUsage GetOperatorUsage(string operatorCodeId, DateTime sinceUtc)
    {
        EnsureEnabled(); try { EnsureInitialized(); if(MigrationActivationBlocked()) throw new AccountingUnavailableException("Interpretation accounting has unresolved migration blockers."); using var db=Open(); using var c=db.CreateCommand(); c.CommandText="SELECT coalesce(sum(CASE WHEN started_utc >= $s THEN known_cost ELSE 0 END),0),coalesce(sum(unresolved_cost_count),0),coalesce(sum(waived_unknown_count),0) FROM execution_usage WHERE operator_code_id=$a AND coalesce(effective_preset,'') <> 'instant' AND coalesce(task_type,'interpretation') <> 'summary'"; Add(c,"$a",operatorCodeId); Add(c,"$s",Iso(sinceUtc)); using var r=c.ExecuteReader(); r.Read(); return new(operatorCodeId,r.GetDecimal(0),r.GetInt32(1),r.GetInt32(2)); } catch(Exception ex) when(ex is SqliteException or IOException) { throw new AccountingUnavailableException("Unable to read interpretation accounting.",ex); }
    }

    public InterpretationAccountUsageSnapshot GetAccountSnapshot(string operatorCodeId)
    {
        try
        {
            EnsureEnabled();
            using var db = Open();
            using var count = db.CreateCommand();
            count.CommandText = "SELECT count(*) FROM execution_usage WHERE operator_code_id=$account";
            Add(count, "$account", operatorCodeId);
            var total = Convert.ToInt32(count.ExecuteScalar(), CultureInfo.InvariantCulture);
            using var latest = db.CreateCommand();
            latest.CommandText = "SELECT started_utc,completed_utc,outcome,http_status FROM execution_usage WHERE operator_code_id=$account ORDER BY started_utc DESC,request_id DESC LIMIT 1";
            Add(latest, "$account", operatorCodeId);
            using var row = latest.ExecuteReader();
            if (!row.Read()) return new(total, null, null, null, null);
            return new(total, ParseUtc(row.IsDBNull(0) ? null : row.GetString(0)),
                ParseUtc(row.IsDBNull(1) ? null : row.GetString(1)),
                row.IsDBNull(2) ? null : row.GetString(2), row.IsDBNull(3) ? null : row.GetInt32(3));
        }
        catch (Exception ex) when (ex is SqliteException or IOException)
        { throw new AccountingUnavailableException("Unable to read interpretation account history.", ex); }
    }
    public InterpretationUsageAggregate Aggregate(string requestId) { EnsureEnabled(); EnsureInitialized(); using var db=Open(); var execution=requestId; using(var resolve=db.CreateCommand()){resolve.CommandText="SELECT execution_id FROM executions WHERE execution_id=$id LIMIT 1";Add(resolve,"$id",requestId);execution=resolve.ExecuteScalar() as string??requestId;} using var c=db.CreateCommand(); c.CommandText="SELECT count(*),sum(a.input_tokens),sum(a.cached_input_tokens),sum(a.cache_write_tokens),sum(a.output_tokens),sum(a.reasoning_tokens),sum(a.visible_output_tokens),sum(a.total_tokens) FROM attempt_starts s LEFT JOIN attempts a ON a.request_id=s.execution_id AND a.attempt_number=s.attempt_number WHERE s.execution_id=$id"; Add(c,"$id",execution); int attempts; int? input,cached,write,output,reasoning,visible,total; using(var r=c.ExecuteReader()){r.Read();attempts=r.GetInt32(0);input=I(r,1);cached=I(r,2);write=I(r,3);output=I(r,4);reasoning=I(r,5);visible=I(r,6);total=I(r,7);} var cost=(decimal?)null; if(attempts>0){using var accounting=db.CreateCommand();accounting.CommandText="SELECT known_cost,unresolved_cost_count,waived_unknown_count FROM execution_accounting WHERE execution_id=$id";Add(accounting,"$id",execution);using var ar=accounting.ExecuteReader();if(ar.Read()&&ar.GetInt32(1)==0&&ar.GetInt32(2)==0)cost=ar.GetDecimal(0);} return new(attempts,input,cached,write,output,reasoning,visible,total,cost); }


    public SqliteConnection OpenForCommand() => Open();
    public InterpretationCost Estimate(string model,int? input,int? cached,int? cacheWrite,int? output,int? fileSearchCalls) { if(!options.Pricing.TryGetValue(model,out var p)||input is null||output is null)return new(); var longCtx=p.LongContextThreshold is long t&&input>t; var i=longCtx?p.LongInputPerMillion??p.InputPerMillion:p.InputPerMillion; var ca=longCtx?p.LongCachedInputPerMillion??p.CachedInputPerMillion:p.CachedInputPerMillion; var w=longCtx?p.LongCacheWritePerMillion??p.CacheWritePerMillion:p.CacheWritePerMillion; var o=longCtx?p.LongOutputPerMillion??p.OutputPerMillion:p.OutputPerMillion; var cachedCount=cached??0; var writeCount=cacheWrite??0; var uncached=Math.Max(0,input.Value-cachedCount-writeCount); var modelCost=(uncached*i+cachedCount*ca+writeCount*w+output.Value*o)/1_000_000m; var search=(fileSearchCalls??0)*p.FileSearchPerCall; return new(modelCost,search,modelCost+search,p.Revision,i,ca,w,o,p.FileSearchPerCall); }

    void EnsureEnabled(){if(!IsEnabled)throw new AccountingUnavailableException("Interpretation accounting is disabled.");}
    void InjectFault(string phase){if(FaultInjector?.Invoke(phase) is Exception failure)throw failure;}
    SqliteConnection Open(){EnsureInitialized(); var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=options.UsageLog.DatabasePath}.ToString()); c.Open(); using var p=c.CreateCommand(); p.CommandText="PRAGMA busy_timeout=5000"; p.ExecuteNonQuery(); return c;}
    void EnsureInitialized()
    {
        if (initialized || !IsEnabled) return;
        lock (initializeLock)
        {
            if (initialized) return;
            var path = options.UsageLog.DatabasePath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            db.Open();
            using var busy = db.CreateCommand();
            busy.CommandText = "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL;";
            busy.ExecuteNonQuery();
            using var tx = db.BeginTransaction(deferred: false);
            using (var schema = Cmd(db, tx, Schema)) schema.ExecuteNonQuery();
            // CREATE TABLE IF NOT EXISTS does not upgrade older tables. Add
            // nullable metadata before reading historical rows or building views.
            EnsureHistoricalColumns(db, tx);
            EnsureColumn(db, tx, "executions", "trace_id", "TEXT");
            foreach (var column in ReceiptColumns)
                EnsureColumn(db, tx, "attempt_receipts", column.Name, column.Type);
            MigrateLegacyTransactional(db, tx);
            RebuildAccountingViews(db, tx);
            tx.Commit();
            initialized = true;
        }
    }

    static void EnsureHistoricalColumns(SqliteConnection db, SqliteTransaction tx)
    {
        void Columns(string table, string type, string names)
        {
            foreach (var name in names.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                EnsureColumn(db, tx, table, name, type);
        }
        Columns("requests", "TEXT", "task_type trace_id started_utc completed_utc authenticated_user_id operator_code_id report_id analysis_ids generation_profile requested_preset effective_preset access_tier preset_revision requested_model requested_reasoning effective_model effective_reasoning requested_guidance_variant effective_guidance_variant guidance_revision request_version response_version package_version prompt_version output_version knowledge_base_ids outcome error_code client_request_id execution_id");
        Columns("requests", "INTEGER", "request_bytes latency_ms http_status provider_attempts input_tokens cached_input_tokens cache_write_tokens output_tokens reasoning_tokens visible_output_tokens total_tokens");
        Columns("requests", "REAL", "estimated_cost");
        Columns("attempts", "TEXT", "openai_response_id provider_request_id timestamp_utc outcome error_code");
        Columns("attempts", "INTEGER", "input_tokens output_tokens total_tokens");
        Columns("attempts", "REAL", "combined_cost");
        foreach (var column in ReceiptColumns) EnsureColumn(db, tx, "attempts", column.Name, column.Type);
    }
    static SqliteCommand Cmd(SqliteConnection db,SqliteTransaction? tx,string sql){var c=db.CreateCommand();c.CommandText=sql;c.Transaction=tx;return c;}
    static void EnsureColumn(SqliteConnection db,SqliteTransaction tx,string table,string column,string type){using var q=Cmd(db,tx,$"SELECT count(*) FROM pragma_table_info('{table}')");q.CommandText=$"SELECT count(*) FROM pragma_table_info('{table}') WHERE name=$n";Add(q,"$n",column);if(Convert.ToInt32(q.ExecuteScalar(),CultureInfo.InvariantCulture)>0)return;using var a=Cmd(db,tx,$"ALTER TABLE {table} ADD COLUMN {column} {type}");a.ExecuteNonQuery();}
    static void Add(SqliteCommand c,string n,object? v)=>c.Parameters.AddWithValue(n,v??DBNull.Value); static string Iso(DateTime x)=>x.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture); static DateTime? ParseUtc(string? x)=>DateTime.TryParse(x,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var d)?d.ToUniversalTime():null; static int? I(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetInt32(i); static decimal? D(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetDecimal(i);
    static string Fingerprint(InterpretationUsageAttempt receipt)
    {
        // Hash an unambiguous representation of the provider receipt. Local
        // observation time and alias spelling do not change a received charge.
        var value = JsonSerializer.SerializeToNode(receipt, new JsonSerializerOptions { IncludeFields = true })!.AsObject();
        value.Remove(nameof(receipt.ServerExecutionId));
        value.Remove(nameof(receipt.RequestId));
        value.Remove(nameof(receipt.TimestampUtc));
        value.Remove(nameof(receipt.LatencyMs));
        value[nameof(receipt.BillingState)] = receipt.BillingState
            ?? (receipt.CombinedCost is null ? "unresolved" : receipt.CombinedCost == 0 ? "known_zero" : "known");
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    }
    static readonly (string Name,string Type)[] ReceiptColumns = new[]{("task_type","TEXT"),("guidance_variant","TEXT"),("guidance_revision","TEXT"),("model","TEXT"),("reasoning","TEXT"),("file_search_enabled","INTEGER"),("file_search_calls","INTEGER"),("latency_ms","INTEGER"),("model_cost","REAL"),("file_search_cost","REAL"),("pricing_revision","TEXT"),("input_rate","REAL"),("cached_input_rate","REAL"),("cache_write_rate","REAL"),("output_rate","REAL"),("file_search_rate","REAL"),("context_fallback","INTEGER"),("retrieval_fallback","INTEGER"),("http_status","INTEGER"),("cached_input_tokens","INTEGER"),("cache_write_tokens","INTEGER"),("reasoning_tokens","INTEGER"),("visible_output_tokens","INTEGER")};
    void InsertExecution(SqliteConnection db,SqliteTransaction tx,InterpretationUsageRequest r,string id){using var c=Cmd(db,tx,"INSERT INTO executions(execution_id,client_request_id,operator_code_id,task_type,effective_preset,started_utc,lifecycle,trace_id) VALUES($e,$c,$a,$t,$p,$s,'admitted',$trace)");Add(c,"$e",id);Add(c,"$c",r.ClientRequestId);Add(c,"$a",r.OperatorCodeId);Add(c,"$t",r.TaskType);Add(c,"$p",r.EffectivePreset);Add(c,"$s",Iso(r.StartedUtc==default?DateTime.UtcNow:r.StartedUtc));Add(c,"$trace",r.TraceId);c.ExecuteNonQuery();}
    void InsertRequest(SqliteConnection db,SqliteTransaction tx,InterpretationUsageRequest r){using var c=Cmd(db,tx,"INSERT OR IGNORE INTO requests(request_id,task_type,trace_id,started_utc,completed_utc,operator_code_id,effective_preset,outcome,http_status,error_code,provider_attempts,input_tokens,cached_input_tokens,cache_write_tokens,output_tokens,reasoning_tokens,visible_output_tokens,total_tokens,estimated_cost,client_request_id,execution_id) VALUES($id,$t,$x,$s,$e,$a,$p,$o,$h,$q,$n,$i,$ci,$cw,$ou,$re,$v,$tt,NULL,$c,$sid)");Add(c,"$id",r.ServerExecutionIdOrRequestId);Add(c,"$t",r.TaskType);Add(c,"$x",r.TraceId);Add(c,"$s",Iso(r.StartedUtc));Add(c,"$e",Iso(r.CompletedUtc));Add(c,"$a",r.OperatorCodeId);Add(c,"$p",r.EffectivePreset);Add(c,"$o",r.Outcome);Add(c,"$h",r.HttpStatus);Add(c,"$q",r.ErrorCode);Add(c,"$n",r.ProviderAttempts);Add(c,"$i",r.InputTokens);Add(c,"$ci",r.CachedInputTokens);Add(c,"$cw",r.CacheWriteTokens);Add(c,"$ou",r.OutputTokens);Add(c,"$re",r.ReasoningTokens);Add(c,"$v",r.VisibleOutputTokens);Add(c,"$tt",r.TotalTokens);Add(c,"$c",r.ClientRequestId);Add(c,"$sid",r.ServerExecutionIdOrRequestId);c.ExecuteNonQuery();using var u=Cmd(db,tx,"UPDATE requests SET report_id=$r,analysis_ids=$an,request_bytes=$b,generation_profile=$g,requested_preset=$rp,access_tier=$tier,preset_revision=$pr,requested_model=$rm,requested_reasoning=$rr,effective_model=$em,effective_reasoning=$er,requested_guidance_variant=$rg,effective_guidance_variant=$eg,guidance_revision=$gr,request_version=$rv,response_version=$sv,package_version=$pv,prompt_version=$pp,output_version=$ov,knowledge_base_ids=$k,latency_ms=$l WHERE request_id=$id");Add(u,"$r",r.ReportId);Add(u,"$an",r.AnalysisIds);Add(u,"$b",r.RequestBytes);Add(u,"$g",r.GenerationProfile);Add(u,"$rp",r.RequestedPreset);Add(u,"$tier",r.AccessTier);Add(u,"$pr",r.PresetRevision);Add(u,"$rm",r.RequestedModel);Add(u,"$rr",r.RequestedReasoning);Add(u,"$em",r.EffectiveModel);Add(u,"$er",r.EffectiveReasoning);Add(u,"$rg",r.RequestedGuidanceVariant);Add(u,"$eg",r.EffectiveGuidanceVariant);Add(u,"$gr",r.GuidanceRevision);Add(u,"$rv",r.RequestVersion);Add(u,"$sv",r.ResponseVersion);Add(u,"$pv",r.PackageVersion);Add(u,"$pp",r.PromptVersion);Add(u,"$ov",r.OutputVersion);Add(u,"$k",r.KnowledgeBaseIds);Add(u,"$l",r.LatencyMs);Add(u,"$id",r.ServerExecutionIdOrRequestId);u.ExecuteNonQuery();}
    static MaintenanceStatus ReadMaintenance(SqliteConnection db,SqliteTransaction? tx){using var c=Cmd(db,tx,"SELECT active,reason,changed_utc FROM maintenance_gate WHERE id=1");using var r=c.ExecuteReader();if(!r.Read())return new(false,null,null);return new(r.GetInt32(0)!=0,r.IsDBNull(1)?null:r.GetString(1),r.IsDBNull(2)?null:ParseUtc(r.GetString(2)));}
    InterpretationOperatorUsage UsageInTransaction(SqliteConnection db,SqliteTransaction tx,string a,DateTime s){using var c=Cmd(db,tx,"SELECT coalesce(sum(CASE WHEN started_utc >= $s THEN known_cost ELSE 0 END),0),coalesce(sum(unresolved_cost_count),0),coalesce(sum(waived_unknown_count),0) FROM execution_usage WHERE operator_code_id=$a AND coalesce(effective_preset,'') <> 'instant' AND coalesce(task_type,'interpretation') <> 'summary'");Add(c,"$a",a);Add(c,"$s",Iso(s));using var r=c.ExecuteReader();r.Read();return new(a,r.GetDecimal(0),r.GetInt32(1),r.GetInt32(2));}
    const string Schema="""
PRAGMA busy_timeout=5000;
CREATE TABLE IF NOT EXISTS schema_info(version INTEGER NOT NULL); INSERT INTO schema_info(version) SELECT 6 WHERE NOT EXISTS(SELECT 1 FROM schema_info);
CREATE TABLE IF NOT EXISTS requests(request_id TEXT PRIMARY KEY,task_type TEXT,trace_id TEXT,started_utc TEXT,completed_utc TEXT,authenticated_user_id TEXT,operator_code_id TEXT,report_id TEXT,analysis_ids TEXT,request_bytes INTEGER,generation_profile TEXT,requested_preset TEXT,effective_preset TEXT,access_tier TEXT,preset_revision TEXT,requested_model TEXT,requested_reasoning TEXT,effective_model TEXT,effective_reasoning TEXT,requested_guidance_variant TEXT,effective_guidance_variant TEXT,guidance_revision TEXT,request_version TEXT,response_version TEXT,package_version TEXT,prompt_version TEXT,output_version TEXT,knowledge_base_ids TEXT,latency_ms INTEGER,outcome TEXT,http_status INTEGER,error_code TEXT,provider_attempts INTEGER,input_tokens INTEGER,cached_input_tokens INTEGER,cache_write_tokens INTEGER,output_tokens INTEGER,reasoning_tokens INTEGER,visible_output_tokens INTEGER,total_tokens INTEGER,estimated_cost REAL,client_request_id TEXT,execution_id TEXT);
CREATE TABLE IF NOT EXISTS attempts(request_id TEXT NOT NULL,attempt_number INTEGER NOT NULL,task_type TEXT,guidance_variant TEXT,guidance_revision TEXT,openai_response_id TEXT,provider_request_id TEXT,timestamp_utc TEXT,latency_ms INTEGER,model TEXT,reasoning TEXT,file_search_enabled INTEGER,file_search_calls INTEGER,input_tokens INTEGER,cached_input_tokens INTEGER,cache_write_tokens INTEGER,output_tokens INTEGER,reasoning_tokens INTEGER,visible_output_tokens INTEGER,total_tokens INTEGER,model_cost REAL,file_search_cost REAL,combined_cost REAL,pricing_revision TEXT,input_rate REAL,cached_input_rate REAL,cache_write_rate REAL,output_rate REAL,file_search_rate REAL,outcome TEXT,http_status INTEGER,error_code TEXT,context_fallback INTEGER,retrieval_fallback INTEGER,PRIMARY KEY(request_id,attempt_number));
CREATE TABLE IF NOT EXISTS executions(execution_id TEXT PRIMARY KEY,client_request_id TEXT,operator_code_id TEXT,task_type TEXT,effective_preset TEXT,started_utc TEXT,lifecycle TEXT,completed_utc TEXT,trace_id TEXT);
CREATE TABLE IF NOT EXISTS submission_claims(operator_code_id TEXT NOT NULL,client_request_id TEXT NOT NULL,execution_id TEXT NOT NULL UNIQUE,claimed_utc TEXT NOT NULL,PRIMARY KEY(operator_code_id,client_request_id));
CREATE TABLE IF NOT EXISTS attempt_starts(execution_id TEXT NOT NULL,attempt_number INTEGER NOT NULL,started_utc TEXT NOT NULL,PRIMARY KEY(execution_id,attempt_number));
CREATE TABLE IF NOT EXISTS attempt_receipts(execution_id TEXT NOT NULL,attempt_number INTEGER NOT NULL,billing_state TEXT NOT NULL,combined_cost REAL,receipt_fingerprint TEXT NOT NULL,recorded_utc TEXT NOT NULL,task_type TEXT,guidance_variant TEXT,guidance_revision TEXT,model TEXT,reasoning TEXT,file_search_enabled INTEGER,file_search_calls INTEGER,latency_ms INTEGER,model_cost REAL,file_search_cost REAL,pricing_revision TEXT,input_rate REAL,cached_input_rate REAL,cache_write_rate REAL,output_rate REAL,file_search_rate REAL,context_fallback INTEGER,retrieval_fallback INTEGER,http_status INTEGER,provider_request_id TEXT,openai_response_id TEXT,outcome TEXT,error_code TEXT,input_tokens INTEGER,cached_input_tokens INTEGER,cache_write_tokens INTEGER,output_tokens INTEGER,reasoning_tokens INTEGER,visible_output_tokens INTEGER,total_tokens INTEGER,PRIMARY KEY(execution_id,attempt_number));
CREATE TABLE IF NOT EXISTS account_holds(operator_code_id TEXT PRIMARY KEY,execution_id TEXT NOT NULL UNIQUE,acquired_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS reconciliation_entries(id INTEGER PRIMARY KEY AUTOINCREMENT,execution_id TEXT NOT NULL,attempt_number INTEGER NOT NULL,billing_state TEXT NOT NULL,verified_cost REAL,evidence TEXT NOT NULL,waiver_reason TEXT,recorded_utc TEXT NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS ix_reconciliation_attempt ON reconciliation_entries(execution_id,attempt_number);
CREATE TABLE IF NOT EXISTS legacy_balances(execution_id TEXT PRIMARY KEY,operator_code_id TEXT,cost REAL NOT NULL,source TEXT NOT NULL,recorded_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS migration_execution_map(legacy_key TEXT PRIMARY KEY,execution_id TEXT NOT NULL UNIQUE,created_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS migration_issues(id INTEGER PRIMARY KEY AUTOINCREMENT,legacy_key TEXT NOT NULL,kind TEXT NOT NULL,details TEXT NOT NULL,blocking INTEGER NOT NULL DEFAULT 1,recorded_utc TEXT NOT NULL,UNIQUE(legacy_key,kind));
CREATE TABLE IF NOT EXISTS migration_state(name TEXT PRIMARY KEY,completed_utc TEXT NOT NULL,limitation_report TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS legacy_settlements(execution_id TEXT PRIMARY KEY,legacy_key TEXT NOT NULL UNIQUE,operator_code_id TEXT,confirmed_anonymous INTEGER NOT NULL,verified_started_utc TEXT,billing_state TEXT NOT NULL,verified_total_cost REAL,waiver_reason TEXT,evidence TEXT NOT NULL,recorded_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS maintenance_gate(id INTEGER PRIMARY KEY CHECK(id=1),active INTEGER NOT NULL DEFAULT 0,reason TEXT,changed_utc TEXT); INSERT OR IGNORE INTO maintenance_gate(id,active) VALUES(1,0);
CREATE INDEX IF NOT EXISTS ix_requests_timestamp ON requests(started_utc);
CREATE INDEX IF NOT EXISTS ix_requests_report ON requests(report_id);
CREATE INDEX IF NOT EXISTS ix_requests_operator ON requests(operator_code_id);
CREATE INDEX IF NOT EXISTS ix_requests_model ON requests(effective_model);
CREATE INDEX IF NOT EXISTS ix_requests_outcome ON requests(outcome);
CREATE INDEX IF NOT EXISTS ix_attempts_request ON attempts(request_id);
CREATE INDEX IF NOT EXISTS ix_attempt_receipts_execution ON attempt_receipts(execution_id);
""";
}

public enum InterpretationAdmissionStatus { Admitted, Duplicate, Busy, Exhausted, Unresolved, Unavailable }
public readonly record struct InterpretationAdmissionResult(InterpretationAdmissionStatus Status,string ServerExecutionId,string? ExistingExecutionId=null,string? Reason=null){public bool IsAdmitted=>Status==InterpretationAdmissionStatus.Admitted;public static InterpretationAdmissionResult Admitted(string id)=>new(InterpretationAdmissionStatus.Admitted,id);public static InterpretationAdmissionResult Duplicate(string id,string existing)=>new(InterpretationAdmissionStatus.Duplicate,id,existing,"duplicate");public static InterpretationAdmissionResult Busy(string id,string reason)=>new(InterpretationAdmissionStatus.Busy,id,null,reason);public static InterpretationAdmissionResult Exhausted(string id)=>new(InterpretationAdmissionStatus.Exhausted,id);public static InterpretationAdmissionResult Unresolved(string id)=>new(InterpretationAdmissionStatus.Unresolved,id);public static InterpretationAdmissionResult Unavailable(string id,string reason)=>new(InterpretationAdmissionStatus.Unavailable,id,null,reason);}
public sealed class AccountingUnavailableException(string message,Exception? inner=null):Exception(message,inner);
public sealed class AccountingConflictException(string executionId,int attempt):Exception($"Conflicting accounting receipt for {executionId}, attempt {attempt}.");
public readonly record struct InterpretationOperatorUsage(string OperatorCodeId,decimal KnownCost,int UnresolvedCount,int WaivedUnknownCount){public bool IsComplete=>UnresolvedCount==0 && WaivedUnknownCount==0;}
public readonly record struct InterpretationExecutionAccounting(string ExecutionId,decimal KnownCost,int UnresolvedCount,int WaivedUnknownCount,int AttemptCount){public bool IsComplete=>UnresolvedCount==0 && WaivedUnknownCount==0;}
public readonly record struct MaintenanceStatus(bool Active,string? Reason,DateTime? ChangedUtc);
public readonly record struct UnresolvedInterpretationAccounting(string ExecutionId,string? OperatorCodeId,string? ClientRequestId,int AttemptNumber,string BillingState,decimal? Cost);
public enum InterpretationAttemptBillingState { Known, KnownZero, Unresolved, WaivedUnknown }
public sealed class InterpretationUsageRequest
{
 public string RequestId="",ServerExecutionId="",ClientRequestId="",TaskType="interpretation",TraceId="",ReportId="",AnalysisIds="",GenerationProfile="",EffectiveModel="",EffectiveReasoning="",RequestVersion="",ResponseVersion="",PackageVersion="",PromptVersion="",OutputVersion="",KnowledgeBaseIds="",Outcome=""; public string? OperatorCodeId,RequestedPreset,EffectivePreset,AccessTier,PresetRevision,RequestedModel,RequestedReasoning,RequestedGuidanceVariant,EffectiveGuidanceVariant,GuidanceRevision,ErrorCode; public DateTime StartedUtc,CompletedUtc; public long RequestBytes,LatencyMs; public int HttpStatus,ProviderAttempts; public int? InputTokens,CachedInputTokens,CacheWriteTokens,OutputTokens,ReasoningTokens,VisibleOutputTokens,TotalTokens; public decimal? EstimatedCost; internal string ServerExecutionIdOrRequestId=>string.IsNullOrWhiteSpace(ServerExecutionId)?RequestId:ServerExecutionId;
}
public readonly record struct InterpretationAccountUsageSnapshot(int? TotalRequests=null,DateTime? MostRecentStartedAtUtc=null,DateTime? MostRecentCompletedAtUtc=null,string? MostRecentOutcome=null,int? MostRecentHttpStatus=null);
public sealed class InterpretationUsageAttempt
{
 public string RequestId="",ServerExecutionId="",TaskType="interpretation",Model="",ReasoningEffort="",Outcome=""; public string? GuidanceVariant,GuidanceRevision,OpenAIResponseId,ProviderRequestId,PricingRevision,ErrorCode; public int AttemptNumber; public DateTime TimestampUtc; public long LatencyMs; public bool FileSearchEnabled,ContextFallback,RetrievalFallback; public int? HttpStatus,FileSearchCalls,InputTokens,CachedInputTokens,CacheWriteTokens,OutputTokens,ReasoningTokens,VisibleOutputTokens,TotalTokens; public decimal? ModelCost,FileSearchCost,CombinedCost,InputRate,CachedInputRate,CacheWriteRate,OutputRate,FileSearchRate; public string? BillingState; internal string ServerExecutionIdOrRequestId=>string.IsNullOrWhiteSpace(ServerExecutionId)?RequestId:ServerExecutionId;
}
public readonly record struct InterpretationCost(decimal? Model=null,decimal? FileSearch=null,decimal? Combined=null,string? Revision=null,decimal? InputRate=null,decimal? CachedRate=null,decimal? CacheWriteRate=null,decimal? OutputRate=null,decimal? FileSearchRate=null);
public readonly record struct InterpretationUsageAggregate(int Attempts=0,int? Input=null,int? Cached=null,int? CacheWrite=null,int? Output=null,int? Reasoning=null,int? Visible=null,int? Total=null,decimal? Cost=null);
