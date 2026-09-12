namespace AnalysisITC.Web;

public sealed class InterpretationQuotaService
{
    readonly GenerationPresetRegistry presets;
    readonly OperatorCodeRegistry operators;
    readonly InterpretationUsageStore usage;

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
        if (account is null)
            throw new AccountingUnavailableException("The authenticated interpretation account could not be read.");
        if (account.QuotaUnlimited) return InterpretationQuotaStatus.Unlimited;
        var limit = account.MonthlyQuotaUsdOverride ?? policy.MonthlyUsd;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var since = configuration.QuotaAccountingStartedAtUtc > monthStart ? configuration.QuotaAccountingStartedAtUtc : monthStart;
        var reset = monthStart.AddMonths(1);
        var accountUsage = this.usage.GetOperatorUsage(operatorCodeId, since);
        // An explicit administrative waiver permits quota use while retaining
        // the unknown actual cost in accounting reports.
        if (accountUsage.UnresolvedCount > 0)
            throw new AccountingUnavailableException("Interpretation accounting needs reconciliation.");
        var spent = accountUsage.KnownCost;
        var percent = limit <= 0 ? 0 : (int)Math.Clamp(Math.Floor((limit - spent) / limit * 100m), 0m, 100m);
        return new(true, spent < limit, percent, reset, limit, spent);
    }

    /// <summary>Returns the policy inputs used by durable admission.</summary>
    public InterpretationQuotaAdmissionPolicy GetAdmissionPolicy(InterpretationGenerationSelection selection, DateTime? nowUtc = null)
    {
        var now = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
        if (selection.EffectivePreset is "instant" or "summary" || selection.OperatorCodeId is null)
            return new(false, null, now);
        var configuration = presets.Read();
        var policy = configuration.Quotas.SingleOrDefault(item => item.AccessTier == selection.AccessTier);
        var account = operators.List().SingleOrDefault(item => item.Id == selection.OperatorCodeId);
        if (account is null)
            return new(false, null, now, false);
        if (policy is null || account.QuotaUnlimited)
            return new(false, null, now);
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var since = configuration.QuotaAccountingStartedAtUtc > monthStart ? configuration.QuotaAccountingStartedAtUtc : monthStart;
        return new(true, account.MonthlyQuotaUsdOverride ?? policy.MonthlyUsd, since);
    }

}

public readonly record struct InterpretationQuotaStatus(bool IsLimited, bool IsAvailable, int RemainingPercent,
    DateTime ResetsAtUtc, decimal LimitUsd, decimal SpentUsd)
{
    public static InterpretationQuotaStatus Unlimited => new(false, true, 100, default, 0, 0);
}

public readonly record struct InterpretationQuotaAdmissionPolicy(bool IsLimited, decimal? LimitUsd, DateTime SinceUtc,
    bool IsAvailable = true);
