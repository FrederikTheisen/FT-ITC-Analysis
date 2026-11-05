using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace AnalysisITC.Web;

public sealed partial class DailyStatusEmail
{
    internal bool AddOpenAiCreditBalance(List<string> lines)
    {
        if (openAiCreditBalancePath is null) return false;
        lines.Add(""); lines.Add("OpenAI credit estimate");
        try
        {
            var baseline = JsonSerializer.Deserialize<OpenAiCreditBaseline>(File.ReadAllText(openAiCreditBalancePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException();
            var checkedAt = now();
            if (baseline.BalanceUsd < 0 || baseline.WarnBelowUsd < 0
                || baseline.CheckedAtUtc == default || baseline.CheckedAtUtc > checkedAt)
                throw new InvalidDataException();

            var lastUpdate = $"  Last balance update: {Format(baseline.CheckedAtUtc)} — {Usd(baseline.BalanceUsd)} USD";
            var costs = usage.ReadOpenAiCreditCosts(baseline.CheckedAtUtc, checkedAt);
            if (costs.UnknownCosts > 0)
            {
                lines.Add($"  Estimated remaining: unavailable — {costs.UnknownCosts} attempt(s) have unknown costs, including any waived costs");
                lines.Add(lastUpdate);
                lines.Add("  Balance warning: ATTENTION — accounting is incomplete");
                return true;
            }
            var remaining = baseline.BalanceUsd - costs.KnownCost;
            lines.Add("  Estimated remaining: " + Usd(remaining) + " USD");
            lines.Add(lastUpdate);
            if (remaining < baseline.WarnBelowUsd)
            {
                lines.Add("  Balance warning: WARNING — estimated credits below " + Usd(baseline.WarnBelowUsd) + " USD; check OpenAI billing");
                return true;
            }
            return false;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or SqliteException
            or AccountingUnavailableException or InvalidOperationException or ArgumentException or OverflowException)
        {
            lines.Add("  Estimated remaining: unavailable — baseline or accounting could not be read or validated");
            lines.Add("  Balance warning: ATTENTION — check the OpenAI balance baseline and usage ledger");
            return true;
        }
    }

    static string Usd(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    sealed record OpenAiCreditBaseline(
        [property: JsonRequired] decimal BalanceUsd,
        [property: JsonRequired] DateTimeOffset CheckedAtUtc,
        decimal WarnBelowUsd = 5m);
}
