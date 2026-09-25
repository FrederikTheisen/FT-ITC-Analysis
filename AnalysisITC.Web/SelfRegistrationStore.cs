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
                terms_version TEXT NULL,
                privacy_version TEXT NULL,
                accepted_at_utc TEXT NULL,
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
        MigrateScrubbableRegistrationMetadata(db);
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
        update.CommandText = "UPDATE registration_accounts SET name=NULL,email=NULL,normalized_email=NULL,organization=NULL,state='scrubbed',terms_version=NULL,privacy_version=NULL,accepted_at_utc=NULL,delivered_at_utc=NULL,failed_at_utc=NULL,failure_code=NULL,activated_at_utc=NULL WHERE id=$id";
        update.Parameters.AddWithValue("$id", id); update.ExecuteNonQuery(); tx.Commit(); return deliveryCancelled > 0;
    }

    /// <summary>
    /// Scrubs clearly unverified registrations at the 30-day boundary. Eligibility is selected
    /// and rechecked while holding an immediate SQLite transaction so activation cannot race it.
    /// </summary>
    public int ScrubExpiredUnverified(DateTime utcNow)
    {
        var cutoff = (utcNow.ToUniversalTime() - TimeSpan.FromDays(30)).ToString("O", CultureInfo.InvariantCulture);
        using var db = Open();
        using var tx = db.BeginTransaction(deferred: false);
        var hasDeliveryTable = HasDeliveryTable(db, tx);
        var ids = new List<string>();
        using (var select = db.CreateCommand())
        {
            select.Transaction = tx;
            select.CommandText = hasDeliveryTable ? EligibleExpiredRegistrationsSql : EligibleExpiredWithoutDeliverySql;
            select.Parameters.AddWithValue("$cutoff", cutoff);
            using var reader = select.ExecuteReader();
            while (reader.Read()) ids.Add(reader.GetString(0));
        }

        var scrubbed = 0;
        foreach (var id in ids)
        {
            using var update = db.CreateCommand();
            update.Transaction = tx;
            update.CommandText = hasDeliveryTable ? """
                UPDATE registration_accounts
                SET name=NULL,email=NULL,normalized_email=NULL,organization=NULL,state='scrubbed',
                    terms_version=NULL,privacy_version=NULL,accepted_at_utc=NULL,delivered_at_utc=NULL,
                    failed_at_utc=NULL,failure_code=NULL,activated_at_utc=NULL
                WHERE id=$id AND created_at_utc <= $cutoff
                  AND (state IN ('pending','activation-sent')
                    OR (state='failed' AND activated_at_utc IS NULL AND EXISTS (
                        SELECT 1 FROM registration_delivery d
                        WHERE d.registration_id=registration_accounts.id AND d.kind='activation')))
                """ : """
                UPDATE registration_accounts
                SET name=NULL,email=NULL,normalized_email=NULL,organization=NULL,state='scrubbed',
                    terms_version=NULL,privacy_version=NULL,accepted_at_utc=NULL,delivered_at_utc=NULL,
                    failed_at_utc=NULL,failure_code=NULL,activated_at_utc=NULL
                WHERE id=$id AND created_at_utc <= $cutoff AND state IN ('pending','activation-sent')
                """;
            update.Parameters.AddWithValue("$id", id);
            update.Parameters.AddWithValue("$cutoff", cutoff);
            // Recheck lifecycle state, original age, and failed-delivery kind before deleting the
            // delivery row. The immediate transaction makes the update and deletion indivisible.
            if (update.ExecuteNonQuery() == 0) continue;

            if (hasDeliveryTable)
            {
                using var deleteDelivery = db.CreateCommand();
                deleteDelivery.Transaction = tx;
                deleteDelivery.CommandText = "DELETE FROM registration_delivery WHERE registration_id=$id";
                deleteDelivery.Parameters.AddWithValue("$id", id);
                deleteDelivery.ExecuteNonQuery();
            }
            scrubbed++;
        }
        tx.Commit();
        return scrubbed;
    }

    const string EligibleExpiredRegistrationsSql = """
        SELECT a.id
        FROM registration_accounts a
        WHERE a.created_at_utc <= $cutoff
          AND (a.state IN ('pending','activation-sent')
            OR (a.state='failed' AND a.activated_at_utc IS NULL AND EXISTS (
                SELECT 1 FROM registration_delivery d
                WHERE d.registration_id=a.id AND d.kind='activation')))
        ORDER BY a.created_at_utc
        """;

    const string EligibleExpiredWithoutDeliverySql = """
        SELECT a.id
        FROM registration_accounts a
        WHERE a.created_at_utc <= $cutoff AND a.state IN ('pending','activation-sent')
        ORDER BY a.created_at_utc
        """;

    static bool HasDeliveryTable(SqliteConnection db, SqliteTransaction tx)
    {
        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='registration_delivery' LIMIT 1";
        return command.ExecuteScalar() is not null;
    }

    static void MigrateScrubbableRegistrationMetadata(SqliteConnection db)
    {
        var requiredNullableColumns = new HashSet<string>(StringComparer.Ordinal)
            { "terms_version", "privacy_version", "accepted_at_utc" };
        using (var inspect = db.CreateCommand())
        {
            inspect.CommandText = "PRAGMA table_info(registration_accounts)";
            using var reader = inspect.ExecuteReader();
            while (reader.Read())
                if (requiredNullableColumns.Contains(reader.GetString(1)) && reader.GetInt32(3) == 0)
                    requiredNullableColumns.Remove(reader.GetString(1));
        }
        if (requiredNullableColumns.Count == 0) return;

        using var tx = db.BeginTransaction(deferred: false);
        using var migrate = db.CreateCommand();
        migrate.Transaction = tx;
        migrate.CommandText = """
            DROP INDEX IF EXISTS ix_registration_state;
            DROP INDEX IF EXISTS ix_registration_created;
            ALTER TABLE registration_accounts RENAME TO registration_accounts_pre_scrub_migration;
            CREATE TABLE registration_accounts (
                id TEXT PRIMARY KEY,
                name TEXT NULL,
                email TEXT NULL,
                normalized_email TEXT NULL UNIQUE,
                organization TEXT NULL,
                state TEXT NOT NULL,
                terms_version TEXT NULL,
                privacy_version TEXT NULL,
                accepted_at_utc TEXT NULL,
                created_at_utc TEXT NOT NULL,
                delivered_at_utc TEXT NULL,
                failed_at_utc TEXT NULL,
                failure_code TEXT NULL,
                activated_at_utc TEXT NULL
            );
            INSERT INTO registration_accounts
              (id,name,email,normalized_email,organization,state,terms_version,privacy_version,accepted_at_utc,
               created_at_utc,delivered_at_utc,failed_at_utc,failure_code,activated_at_utc)
            SELECT id,name,email,normalized_email,organization,state,terms_version,privacy_version,accepted_at_utc,
                   created_at_utc,delivered_at_utc,failed_at_utc,failure_code,activated_at_utc
            FROM registration_accounts_pre_scrub_migration;
            DROP TABLE registration_accounts_pre_scrub_migration;
            CREATE INDEX ix_registration_state ON registration_accounts(state);
            CREATE INDEX ix_registration_created ON registration_accounts(created_at_utc);
            """;
        migrate.ExecuteNonQuery();
        tx.Commit();
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
