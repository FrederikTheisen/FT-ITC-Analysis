using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public static class RegistrationMessageKinds
{
    public const string Activation = "activation";
    public const string AccessCode = "access-code";
}

public enum RegistrationSubmissionOutcome { Created, PendingResendQueued, PendingCooldown, Active }

public sealed record RegistrationSubmissionResult(string RegistrationId, RegistrationSubmissionOutcome Outcome);

/// <summary>Encrypted registration delivery queue and atomic activation boundary.</summary>
public sealed class RegistrationDeliveryOutbox
{
    const string ActivationPrefix = "ftitc_act_";
    const string AccessCodePrefix = "ftitc_op_";
    readonly SelfRegistrationStore store;
    readonly IDataProtector protector;
    readonly RegistrationOptions options;

    public RegistrationDeliveryOutbox(SelfRegistrationStore store, IDataProtectionProvider protection,
        IOptions<InterpretationOptions> configured)
    {
        this.store = store;
        options = configured.Value.Registration;
        // Retain the original purpose so already-encrypted registration messages remain readable.
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
                last_failure_code TEXT NULL,
                kind TEXT NOT NULL DEFAULT 'activation',
                token_hash TEXT NULL,
                queued_at_utc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_registration_delivery_state ON registration_delivery(state, next_attempt_utc);
            """;
        command.ExecuteNonQuery();
        SelfRegistrationStore.EnsureColumn(db, "registration_delivery", "expires_at_utc", "TEXT NULL");
        SelfRegistrationStore.EnsureColumn(db, "registration_delivery", "kind", "TEXT NOT NULL DEFAULT 'activation'");
        SelfRegistrationStore.EnsureColumn(db, "registration_delivery", "token_hash", "TEXT NULL");
        SelfRegistrationStore.EnsureColumn(db, "registration_delivery", "queued_at_utc", "TEXT NULL");
        using (var index = db.CreateCommand())
        {
            index.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS ix_registration_activation_hash ON registration_delivery(token_hash) WHERE token_hash IS NOT NULL";
            index.ExecuteNonQuery();
        }
        MigrateProtectedMessages(db);
    }

    public RegistrationSubmissionResult Submit(string name, string email, string? organization,
        string termsVersion, string privacyVersion, DateTime utcNow)
    {
        var canonical = SelfRegistrationStore.NormalizeEmail(email);
        using var db = store.Open(); using var tx = db.BeginTransaction(deferred: false);
        string? existingId = null; string? existingState = null;
        using (var find = db.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT id,state FROM registration_accounts WHERE normalized_email=$email LIMIT 1";
            find.Parameters.AddWithValue("$email", canonical);
            using var reader = find.ExecuteReader();
            if (reader.Read()) { existingId = reader.GetString(0); existingState = reader.GetString(1); }
        }

        if (existingId is null)
        {
            var id = Guid.NewGuid().ToString("N");
            using (var insert = db.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO registration_accounts
                      (id,name,email,normalized_email,organization,state,terms_version,privacy_version,accepted_at_utc,created_at_utc)
                    VALUES ($id,$name,$email,$normalized,$organization,'pending',$terms,$privacy,$accepted,$created)
                    """;
                insert.Parameters.AddWithValue("$id", id); insert.Parameters.AddWithValue("$name", name.Trim());
                insert.Parameters.AddWithValue("$email", canonical); insert.Parameters.AddWithValue("$normalized", canonical);
                insert.Parameters.AddWithValue("$organization", string.IsNullOrWhiteSpace(organization) ? DBNull.Value : organization.Trim());
                insert.Parameters.AddWithValue("$terms", termsVersion); insert.Parameters.AddWithValue("$privacy", privacyVersion);
                insert.Parameters.AddWithValue("$accepted", utcNow.ToString("O", CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$created", utcNow.ToString("O", CultureInfo.InvariantCulture));
                insert.ExecuteNonQuery();
            }
            QueueActivation(db, tx, id, utcNow, replace: false);
            tx.Commit();
            return new(id, RegistrationSubmissionOutcome.Created);
        }

        if (existingState is "active" or "activating" or "scrubbed")
        {
            tx.Commit();
            return new(existingId, RegistrationSubmissionOutcome.Active);
        }

        string? kind = null; string? expiresText = null; string? queuedText = null;
        using (var delivery = db.CreateCommand())
        {
            delivery.Transaction = tx;
            delivery.CommandText = "SELECT kind,expires_at_utc,queued_at_utc FROM registration_delivery WHERE registration_id=$id";
            delivery.Parameters.AddWithValue("$id", existingId);
            using var reader = delivery.ExecuteReader();
            if (reader.Read())
            {
                kind = reader.GetString(0);
                expiresText = reader.IsDBNull(1) ? null : reader.GetString(1);
                queuedText = reader.IsDBNull(2) ? null : reader.GetString(2);
            }
        }

        if (kind == RegistrationMessageKinds.AccessCode)
        {
            tx.Commit();
            return new(existingId, RegistrationSubmissionOutcome.Active);
        }

        var expires = ParseUtc(expiresText);
        var queued = ParseUtc(queuedText);
        if (expires is not null && expires > utcNow && queued is not null
            && queued > utcNow.AddMinutes(-options.ResendCooldownMinutes))
        {
            tx.Commit();
            return new(existingId, RegistrationSubmissionOutcome.PendingCooldown);
        }

        if (kind == RegistrationMessageKinds.Activation && expires is not null && expires > utcNow)
        {
            using var resend = db.CreateCommand(); resend.Transaction = tx;
            resend.CommandText = """
                UPDATE registration_delivery
                SET state='pending',attempts=0,next_attempt_utc=$now,queued_at_utc=$now,
                    idempotency_key=$key,last_failure_code=NULL
                WHERE registration_id=$id AND kind='activation'
                """;
            resend.Parameters.AddWithValue("$now", utcNow.ToString("O", CultureInfo.InvariantCulture));
            resend.Parameters.AddWithValue("$key", ActivationIdempotencyKey(existingId)); resend.Parameters.AddWithValue("$id", existingId);
            resend.ExecuteNonQuery();
        }
        else QueueActivation(db, tx, existingId, utcNow, replace: true);

        using (var pending = db.CreateCommand())
        {
            pending.Transaction = tx;
            pending.CommandText = "UPDATE registration_accounts SET state='pending',failed_at_utc=NULL,failure_code=NULL WHERE id=$id AND state<>'scrubbed'";
            pending.Parameters.AddWithValue("$id", existingId); pending.ExecuteNonQuery();
        }
        tx.Commit();
        return new(existingId, RegistrationSubmissionOutcome.PendingResendQueued);
    }

    public PendingRegistrationDelivery? GetPending(DateTime utcNow)
    {
        using var db = store.Open(); using var command = db.CreateCommand();
        command.CommandText = """
            SELECT d.registration_id,d.kind,d.protected_code,d.idempotency_key,d.attempts,a.name,a.email,a.organization
            FROM registration_delivery d JOIN registration_accounts a ON a.id=d.registration_id
            WHERE d.state='pending' AND d.next_attempt_utc <= $now
              AND (d.kind<>'activation' OR d.expires_at_utc > $now)
              AND a.state<>'scrubbed'
            ORDER BY d.next_attempt_utc LIMIT 1
            """;
        command.Parameters.AddWithValue("$now", utcNow.ToString("O", CultureInfo.InvariantCulture));
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return ReadDelivery(reader);
    }

    public PendingRegistrationDelivery? BeginActivation(string token, DateTime utcNow)
    {
        if (!IsActivationTokenShape(token)) return null;
        var hash = Hash(token);
        using var db = store.Open(); using var tx = db.BeginTransaction(deferred: false);
        PendingRegistrationDelivery? activation = null;
        using (var command = db.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                SELECT d.registration_id,d.kind,d.protected_code,d.idempotency_key,d.attempts,a.name,a.email,a.organization
                FROM registration_delivery d JOIN registration_accounts a ON a.id=d.registration_id
                WHERE d.kind='activation' AND d.state='sent' AND d.token_hash=$hash
                  AND d.expires_at_utc > $now AND a.state='activation-sent'
                LIMIT 1
                """;
            command.Parameters.AddWithValue("$hash", hash); command.Parameters.AddWithValue("$now", utcNow.ToString("O", CultureInfo.InvariantCulture));
            using var reader = command.ExecuteReader(); if (reader.Read()) activation = ReadDelivery(reader);
        }
        if (activation is null || !FixedEquals(activation.Secret, token)) { tx.Rollback(); return null; }

        var bearer = AccessCodePrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        using (var update = db.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = """
                UPDATE registration_delivery
                SET kind='access-code',protected_code=$secret,token_hash=NULL,idempotency_key=$key,
                    state='pending',attempts=0,next_attempt_utc=$now,queued_at_utc=$now,
                    expires_at_utc=NULL,last_failure_code=NULL
                WHERE registration_id=$id AND kind='activation' AND state='sent' AND token_hash=$hash
                """;
            update.Parameters.AddWithValue("$secret", protector.Protect(bearer));
            update.Parameters.AddWithValue("$key", "ftitc-access-" + activation.RegistrationId);
            update.Parameters.AddWithValue("$now", utcNow.ToString("O", CultureInfo.InvariantCulture));
            update.Parameters.AddWithValue("$id", activation.RegistrationId); update.Parameters.AddWithValue("$hash", hash);
            if (update.ExecuteNonQuery() != 1) { tx.Rollback(); return null; }
        }
        using (var account = db.CreateCommand())
        {
            account.Transaction = tx; account.CommandText = "UPDATE registration_accounts SET state='activating',failure_code=NULL WHERE id=$id AND state='activation-sent'";
            account.Parameters.AddWithValue("$id", activation.RegistrationId);
            if (account.ExecuteNonQuery() != 1) { tx.Rollback(); return null; }
        }
        tx.Commit();
        return activation with { Kind = RegistrationMessageKinds.AccessCode, Secret = bearer, IdempotencyKey = "ftitc-access-" + activation.RegistrationId, Attempts = 0 };
    }

    public void MarkSent(string registrationId, string kind, DateTime sentAtUtc)
    {
        using var db = store.Open(); using var tx = db.BeginTransaction();
        using (var delivery = db.CreateCommand())
        {
            delivery.Transaction = tx;
            delivery.CommandText = "UPDATE registration_delivery SET state='sent',last_failure_code=NULL WHERE registration_id=$id AND kind=$kind AND state='pending'";
            delivery.Parameters.AddWithValue("$id", registrationId); delivery.Parameters.AddWithValue("$kind", kind);
            if (delivery.ExecuteNonQuery() != 1) { tx.Rollback(); return; }
        }
        using (var account = db.CreateCommand())
        {
            account.Transaction = tx;
            account.CommandText = kind == RegistrationMessageKinds.AccessCode
                ? "UPDATE registration_accounts SET state='active',activated_at_utc=$time,failure_code=NULL WHERE id=$id AND state='activating'"
                : "UPDATE registration_accounts SET state='activation-sent',delivered_at_utc=$time,failure_code=NULL WHERE id=$id AND state IN ('pending','failed','activation-sent')";
            account.Parameters.AddWithValue("$time", sentAtUtc.ToString("O", CultureInfo.InvariantCulture)); account.Parameters.AddWithValue("$id", registrationId);
            if (account.ExecuteNonQuery() != 1) { tx.Rollback(); return; }
        }
        tx.Commit();
    }

    public void MarkRetry(string registrationId, int attempts, DateTime nextAttemptUtc, string safeFailureCode)
        => Update(registrationId, "pending", attempts, nextAttemptUtc, safeFailureCode);

    public void MarkFailed(string registrationId, string safeFailureCode)
    {
        var now = DateTime.UtcNow;
        using var db = store.Open(); using var tx = db.BeginTransaction();
        using (var delivery = db.CreateCommand())
        {
            delivery.Transaction = tx;
            delivery.CommandText = "UPDATE registration_delivery SET state='failed',last_failure_code=$failure WHERE registration_id=$id";
            delivery.Parameters.AddWithValue("$failure", safeFailureCode); delivery.Parameters.AddWithValue("$id", registrationId);
            delivery.ExecuteNonQuery();
        }
        using (var account = db.CreateCommand())
        {
            account.Transaction = tx;
            account.CommandText = "UPDATE registration_accounts SET state='failed',failed_at_utc=$time,failure_code=$failure WHERE id=$id AND state<>'scrubbed'";
            account.Parameters.AddWithValue("$time", now.ToString("O", CultureInfo.InvariantCulture));
            account.Parameters.AddWithValue("$failure", safeFailureCode); account.Parameters.AddWithValue("$id", registrationId);
            account.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public void Cancel(string registrationId) => Update(registrationId, "cancelled", null, null, "account_scrubbed");

    void QueueActivation(SqliteConnection db, SqliteTransaction tx, string id, DateTime utcNow, bool replace)
    {
        var token = ActivationPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var expires = utcNow.AddHours(options.ActivationLifetimeHours);
        using var command = db.CreateCommand(); command.Transaction = tx;
        command.CommandText = replace ? """
            INSERT INTO registration_delivery
              (registration_id,protected_code,idempotency_key,state,attempts,next_attempt_utc,expires_at_utc,last_failure_code,kind,token_hash,queued_at_utc)
            VALUES ($id,$secret,$key,'pending',0,$now,$expires,NULL,'activation',$hash,$now)
            ON CONFLICT(registration_id) DO UPDATE SET
              protected_code=excluded.protected_code,idempotency_key=excluded.idempotency_key,state='pending',attempts=0,
              next_attempt_utc=excluded.next_attempt_utc,expires_at_utc=excluded.expires_at_utc,last_failure_code=NULL,
              kind='activation',token_hash=excluded.token_hash,queued_at_utc=excluded.queued_at_utc
            """ : """
            INSERT INTO registration_delivery
              (registration_id,protected_code,idempotency_key,state,attempts,next_attempt_utc,expires_at_utc,last_failure_code,kind,token_hash,queued_at_utc)
            VALUES ($id,$secret,$key,'pending',0,$now,$expires,NULL,'activation',$hash,$now)
            """;
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$secret", protector.Protect(token));
        command.Parameters.AddWithValue("$key", ActivationIdempotencyKey(id)); command.Parameters.AddWithValue("$hash", Hash(token));
        command.Parameters.AddWithValue("$now", utcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$expires", expires.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    void MigrateProtectedMessages(SqliteConnection db)
    {
        var rows = new List<(string Id, string Protected, string State, string AccountState)>();
        using (var read = db.CreateCommand())
        {
            read.CommandText = """
                SELECT d.registration_id,d.protected_code,d.state,a.state
                FROM registration_delivery d JOIN registration_accounts a ON a.id=d.registration_id
                WHERE d.token_hash IS NULL AND d.kind='activation'
                """;
            using var reader = read.ExecuteReader();
            while (reader.Read()) rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }
        foreach (var row in rows)
        {
            string? secret = null;
            try { secret = protector.Unprotect(row.Protected); }
            catch (CryptographicException)
            {
                // Deployments before the persistent key ring can contain delivery secrets
                // encrypted with an ephemeral key. An active account already has its bearer
                // credential, so preserve it and retire only the unreadable delivery payload.
                // For every other account, issue a fresh activation link instead of allowing
                // one stale row to prevent all future registration requests.
            }
            using var update = db.CreateCommand();
            if (secret is null && row.AccountState is "active" or "scrubbed")
            {
                update.CommandText = """
                    UPDATE registration_delivery
                    SET kind='access-code',state=$state,token_hash=NULL,
                        queued_at_utc=COALESCE(queued_at_utc,next_attempt_utc),last_failure_code='legacy_secret_unreadable'
                    WHERE registration_id=$id
                    """;
                update.Parameters.AddWithValue("$state", row.AccountState == "scrubbed" ? "cancelled" : "sent");
            }
            else if (secret is not null && secret.StartsWith(ActivationPrefix, StringComparison.Ordinal))
            {
                update.CommandText = "UPDATE registration_delivery SET token_hash=$hash,queued_at_utc=COALESCE(queued_at_utc,next_attempt_utc) WHERE registration_id=$id";
                update.Parameters.AddWithValue("$hash", Hash(secret));
                if (row.State == "sent")
                {
                    using var account = db.CreateCommand();
                    account.CommandText = "UPDATE registration_accounts SET state='activation-sent' WHERE id=$id AND state='delivered'";
                    account.Parameters.AddWithValue("$id", row.Id); account.ExecuteNonQuery();
                }
            }
            else if (secret is not null && secret.StartsWith(AccessCodePrefix, StringComparison.Ordinal) && row.State == "sent")
                update.CommandText = "UPDATE registration_delivery SET kind='access-code',queued_at_utc=COALESCE(queued_at_utc,next_attempt_utc) WHERE registration_id=$id";
            else
            {
                var token = ActivationPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));
                update.CommandText = """
                    UPDATE registration_delivery SET protected_code=$secret,token_hash=$hash,kind='activation',state='pending',attempts=0,
                    idempotency_key=$key,next_attempt_utc=$now,queued_at_utc=$now,expires_at_utc=$expires,last_failure_code=NULL
                    WHERE registration_id=$id
                    """;
                var now = DateTime.UtcNow;
                update.Parameters.AddWithValue("$secret", protector.Protect(token)); update.Parameters.AddWithValue("$hash", Hash(token));
                update.Parameters.AddWithValue("$key", ActivationIdempotencyKey(row.Id)); update.Parameters.AddWithValue("$now", now.ToString("O", CultureInfo.InvariantCulture));
                update.Parameters.AddWithValue("$expires", now.AddHours(options.ActivationLifetimeHours).ToString("O", CultureInfo.InvariantCulture));
            }
            update.Parameters.AddWithValue("$id", row.Id); update.ExecuteNonQuery();
        }
    }

    void Update(string id, string state, int? attempts, DateTime? nextAttempt, string? failureCode)
    {
        using var db = store.Open(); using var command = db.CreateCommand();
        command.CommandText = "UPDATE registration_delivery SET state=$state,attempts=COALESCE($attempts,attempts),next_attempt_utc=COALESCE($next,next_attempt_utc),last_failure_code=$failure WHERE registration_id=$id";
        command.Parameters.AddWithValue("$state", state); command.Parameters.AddWithValue("$attempts", (object?)attempts ?? DBNull.Value);
        command.Parameters.AddWithValue("$next", nextAttempt is null ? DBNull.Value : nextAttempt.Value.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$failure", (object?)failureCode ?? DBNull.Value); command.Parameters.AddWithValue("$id", id); command.ExecuteNonQuery();
    }

    PendingRegistrationDelivery ReadDelivery(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), protector.Unprotect(reader.GetString(2)), reader.GetString(3), reader.GetInt32(4),
        reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7));

    static string ActivationIdempotencyKey(string id) => $"ftitc-registration-{id}-{Guid.NewGuid():N}";
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(left)), SHA256.HashData(Encoding.UTF8.GetBytes(right)));
    internal static bool IsActivationTokenShape(string value) => value.StartsWith(ActivationPrefix, StringComparison.Ordinal)
        && value.Length == ActivationPrefix.Length + 43
        && value[ActivationPrefix.Length..].All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_');
    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static DateTime? ParseUtc(string? value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed.ToUniversalTime() : null;
}

public sealed record PendingRegistrationDelivery(string RegistrationId, string Kind, string Secret, string IdempotencyKey,
    int Attempts, string Name, string Email, string? Organization);
