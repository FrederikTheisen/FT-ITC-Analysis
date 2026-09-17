using System.Globalization;
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
                failure_code TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_registration_state ON registration_accounts(state);
            CREATE INDEX IF NOT EXISTS ix_registration_created ON registration_accounts(created_at_utc);
            """;
        command.ExecuteNonQuery();
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

    public bool EmailExists(string normalizedEmail)
    {
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = "SELECT 1 FROM registration_accounts WHERE normalized_email=$email LIMIT 1";
        command.Parameters.AddWithValue("$email", normalizedEmail);
        return command.ExecuteScalar() is not null;
    }

    public string CreatePending(string name, string email, string? organization, string termsVersion, string privacyVersion, DateTime acceptedAtUtc)
    {
        var id = Guid.NewGuid().ToString("N");
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO registration_accounts
              (id,name,email,normalized_email,organization,state,terms_version,privacy_version,accepted_at_utc,created_at_utc)
            VALUES ($id,$name,$email,$normalized,$organization,'pending',$terms,$privacy,$accepted,$created)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$email", email);
        command.Parameters.AddWithValue("$normalized", email.ToUpperInvariant());
        command.Parameters.AddWithValue("$organization", (object?)organization ?? DBNull.Value);
        command.Parameters.AddWithValue("$terms", termsVersion);
        command.Parameters.AddWithValue("$privacy", privacyVersion);
        command.Parameters.AddWithValue("$accepted", acceptedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
        return id;
    }

    public void MarkDelivered(string id, DateTime deliveredAtUtc)
        => UpdateState(id, "delivered", deliveredAtUtc, null);

    public void MarkFailed(string id, string safeFailureCode, DateTime failedAtUtc)
        => UpdateState(id, "failed", failedAtUtc, safeFailureCode);

    public bool MarkActivated(string id)
    {
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = "UPDATE registration_accounts SET state='active' WHERE id=$id AND state='delivered'";
        command.Parameters.AddWithValue("$id", id); return command.ExecuteNonQuery() == 1;
    }

    public void DeleteDelivery(string id)
    {
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = "DELETE FROM registration_delivery WHERE registration_id=$id";
        command.Parameters.AddWithValue("$id", id); command.ExecuteNonQuery();
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

    void UpdateState(string id, string state, DateTime timestamp, string? failureCode)
    {
        using var db = Open(); using var command = db.CreateCommand();
        command.CommandText = "UPDATE registration_accounts SET state=$state, delivered_at_utc=CASE WHEN $state='delivered' THEN $time ELSE delivered_at_utc END, failed_at_utc=CASE WHEN $state='failed' THEN $time ELSE failed_at_utc END, failure_code=$failure WHERE id=$id";
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$time", timestamp.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$failure", (object?)failureCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
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
