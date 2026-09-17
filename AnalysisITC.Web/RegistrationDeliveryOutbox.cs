using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

/// <summary>Encrypted, metadata-only registration delivery queue.</summary>
public sealed class RegistrationDeliveryOutbox
{
    readonly SelfRegistrationStore store;
    readonly IDataProtector protector;

    public RegistrationDeliveryOutbox(SelfRegistrationStore store, IDataProtectionProvider protection)
    {
        this.store = store;
        protector = protection.CreateProtector("FT-ITC.PublicRegistration.BearerCode.v1");
        using var db = store.Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS registration_delivery (
                registration_id TEXT PRIMARY KEY,
                protected_code TEXT NOT NULL,
                idempotency_key TEXT NOT NULL UNIQUE,
                state TEXT NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                next_attempt_utc TEXT NOT NULL,
                expires_at_utc TEXT NULL,
                last_failure_code TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_registration_delivery_state ON registration_delivery(state, next_attempt_utc);
            """;
        command.ExecuteNonQuery();
        try { using var migrate = db.CreateCommand(); migrate.CommandText = "ALTER TABLE registration_delivery ADD COLUMN expires_at_utc TEXT NULL"; migrate.ExecuteNonQuery(); } catch (SqliteException) { }
    }

    public void Queue(string registrationId, string bearerCode, DateTime nextAttemptUtc, DateTime? expiresAtUtc = null)
    {
        using var db = store.Open(); using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO registration_delivery
              (registration_id,protected_code,idempotency_key,state,next_attempt_utc,expires_at_utc)
            VALUES ($id,$code,$key,'pending',$next,$expires)
            """;
        command.Parameters.AddWithValue("$id", registrationId);
        command.Parameters.AddWithValue("$code", protector.Protect(bearerCode));
        command.Parameters.AddWithValue("$key", "ftitc-registration-" + registrationId);
        command.Parameters.AddWithValue("$next", nextAttemptUtc.ToString("O"));
        command.Parameters.AddWithValue("$expires", expiresAtUtc is null ? DBNull.Value : expiresAtUtc.Value.ToString("O"));
        command.ExecuteNonQuery();
    }

    public PendingRegistrationDelivery? GetPending(DateTime utcNow)
    {
        using var db = store.Open(); using var command = db.CreateCommand();
        command.CommandText = "SELECT d.registration_id,d.protected_code,d.idempotency_key,d.attempts,a.name,a.email,a.organization FROM registration_delivery d JOIN registration_accounts a ON a.id=d.registration_id WHERE d.state='pending' AND d.next_attempt_utc <= $now AND (d.expires_at_utc IS NULL OR d.expires_at_utc > $now) ORDER BY d.next_attempt_utc LIMIT 1";
        command.Parameters.AddWithValue("$now", utcNow.ToString("O"));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return new PendingRegistrationDelivery(reader.GetString(0), protector.Unprotect(reader.GetString(1)), reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    public void MarkSent(string registrationId)
        => Update(registrationId, "sent", null, null, null);

    public PendingRegistrationDelivery? FindSentByToken(string token)
    {
        using var db = store.Open(); using var command = db.CreateCommand();
        command.CommandText = "SELECT d.registration_id,d.protected_code,d.idempotency_key,d.attempts,a.name,a.email,a.organization FROM registration_delivery d JOIN registration_accounts a ON a.id=d.registration_id WHERE d.state='sent' AND (d.expires_at_utc IS NULL OR d.expires_at_utc > $now)";
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var protectedValue = protector.Unprotect(reader.GetString(1));
            if (CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(protectedValue)), SHA256.HashData(Encoding.UTF8.GetBytes(token))))
                return new PendingRegistrationDelivery(reader.GetString(0), protectedValue, reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6));
        }
        return null;
    }

    public void MarkActivated(string registrationId) => Update(registrationId, "activated", null, null, null);

    public void MarkRetry(string registrationId, int attempts, DateTime nextAttemptUtc, string safeFailureCode)
        => Update(registrationId, "pending", attempts, nextAttemptUtc, safeFailureCode);

    public void MarkFailed(string registrationId, string safeFailureCode)
        => Update(registrationId, "failed", null, null, safeFailureCode);

    public void Cancel(string registrationId) => Update(registrationId, "cancelled", null, null, "account_scrubbed");

    void Update(string id, string state, int? attempts, DateTime? nextAttempt, string? failureCode)
    {
        using var db = store.Open(); using var command = db.CreateCommand();
        command.CommandText = "UPDATE registration_delivery SET state=$state, attempts=COALESCE($attempts,attempts), next_attempt_utc=COALESCE($next,next_attempt_utc), last_failure_code=$failure WHERE registration_id=$id";
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$attempts", (object?)attempts ?? DBNull.Value);
        command.Parameters.AddWithValue("$next", nextAttempt is null ? DBNull.Value : nextAttempt.Value.ToString("O"));
        command.Parameters.AddWithValue("$failure", (object?)failureCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }
}

public sealed record PendingRegistrationDelivery(string RegistrationId, string BearerCode, string IdempotencyKey, int Attempts, string Name, string Email, string? Organization);
