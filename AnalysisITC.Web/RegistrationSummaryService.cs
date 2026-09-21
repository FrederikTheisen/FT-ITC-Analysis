using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public enum RegistrationSummaryStatus
{
    Available,
    Skipped,
    Unavailable,
}

public sealed record RegistrationSummaryReport(
    RegistrationSummaryStatus Status,
    IReadOnlyDictionary<string, long> AllTimeByState,
    IReadOnlyDictionary<string, long> PreviousDayByState,
    string? Detail = null)
{
    internal static readonly HashSet<string> KnownStates = new(StringComparer.Ordinal)
    {
        "pending", "activation-sent", "activating", "active", "failed", "scrubbed",
    };

    public long TotalRecords => AllTimeByState.Values.Sum();
    public long ActiveAccounts => Count("active");
    public long EmailVerified => Count("activating") + Count("active");
    public long AwaitingEmailVerification => Count("pending") + Count("activation-sent");
    public long AccessCodeDeliveryPending => Count("activating");
    public long Failed => Count("failed");
    public long Scrubbed => Count("scrubbed");
    public long Unknown => AllTimeByState.Where(pair => !KnownStates.Contains(pair.Key)).Sum(pair => pair.Value);
    public long PreviousDayCreated => PreviousDayByState.Values.Sum();
    public bool HasAttention => Status == RegistrationSummaryStatus.Unavailable || Unknown > 0;

    long Count(string state) => AllTimeByState.TryGetValue(state, out var count) ? count : 0;
}

/// <summary>Read-only aggregate reporting for public registration records.</summary>
public sealed class RegistrationSummaryService
{
    readonly RegistrationOptions options;
    readonly RegistrationAvailability registrationAvailability;
    readonly InterpretationServiceAvailability interpretationAvailability;

    public RegistrationSummaryService(IOptions<InterpretationOptions> configured,
        RegistrationAvailability registrationAvailability,
        InterpretationServiceAvailability interpretationAvailability)
    {
        options = configured.Value.Registration;
        this.registrationAvailability = registrationAvailability;
        this.interpretationAvailability = interpretationAvailability;
    }

    public RegistrationSummaryReport Build(DateOnly date)
    {
        if (!options.Enabled) return Skipped();
        try
        {
            var policy = registrationAvailability.Read();
            var interpretation = interpretationAvailability.Read();
            if (!policy.Enabled || interpretation.Status == "retired") return Skipped();
        }
        catch { return Unavailable(); }

        try
        {
            if (!File.Exists(options.DatabasePath)) return Unavailable();
            var (start, end) = DailyStatusEmail.Bounds(date);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = options.DatabasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
            connection.Open();
            var allTime = ReadCounts(connection, null, null);
            var previousDay = ReadCounts(connection, start, end);
            return new(RegistrationSummaryStatus.Available, allTime, previousDay);
        }
        catch (SqliteException) { return Unavailable(); }
        catch (IOException) { return Unavailable(); }
        catch (UnauthorizedAccessException) { return Unavailable(); }
        catch (InvalidOperationException) { return Unavailable(); }
    }

    static IReadOnlyDictionary<string, long> ReadCounts(SqliteConnection connection, DateTime? start, DateTime? end)
    {
        using var command = connection.CreateCommand();
        command.CommandText = start is null
            ? "SELECT state,COUNT(*) FROM registration_accounts GROUP BY state"
            : "SELECT state,COUNT(*) FROM registration_accounts WHERE created_at_utc >= $start AND created_at_utc < $end GROUP BY state";
        if (start is not null)
        {
            command.Parameters.AddWithValue("$start", start.Value.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$end", end!.Value.ToString("O", CultureInfo.InvariantCulture));
        }
        using var reader = command.ExecuteReader();
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var state = reader.IsDBNull(0) ? "(unknown)" : reader.GetString(0);
            if (!RegistrationSummaryReport.KnownStates.Contains(state)) state = "(unknown)";
            counts[state] = reader.GetInt64(1);
        }
        return counts;
    }

    static RegistrationSummaryReport Skipped() => new(
        RegistrationSummaryStatus.Skipped,
        new Dictionary<string, long>(StringComparer.Ordinal),
        new Dictionary<string, long>(StringComparer.Ordinal),
        "registration is intentionally unavailable");

    static RegistrationSummaryReport Unavailable() => new(
        RegistrationSummaryStatus.Unavailable,
        new Dictionary<string, long>(StringComparer.Ordinal),
        new Dictionary<string, long>(StringComparer.Ordinal),
        "registration database could not be read");
}
