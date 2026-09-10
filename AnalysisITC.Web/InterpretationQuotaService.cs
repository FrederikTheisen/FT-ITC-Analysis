using System.Collections.Concurrent;

namespace AnalysisITC.Web;

public sealed class InterpretationQuotaService
{
    readonly GenerationPresetRegistry presets;
    readonly OperatorCodeRegistry operators;
    readonly InterpretationUsageStore usage;
    readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new(StringComparer.Ordinal);

    public InterpretationQuotaService(GenerationPresetRegistry presets, OperatorCodeRegistry operators, InterpretationUsageStore usage)
    { this.presets = presets; this.operators = operators; this.usage = usage; }

    public InterpretationQuotaStatus GetStatus(string? operatorCodeId, string accessTier, string presetId, DateTime? nowUtc = null)
    {
        var now = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
        if (presetId is "instant" or "summary") return InterpretationQuotaStatus.Unlimited;
        if (operatorCodeId is null) return InterpretationQuotaStatus.Unlimited;
        var configuration = presets.Read();
        var policy = configuration.Quotas.SingleOrDefault(x => x.AccessTier == accessTier);
        if (policy is null) return InterpretationQuotaStatus.Unlimited;
        var account = operators.List().SingleOrDefault(x => x.Id == operatorCodeId);
        if (account?.QuotaUnlimited == true) return InterpretationQuotaStatus.Unlimited;
        var limit = account?.MonthlyQuotaUsdOverride ?? policy.MonthlyUsd;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var since = configuration.QuotaAccountingStartedAtUtc > monthStart ? configuration.QuotaAccountingStartedAtUtc : monthStart;
        var reset = monthStart.AddMonths(1);
        var spent = usage.CostForOperator(operatorCodeId, since);
        var percent = limit <= 0 ? 0 : (int)Math.Clamp(Math.Floor((limit - spent) / limit * 100m), 0m, 100m);
        return new(true, spent < limit, percent, reset, limit, spent);
    }

    public InterpretationQuotaLease TryAcquire(InterpretationGenerationSelection selection)
    {
        var status = GetStatus(selection.OperatorCodeId, selection.AccessTier, selection.EffectivePreset);
        if (!status.IsLimited) return new(null, status, true);
        var gate = gates.GetOrAdd(selection.OperatorCodeId!, _ => new SemaphoreSlim(1, 1));
        if (!gate.Wait(0)) return new(null, status, false);
        status = GetStatus(selection.OperatorCodeId, selection.AccessTier, selection.EffectivePreset);
        if (!status.IsAvailable) { gate.Release(); return new(null, status, true); }
        return new(gate, status, true);
    }
}

public readonly record struct InterpretationQuotaStatus(bool IsLimited, bool IsAvailable, int RemainingPercent,
    DateTime ResetsAtUtc, decimal LimitUsd, decimal SpentUsd)
{
    public static InterpretationQuotaStatus Unlimited => new(false, true, 100, default, 0, 0);
}

public sealed class InterpretationQuotaLease : IDisposable
{
    readonly SemaphoreSlim? gate;
    internal InterpretationQuotaLease(SemaphoreSlim? gate, InterpretationQuotaStatus status, bool acquired)
    { this.gate = gate; Status = status; Acquired = acquired; }
    public InterpretationQuotaStatus Status { get; }
    public bool Acquired { get; }
    public void Dispose() => gate?.Release();
}
