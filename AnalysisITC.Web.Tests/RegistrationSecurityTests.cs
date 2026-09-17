using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnalysisITC.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class RegistrationSecurityTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-registration-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CaseVariantsAndConcurrentSubmissionsCreateOnePendingRegistration()
    {
        var (_, outbox, _) = CreateServices();
        var now = new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);
        var emails = Enumerable.Range(0, 16).Select(index => (index % 4) switch
        {
            0 => "User@Example.org",
            1 => "user@example.org",
            2 => " user@example.org ",
            _ => "Display Name <USER@example.org>",
        });

        var results = await Task.WhenAll(emails.Select(email => Task.Run(() =>
            outbox.Submit("Original name", email, "Original organization", "terms", "privacy", now))));

        Assert.Single(results, result => result.Outcome == RegistrationSubmissionOutcome.Created);
        Assert.All(results.Where(result => result.Outcome != RegistrationSubmissionOutcome.Created),
            result => Assert.Equal(RegistrationSubmissionOutcome.PendingCooldown, result.Outcome));
        using var db = OpenDatabase();
        Assert.Equal(1L, Scalar<long>(db, "SELECT count(*) FROM registration_accounts"));
        Assert.Equal(1L, Scalar<long>(db, "SELECT count(*) FROM registration_delivery"));
        Assert.Equal("user@example.org", Scalar<string>(db, "SELECT normalized_email FROM registration_accounts"));
        Assert.Equal("user@example.org", Scalar<string>(db, "SELECT email FROM registration_accounts"));
        Assert.Equal("Original name", Scalar<string>(db, "SELECT name FROM registration_accounts"));
        var pending = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(now.AddSeconds(1)));
        var encrypted = Scalar<string>(db, "SELECT protected_code FROM registration_delivery");
        Assert.DoesNotContain(pending.Secret, encrypted, StringComparison.Ordinal);
        Assert.Equal(64, Scalar<string>(db, "SELECT token_hash FROM registration_delivery").Length);
    }

    [Fact]
    public void ResendUsesSameValidTokenAndRotatesOnlyAfterExpiry()
    {
        var (_, outbox, _) = CreateServices();
        var now = new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);
        var created = outbox.Submit("Name", "user@example.org", null, "terms", "privacy", now);
        var original = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(now));
        outbox.MarkSent(created.RegistrationId, RegistrationMessageKinds.Activation, now);

        Assert.Equal(RegistrationSubmissionOutcome.PendingCooldown,
            outbox.Submit("Changed", "USER@example.org", "Changed", "terms", "privacy", now.AddMinutes(14)).Outcome);
        Assert.Equal(RegistrationSubmissionOutcome.PendingResendQueued,
            outbox.Submit("Changed", "USER@example.org", "Changed", "terms", "privacy", now.AddMinutes(16)).Outcome);
        var resent = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(now.AddMinutes(16)));
        Assert.Equal(original.Secret, resent.Secret);
        Assert.Equal("Name", resent.Name);
        outbox.MarkSent(created.RegistrationId, RegistrationMessageKinds.Activation, now.AddMinutes(16));

        Assert.Equal(RegistrationSubmissionOutcome.PendingResendQueued,
            outbox.Submit("Changed", "user@example.org", null, "terms", "privacy", now.AddHours(25)).Outcome);
        var replacement = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(now.AddHours(25)));
        Assert.NotEqual(original.Secret, replacement.Secret);
        Assert.Equal("Name", replacement.Name);
    }

    [Fact]
    public async Task ActivationIsSingleUseAndQueuesOneStableAccessCode()
    {
        var (store, outbox, configured) = CreateServices();
        var now = new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);
        var submitted = outbox.Submit("Name", "user@example.org", "Lab", "terms", "privacy", now);
        var activation = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(now));
        outbox.MarkSent(submitted.RegistrationId, RegistrationMessageKinds.Activation, now);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => outbox.BeginActivation(activation.Secret, now.AddMinutes(1)))));
        var access = Assert.Single(attempts, result => result is not null)!;
        Assert.Equal(RegistrationMessageKinds.AccessCode, access.Kind);
        Assert.StartsWith("ftitc_op_", access.Secret, StringComparison.Ordinal);
        Assert.Null(outbox.BeginActivation(activation.Secret, now.AddMinutes(2)));

        var registry = new OperatorCodeRegistry(Options.Create(configured), NullLogger<OperatorCodeRegistry>.Instance,
            new OperatorTombstoneRegistry(Options.Create(configured)));
        registry.CreateRegisteredWithCode(access.RegistrationId, access.Name, access.Email, access.Organization, access.Secret);
        registry.CreateRegisteredWithCode(access.RegistrationId, access.Name, access.Email, access.Organization, access.Secret);
        Assert.Throws<InvalidDataException>(() => registry.CreateRegisteredWithCode(access.RegistrationId,
            access.Name, access.Email, access.Organization, "ftitc_op_" + Base64Url(RandomNumberGenerator.GetBytes(32))));
        Assert.True(registry.Authenticate("Bearer " + access.Secret).IsAuthorized);

        outbox.MarkSent(access.RegistrationId, RegistrationMessageKinds.AccessCode, now.AddMinutes(2));
        using var db = store.Open();
        Assert.Equal("active", Scalar<string>(db, "SELECT state FROM registration_accounts"));
        Assert.Equal("access-code", Scalar<string>(db, "SELECT kind FROM registration_delivery"));
        Assert.Equal("sent", Scalar<string>(db, "SELECT state FROM registration_delivery"));
        Assert.Equal(0L, Scalar<long>(db, "SELECT count(*) FROM registration_delivery WHERE token_hash IS NOT NULL"));
    }

    [Fact]
    public void ActivationExpiresAtTheExactBoundaryAndMalformedValuesAreRejected()
    {
        var (_, outbox, _) = CreateServices();
        var now = new DateTime(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);
        var submitted = outbox.Submit("Name", "user@example.org", null, "terms", "privacy", now);
        var activation = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(now));
        outbox.MarkSent(submitted.RegistrationId, RegistrationMessageKinds.Activation, now);

        Assert.Null(outbox.BeginActivation(activation.Secret, now.AddHours(24)));
        Assert.Null(outbox.BeginActivation("ftitc_act_short", now));
        Assert.Null(outbox.BeginActivation("ftitc_op_" + Base64Url(RandomNumberGenerator.GetBytes(32)), now));
    }

    [Fact]
    public void ScrubbingCancelsDeliveryAndInvalidatesActivationToken()
    {
        var (store, outbox, _) = CreateServices();
        var now = DateTime.UtcNow;
        var submitted = outbox.Submit("Name", "user@example.org", null, "terms", "privacy", now);
        var activation = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(now.AddSeconds(1)));
        outbox.MarkSent(submitted.RegistrationId, RegistrationMessageKinds.Activation, now);

        Assert.True(store.Scrub(submitted.RegistrationId, now.AddMinutes(1)));
        Assert.Null(outbox.BeginActivation(activation.Secret, now.AddMinutes(2)));
        Assert.Null(outbox.GetPending(now.AddDays(1)));
        using var db = store.Open();
        Assert.Equal("scrubbed", Scalar<string>(db, "SELECT state FROM registration_accounts"));
        Assert.Equal(0L, Scalar<long>(db, "SELECT count(*) FROM registration_delivery"));
    }

    [Fact]
    public void StartupRejectsCanonicalEmailCollisionsForManualReview()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "registration.db");
        using (var db = new SqliteConnection($"Data Source={path}"))
        {
            db.Open(); using var command = db.CreateCommand();
            command.CommandText = """
                CREATE TABLE registration_accounts (
                  id TEXT PRIMARY KEY,name TEXT,email TEXT,normalized_email TEXT UNIQUE,organization TEXT,state TEXT NOT NULL,
                  terms_version TEXT NOT NULL,privacy_version TEXT NOT NULL,accepted_at_utc TEXT NOT NULL,created_at_utc TEXT NOT NULL,
                  delivered_at_utc TEXT,failed_at_utc TEXT,failure_code TEXT);
                INSERT INTO registration_accounts VALUES
                  ('one','One','User@example.org','User@example.org',NULL,'pending','t','p','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z',NULL,NULL,NULL),
                  ('two','Two','user@example.org','user@example.org',NULL,'pending','t','p','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z',NULL,NULL,NULL);
                """;
            command.ExecuteNonQuery();
        }
        var options = Options.Create(Configuration(path));

        Assert.Throws<InvalidDataException>(() => new SelfRegistrationStore(options));
    }

    [Fact]
    public void MigrationKeepsAlreadySentLegacyActivationLinkUsable()
    {
        Directory.CreateDirectory(directory);
        var configured = Configuration(Path.Combine(directory, "registration.db"));
        var values = Options.Create(configured);
        var store = new SelfRegistrationStore(values);
        var protection = DataProtectionProvider.Create(new DirectoryInfo(configured.Registration.DataProtectionKeysPath),
            builder => builder.SetApplicationName("FT-ITC.PublicRegistration"));
        var protector = protection.CreateProtector("FT-ITC.PublicRegistration.BearerCode.v1");
        var token = "ftitc_act_" + Base64Url(RandomNumberGenerator.GetBytes(32));
        using (var db = store.Open())
        {
            using var command = db.CreateCommand();
            command.CommandText = """
                INSERT INTO registration_accounts
                  (id,name,email,normalized_email,organization,state,terms_version,privacy_version,accepted_at_utc,created_at_utc,delivered_at_utc)
                VALUES ('legacy','Legacy','legacy@example.org','LEGACY@EXAMPLE.ORG',NULL,'delivered','terms','privacy',$now,$now,$now);
                CREATE TABLE registration_delivery (
                  registration_id TEXT PRIMARY KEY,protected_code TEXT NOT NULL,idempotency_key TEXT NOT NULL UNIQUE,
                  state TEXT NOT NULL,attempts INTEGER NOT NULL DEFAULT 0,next_attempt_utc TEXT NOT NULL,
                  expires_at_utc TEXT NULL,last_failure_code TEXT NULL);
                INSERT INTO registration_delivery VALUES ('legacy',$secret,'legacy-key','sent',0,$now,$expires,NULL);
                """;
            command.Parameters.AddWithValue("$now", DateTime.UtcNow.AddMinutes(-1).ToString("O"));
            command.Parameters.AddWithValue("$expires", DateTime.UtcNow.AddHours(1).ToString("O"));
            command.Parameters.AddWithValue("$secret", protector.Protect(token));
            command.ExecuteNonQuery();
        }

        var migrated = new RegistrationDeliveryOutbox(store, protection, values);
        var access = Assert.IsType<PendingRegistrationDelivery>(migrated.BeginActivation(token, DateTime.UtcNow));
        Assert.Equal(RegistrationMessageKinds.AccessCode, access.Kind);
        using var verify = store.Open();
        Assert.Equal("activating", Scalar<string>(verify, "SELECT state FROM registration_accounts WHERE id='legacy'"));
    }

    [Fact]
    public void MigrationDoesNotBlockOnUnreadableLegacySecretForActiveAccount()
    {
        Directory.CreateDirectory(directory);
        var configured = Configuration(Path.Combine(directory, "registration.db"));
        var values = Options.Create(configured);
        var store = new SelfRegistrationStore(values);
        InsertLegacyDelivery(store, "active", ProtectWithDifferentKey("ftitc_op_" + Base64Url(RandomNumberGenerator.GetBytes(32))), "sent");

        var currentProtection = DataProtectionProvider.Create(new DirectoryInfo(configured.Registration.DataProtectionKeysPath),
            builder => builder.SetApplicationName("FT-ITC.PublicRegistration"));
        var outbox = new RegistrationDeliveryOutbox(store, currentProtection, values);

        using var db = store.Open();
        Assert.Equal("access-code", Scalar<string>(db, "SELECT kind FROM registration_delivery WHERE registration_id='legacy'"));
        Assert.Equal("sent", Scalar<string>(db, "SELECT state FROM registration_delivery WHERE registration_id='legacy'"));
        Assert.Equal(RegistrationSubmissionOutcome.Created,
            outbox.Submit("New", "new@example.org", null, "terms", "privacy", DateTime.UtcNow).Outcome);
    }

    [Fact]
    public void MigrationReplacesUnreadablePendingSecretWithFreshActivation()
    {
        Directory.CreateDirectory(directory);
        var configured = Configuration(Path.Combine(directory, "registration.db"));
        var values = Options.Create(configured);
        var store = new SelfRegistrationStore(values);
        InsertLegacyDelivery(store, "pending", ProtectWithDifferentKey("ftitc_op_" + Base64Url(RandomNumberGenerator.GetBytes(32))), "pending");

        var currentProtection = DataProtectionProvider.Create(new DirectoryInfo(configured.Registration.DataProtectionKeysPath),
            builder => builder.SetApplicationName("FT-ITC.PublicRegistration"));
        var outbox = new RegistrationDeliveryOutbox(store, currentProtection, values);
        var replacement = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(DateTime.UtcNow.AddMinutes(1)));

        Assert.Equal(RegistrationMessageKinds.Activation, replacement.Kind);
        Assert.StartsWith("ftitc_act_", replacement.Secret, StringComparison.Ordinal);
        using var db = store.Open();
        Assert.Equal(64, Scalar<string>(db, "SELECT token_hash FROM registration_delivery WHERE registration_id='legacy'").Length);
    }

    [Fact]
    public async Task HttpEndpointReturnsIdenticalAcceptedResponseForCaseVariedDuplicate()
    {
        using var factory = new RegistrationApplicationFactory(directory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false,
        });

        var first = await PostRegistration(client, "User@Example.org");
        var duplicate = await PostRegistration(client, " user@example.org ");
        var firstBody = await first.Content.ReadAsStringAsync();
        var duplicateBody = await duplicate.Content.ReadAsStringAsync();

        Assert.True(first.StatusCode == HttpStatusCode.Accepted, firstBody);
        Assert.Equal(first.StatusCode, duplicate.StatusCode);
        Assert.Equal(first.Content.Headers.ContentType?.ToString(), duplicate.Content.Headers.ContentType?.ToString());
        Assert.Equal(firstBody, duplicateBody);
        using var db = OpenDatabase();
        Assert.Equal(1L, Scalar<long>(db, "SELECT count(*) FROM registration_accounts"));
    }

    [Fact]
    public async Task ActivationEndpointConsumesTokenOnceAndUsesSafeErrors()
    {
        using var factory = new RegistrationApplicationFactory(directory);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = true, AllowAutoRedirect = false,
        });
        var submitted = await PostRegistration(client, "user@example.org");
        Assert.Equal(HttpStatusCode.Accepted, submitted.StatusCode);
        var outbox = factory.Services.GetRequiredService<RegistrationDeliveryOutbox>();
        var pending = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(DateTime.UtcNow.AddSeconds(1)));
        outbox.MarkSent(pending.RegistrationId, RegistrationMessageKinds.Activation, DateTime.UtcNow);

        var malformed = await client.PostAsJsonAsync("/api/registration/activate", new { token = "ftitc_act_short" });
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        var activated = await client.PostAsJsonAsync("/api/registration/activate", new { token = pending.Secret });
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        var reused = await client.PostAsJsonAsync("/api/registration/activate", new { token = pending.Secret });
        var unknown = await client.PostAsJsonAsync("/api/registration/activate",
            new { token = "ftitc_act_" + Base64Url(RandomNumberGenerator.GetBytes(32)) });
        Assert.Equal(HttpStatusCode.Gone, reused.StatusCode);
        Assert.Equal(reused.StatusCode, unknown.StatusCode);
        Assert.Equal(await reused.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MailFlowSendsActivationBeforeStableProvisionedAccessCode()
    {
        var (_, outbox, configured) = CreateServices();
        configured.Registration.MailConfigurationPath = Path.Combine(directory, "mail.json");
        await File.WriteAllTextAsync(configured.Registration.MailConfigurationPath,
            "{\"apiKey\":\"secret\",\"from\":\"FT-ITC <mist@ft-itc.org>\",\"replyTo\":\"support@ft-itc.org\"}");
        var now = DateTime.UtcNow;
        outbox.Submit("Test User", "user@example.org", "Test Lab", "terms", "privacy", now);
        var activation = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(now.AddSeconds(1)));
        var handler = new RecordingHandler();
        var registry = new OperatorCodeRegistry(Options.Create(configured), NullLogger<OperatorCodeRegistry>.Instance,
            new OperatorTombstoneRegistry(Options.Create(configured)));
        var sender = new RegistrationMailSender(Options.Create(configured), outbox, registry,
            new SingleClientFactory(new HttpClient(handler)), new OperatorTombstoneRegistry(Options.Create(configured)));

        Assert.True(await sender.ProcessOneAsync());
        var activationMessage = handler.Messages.Single();
        Assert.Equal("Activate your FT-ITC interpretation access", activationMessage.GetProperty("subject").GetString());
        var activationText = activationMessage.GetProperty("text").GetString()!;
        Assert.Contains("https://ft-itc.org/activate#token=ftitc_act_", activationText);
        Assert.DoesNotContain("ftitc_op_", activationText);

        var access = Assert.IsType<PendingRegistrationDelivery>(outbox.BeginActivation(activation.Secret, DateTime.UtcNow));
        Assert.False(registry.Authenticate("Bearer " + access.Secret).IsAuthorized);
        Assert.True(await sender.ProcessOneAsync());
        Assert.True(registry.Authenticate("Bearer " + access.Secret).IsAuthorized);
        var accessMessage = handler.Messages.Last();
        Assert.Equal("Your FT-ITC interpretation access is ready", accessMessage.GetProperty("subject").GetString());
        Assert.Contains(access.Secret, accessMessage.GetProperty("text").GetString());
    }

    [Fact]
    public async Task MailFlowUsesConfiguredHostedTemplatesAndExpectedVariables()
    {
        var (_, outbox, configured) = CreateServices();
        configured.Registration.MailConfigurationPath = Path.Combine(directory, "mail.json");
        await File.WriteAllTextAsync(configured.Registration.MailConfigurationPath,
            """
            {"ApiKey":"secret","From":"FT-ITC <mist@ft-itc.org>","ReplyTo":"support@ft-itc.org",
             "ActivationTemplateId":"ft-itc-registration-activation",
             "AccessCodeTemplateId":"ft-itc-registration-access-ready"}
            """);
        var now = DateTime.UtcNow;
        outbox.Submit("Test User", "user@example.org", "Test Lab", "terms", "privacy", now);
        var activation = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(now.AddSeconds(1)));
        var handler = new RecordingHandler();
        var registry = new OperatorCodeRegistry(Options.Create(configured), NullLogger<OperatorCodeRegistry>.Instance,
            new OperatorTombstoneRegistry(Options.Create(configured)));
        var sender = new RegistrationMailSender(Options.Create(configured), outbox, registry,
            new SingleClientFactory(new HttpClient(handler)), new OperatorTombstoneRegistry(Options.Create(configured)));

        Assert.True(await sender.ProcessOneAsync());
        var activationMessage = handler.Messages.Single();
        var activationTemplate = activationMessage.GetProperty("template");
        Assert.Equal("ft-itc-registration-activation", activationTemplate.GetProperty("id").GetString());
        var activationVariables = activationTemplate.GetProperty("variables");
        Assert.Equal("Test User", activationVariables.GetProperty("NAME").GetString());
        Assert.Equal("24", activationVariables.GetProperty("EXPIRY_HOURS").GetString());
        Assert.Contains("https://ft-itc.org/activate#token=ftitc_act_",
            activationVariables.GetProperty("ACTIVATION_URL").GetString());
        Assert.False(activationMessage.TryGetProperty("html", out _));

        var access = Assert.IsType<PendingRegistrationDelivery>(outbox.BeginActivation(activation.Secret, DateTime.UtcNow));
        Assert.True(await sender.ProcessOneAsync());
        var accessTemplate = handler.Messages.Last().GetProperty("template");
        Assert.Equal("ft-itc-registration-access-ready", accessTemplate.GetProperty("id").GetString());
        Assert.Equal("Test User", accessTemplate.GetProperty("variables").GetProperty("NAME").GetString());
        Assert.Equal(access.Secret, accessTemplate.GetProperty("variables").GetProperty("ACCESS_CODE").GetString());
        Assert.True(registry.Authenticate("Bearer " + access.Secret).IsAuthorized);
    }

    [Fact]
    public async Task TransientMailFailureKeepsTheSameEncryptedMessageForRetry()
    {
        var (_, outbox, configured) = CreateServices();
        configured.Registration.MailConfigurationPath = Path.Combine(directory, "mail.json");
        await File.WriteAllTextAsync(configured.Registration.MailConfigurationPath,
            "{\"apiKey\":\"secret\",\"from\":\"FT-ITC <mist@ft-itc.org>\",\"replyTo\":\"support@ft-itc.org\"}");
        var now = DateTime.UtcNow;
        outbox.Submit("Test User", "user@example.org", null, "terms", "privacy", now);
        var original = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(now.AddSeconds(1)));
        var handler = new RecordingHandler { StatusCode = HttpStatusCode.ServiceUnavailable };
        var registry = new OperatorCodeRegistry(Options.Create(configured), NullLogger<OperatorCodeRegistry>.Instance,
            new OperatorTombstoneRegistry(Options.Create(configured)));
        var sender = new RegistrationMailSender(Options.Create(configured), outbox, registry,
            new SingleClientFactory(new HttpClient(handler)), new OperatorTombstoneRegistry(Options.Create(configured)));

        Assert.True(await sender.ProcessOneAsync());
        Assert.Null(outbox.GetPending(DateTime.UtcNow.AddMinutes(9)));
        var retry = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(DateTime.UtcNow.AddMinutes(11)));
        Assert.Equal(original.Secret, retry.Secret);
        Assert.Equal(1, retry.Attempts);
    }

    [Fact]
    public async Task RejectedAccessCodeEmailRemainsRetryableWithItsProvisionedCredential()
    {
        var (_, outbox, configured) = CreateServices();
        configured.Registration.MailConfigurationPath = Path.Combine(directory, "mail.json");
        await File.WriteAllTextAsync(configured.Registration.MailConfigurationPath,
            "{\"apiKey\":\"secret\",\"from\":\"FT-ITC <mist@ft-itc.org>\",\"replyTo\":\"support@ft-itc.org\"}");
        var now = DateTime.UtcNow;
        var submission = outbox.Submit("Test User", "user@example.org", null, "terms", "privacy", now);
        var activation = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(now.AddSeconds(1)));
        outbox.MarkSent(submission.RegistrationId, RegistrationMessageKinds.Activation, now);
        var access = Assert.IsType<PendingRegistrationDelivery>(outbox.BeginActivation(activation.Secret, DateTime.UtcNow));
        var handler = new RecordingHandler { StatusCode = HttpStatusCode.BadRequest };
        var registry = new OperatorCodeRegistry(Options.Create(configured), NullLogger<OperatorCodeRegistry>.Instance,
            new OperatorTombstoneRegistry(Options.Create(configured)));
        var sender = new RegistrationMailSender(Options.Create(configured), outbox, registry,
            new SingleClientFactory(new HttpClient(handler)), new OperatorTombstoneRegistry(Options.Create(configured)));

        Assert.True(await sender.ProcessOneAsync());
        Assert.True(registry.Authenticate("Bearer " + access.Secret).IsAuthorized);
        var retry = Assert.IsType<PendingRegistrationDelivery>(outbox.GetPending(DateTime.UtcNow.AddMinutes(11)));
        Assert.Equal(access.Secret, retry.Secret);
        Assert.Equal(1, retry.Attempts);

        outbox.MarkRetry(access.RegistrationId, 4, DateTime.UtcNow.AddSeconds(-1), "provider_unavailable");
        Assert.True(await sender.ProcessOneAsync());
        Assert.Null(outbox.GetPending(DateTime.UtcNow.AddDays(1)));
        using var db = OpenDatabase();
        Assert.Equal("failed", Scalar<string>(db, "SELECT state FROM registration_accounts"));
    }

    (SelfRegistrationStore Store, RegistrationDeliveryOutbox Outbox, InterpretationOptions Options) CreateServices()
    {
        Directory.CreateDirectory(directory);
        var configured = Configuration(Path.Combine(directory, "registration.db"));
        var values = Options.Create(configured);
        var store = new SelfRegistrationStore(values);
        var protection = DataProtectionProvider.Create(new DirectoryInfo(configured.Registration.DataProtectionKeysPath),
            builder => builder.SetApplicationName("FT-ITC.PublicRegistration"));
        return (store, new RegistrationDeliveryOutbox(store, protection, values), configured);
    }

    InterpretationOptions Configuration(string databasePath) => new()
    {
        Registration = new RegistrationOptions
        {
            Enabled = true, DatabasePath = databasePath,
            DataProtectionKeysPath = Path.Combine(directory, "keys"),
            OperatorRegistryPath = Path.Combine(directory, "registered-codes.json"),
            ActivationLifetimeHours = 24, ResendCooldownMinutes = 15,
        },
        OperatorAccess = new InterpretationOperatorOptions
        {
            Enabled = true, RegistryPath = Path.Combine(directory, "operator-codes.json"),
            PresetRegistryPath = Path.Combine(directory, "presets.json"),
        },
        TombstonePath = Path.Combine(directory, "tombstones.json"),
    };

    SqliteConnection OpenDatabase()
    {
        var db = new SqliteConnection($"Data Source={Path.Combine(directory, "registration.db")}"); db.Open(); return db;
    }

    void InsertLegacyDelivery(SelfRegistrationStore store, string accountState, string protectedSecret, string deliveryState)
    {
        using var db = store.Open(); using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO registration_accounts
              (id,name,email,normalized_email,organization,state,terms_version,privacy_version,accepted_at_utc,created_at_utc)
            VALUES ('legacy','Legacy','legacy@example.org','legacy@example.org',NULL,$accountState,'terms','privacy',$now,$now);
            CREATE TABLE registration_delivery (
              registration_id TEXT PRIMARY KEY,protected_code TEXT NOT NULL,idempotency_key TEXT NOT NULL UNIQUE,
              state TEXT NOT NULL,attempts INTEGER NOT NULL DEFAULT 0,next_attempt_utc TEXT NOT NULL,
              expires_at_utc TEXT NULL,last_failure_code TEXT NULL);
            INSERT INTO registration_delivery VALUES ('legacy',$secret,'legacy-key',$deliveryState,0,$now,NULL,NULL);
            """;
        command.Parameters.AddWithValue("$accountState", accountState);
        command.Parameters.AddWithValue("$deliveryState", deliveryState);
        command.Parameters.AddWithValue("$secret", protectedSecret);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.AddMinutes(-1).ToString("O"));
        command.ExecuteNonQuery();
    }

    string ProtectWithDifferentKey(string value)
    {
        var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "old-keys")),
            builder => builder.SetApplicationName("FT-ITC.PublicRegistration"));
        return protection.CreateProtector("FT-ITC.PublicRegistration.BearerCode.v1").Protect(value);
    }

    static T Scalar<T>(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand(); command.CommandText = sql; return (T)command.ExecuteScalar()!;
    }

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static async Task<HttpResponseMessage> PostRegistration(HttpClient client, string email)
    {
        var token = await client.GetFromJsonAsync<AntiforgeryToken>("/api/viewer/token");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/registration")
        {
            Content = JsonContent.Create(new
            {
                name = "Original Name", email, organisation = "Original Lab",
                acceptedTerms = true, acknowledgedPrivacy = true,
                termsVersion = "terms", privacyVersion = "privacy", turnstileToken = "verified",
            }),
        };
        request.Headers.Add("X-CSRF-TOKEN", token!.RequestToken);
        return await client.SendAsync(request);
    }

    sealed record AntiforgeryToken(string RequestToken);

    sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    sealed class RecordingHandler : HttpMessageHandler
    {
        public List<JsonElement> Messages { get; } = [];
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json); Messages.Add(document.RootElement.Clone());
            return new HttpResponseMessage(StatusCode) { Content = new StringContent("{}") };
        }
    }

    sealed class RegistrationApplicationFactory : WebApplicationFactory<Program>
    {
        readonly string directory;
        public RegistrationApplicationFactory(string directory)
        {
            this.directory = directory; Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "turnstile.json"), "{\"secretKey\":\"test-secret\"}");
            File.WriteAllText(Path.Combine(directory, "registration-service.json"), "{\"Enabled\":true}");
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Interpretation:Registration:Enabled"] = "true",
                ["Interpretation:Registration:DatabasePath"] = Path.Combine(directory, "registration.db"),
                ["Interpretation:Registration:DataProtectionKeysPath"] = Path.Combine(directory, "keys"),
                ["Interpretation:Registration:SecretConfigurationPath"] = Path.Combine(directory, "turnstile.json"),
                ["Interpretation:Registration:MailConfigurationPath"] = Path.Combine(directory, "missing-mail.json"),
                ["Interpretation:Registration:AvailabilityPolicyPath"] = Path.Combine(directory, "registration-service.json"),
                ["Interpretation:Registration:TermsVersion"] = "terms",
                ["Interpretation:Registration:PrivacyVersion"] = "privacy",
                ["Interpretation:OperatorAccess:RegistryPath"] = Path.Combine(directory, "operator-codes.json"),
                ["Interpretation:OperatorAccess:PresetRegistryPath"] = Path.Combine(directory, "presets.json"),
                ["Interpretation:TombstonePath"] = Path.Combine(directory, "tombstones.json"),
                ["Interpretation:UsageLog:DatabasePath"] = Path.Combine(directory, "usage.db"),
            }));
            builder.ConfigureServices(services => services.AddSingleton<TurnstileVerifier, AcceptingTurnstileVerifier>());
        }
    }

    sealed class AcceptingTurnstileVerifier : TurnstileVerifier
    {
        public AcceptingTurnstileVerifier(IOptions<InterpretationOptions> options, IHttpClientFactory clients)
            : base(options, clients) { }
        public override Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken cancellationToken) => Task.FromResult(true);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
