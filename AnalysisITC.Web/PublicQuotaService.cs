namespace AnalysisITC.Web;

public sealed class PublicQuotaService
{
    readonly GenerationPresetRegistry presets;
    readonly InterpretationUsageStore usage;
    public PublicQuotaService(GenerationPresetRegistry presets, InterpretationUsageStore usage) { this.presets = presets; this.usage = usage; }

    public PublicQuotaDecision Check(string? clientId, DateTime? nowUtc = null)
    {
        var now = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
        var config = presets.Read();
        var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var window = now.AddHours(-config.PublicRequestWindowHours);
        var globalWindow = now.AddHours(-config.GlobalPublicRequestWindowHours);
        var installation = usage.GetPublicUsage(clientId, window, month);
        var global = usage.GetPublicUsage(null, globalWindow, month);
        if (clientId is not null && usage.HasActivePublicExecution(clientId)) return new(false, "interpretation_public_concurrent");
        if (installation.UnresolvedCount > 0) return new(false, "interpretation_public_accounting_unresolved");
        if (installation.RequestCount >= config.PublicRequestLimit) return new(false, "interpretation_public_request_limit");
        if (installation.MonthlyCost >= config.PublicMonthlyUsd) return new(false, "interpretation_public_monthly_limit");
        if (global.RequestCount >= config.GlobalPublicRequestLimit) return new(false, "interpretation_public_global_request_limit");
        if (global.UnresolvedCount > 0 && installation.MonthlyCost + .10m >= config.PublicMonthlyUsd)
            return new(false, "interpretation_public_global_accounting_unresolved");
        if (global.MonthlyCost >= config.GlobalPublicMonthlyUsd) return new(false, "interpretation_public_global_monthly_limit");
        return new(true, null);
    }

    public PublicQuotaStatus GetStatus(string clientId, DateTime? nowUtc = null)
    {
        var now = (nowUtc ?? DateTime.UtcNow).ToUniversalTime(); var config = presets.Read();
        var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var usageSnapshot = usage.GetPublicUsage(clientId, now.AddHours(-config.PublicRequestWindowHours), month);
        return new(usageSnapshot.RequestCount, config.PublicRequestLimit, usageSnapshot.MonthlyCost, config.PublicMonthlyUsd,
            now.AddHours(config.PublicRequestWindowHours), month.AddMonths(1), usageSnapshot.UnresolvedCount == 0);
    }
}

public readonly record struct PublicQuotaDecision(bool Allowed, string? Code);
public readonly record struct PublicQuotaStatus(int RequestsUsed, int RequestLimit, decimal CostUsed, decimal CostLimit,
    DateTime RequestResetUtc, DateTime CostResetUtc, bool AccountingResolved);
