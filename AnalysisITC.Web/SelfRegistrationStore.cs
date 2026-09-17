using System.Globalization;
using System.Net.Mail;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

/// <summary>Persistent metadata for public registration and its delivery outbox.</summary>
public sealed class SelfRegistrationStore
{
    readonly string path;

    public SelfRegistrationStore(IOptions<InterpretationOptions> options)
    {
        path = options.Value.Registration.DatabasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS registration_accounts (
                id TEXT PRIMARY KEY,
                name TEXT NULL,
                email TEXT NULL,
                normalized_email TEXT NULL UNIQUE,
                organization TEXT NULL,
                state TEXT NOT NULL,
                terms_version TEXT NOT NULL,
                privacy_version TEXT NOT NULL,
                accepted_at_utc TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                delivered_at_utc TEXT NULL,
                failed_at_utc TEXT NULL,
                failure_code TEXT NULL,
                activated_at_utc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_registration_state ON registration_accounts(state);
            CREATE INDEX IF NOT EXISTS ix_registration_created ON registration_accounts(created_at_utc);
            """;
        command.ExecuteNonQuery();
        EnsureColumn(db, "registration_accounts", "activated_at_utc", "TEXT NULL");
        NormalizeStoredEmails(db);
    }

    public SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        db.Open();
        using var busy = db.CreateCommand();
        busy.CommandText = "PRAGMA busy_timeout=5000;";
        busy.ExecuteNonQuery();
        return db;
    }

    internal static string NormalizeEmail(string email) => new MailAddress(email.Trim()).Address.Trim().ToLowerInvariant();

    public void ReconcileActiveAccounts(IEnumerable<OperatorCodeRecord> records)
    {
        var ids = records.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0) return;
        using var db = Open(); using var tx = db.BeginTransaction();
        foreach (var id in ids)
        {
            using var command = db.CreateCommand(); command.Transaction = tx;
            command.CommandText = "UPDATE registration_accounts SET state='active',activated_at_utc=COALESCE(activated_at_utc,$time),failure_code=NULL WHERE id=$id AND state<>'scrubbed'";
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public bool Scrub(string id, DateTime scrubbedAtUtc)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        int deliveryCancelled;
        using (var delivery = db.CreateCommand()) { delivery.Transaction = tx; delivery.CommandText = "DELETE FROM registration_delivery WHERE registration_id=$id"; delivery.Parameters.AddWithValue("$id", id); deliveryCancelled = delivery.ExecuteNonQuery(); }
        using var update = db.CreateCommand(); update.Transaction = tx;
        update.CommandText = "UPDATE registration_accounts SET name=NULL,email=NULL,normalized_email=NULL,organization=NULL,state='scrubbed' WHERE id=$id";
        update.Parameters.AddWithValue("$id", id); update.ExecuteNonQuery(); tx.Commit(); return deliveryCancelled > 0;
    }

    internal static void EnsureColumn(SqliteConnection db, string table, string column, string definition)
    {
        using var inspect = db.CreateCommand(); inspect.CommandText = $"PRAGMA table_info({table})";
        using var reader = inspect.ExecuteReader();
        while (reader.Read()) if (string.Equals(reader.GetString(1), column, StringComparison.Ordinal)) return;
        reader.Close();
        using var alter = db.CreateCommand(); alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}"; alter.ExecuteNonQuery();
    }

    static void NormalizeStoredEmails(SqliteConnection db)
    {
        var rows = new List<(string Id, string Canonical)>();
        using (var read = db.CreateCommand())
        {
            read.CommandText = "SELECT id,email FROM registration_accounts WHERE email IS NOT NULL";
            using var reader = read.ExecuteReader();
            while (reader.Read()) rows.Add((reader.GetString(0), NormalizeEmail(reader.GetString(1))));
        }
        var collision = rows.GroupBy(row => row.Canonical, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (collision is not null)
            throw new InvalidDataException($"Registration email normalization found {collision.Count()} conflicting records; manual review is required.");
        using var tx = db.BeginTransaction();
        foreach (var row in rows)
        {
            using var update = db.CreateCommand(); update.Transaction = tx;
            update.CommandText = "UPDATE registration_accounts SET normalized_email=$email WHERE id=$id";
            update.Parameters.AddWithValue("$email", row.Canonical); update.Parameters.AddWithValue("$id", row.Id); update.ExecuteNonQuery();
        }
        tx.Commit();
    }
}

public sealed class RegistrationRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Organisation { get; set; }
    public string? TermsVersion { get; set; }
    public string? PrivacyVersion { get; set; }
    public bool AcceptedTerms { get; set; }
    public bool AcknowledgedPrivacy { get; set; }
    public string? TurnstileToken { get; set; }
}

public sealed class RegistrationActivationRequest { public string? Token { get; set; } }
