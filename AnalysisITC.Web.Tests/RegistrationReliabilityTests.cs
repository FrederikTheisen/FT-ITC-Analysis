using System.Diagnostics;
using System.Net;
using AnalysisITC.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnalysisITC.Web.Tests;

public sealed class RegistrationReliabilityTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-reliability-" + Guid.NewGuid().ToString("N"));
    readonly InterpretationOptions configuration;
    readonly OperatorTombstoneRegistry markers;
    readonly OperatorCodeRegistry registry;
    readonly SelfRegistrationStore store;
    readonly RegistrationDeliveryOutbox outbox;
    readonly RecordingMail handler = new();
    readonly HttpClient client;
    readonly RegistrationMailSender sender;

    public RegistrationReliabilityTests()
    {
        Directory.CreateDirectory(directory);
        configuration = new()
        {
            OperatorAccess = new() { Enabled = true, RegistryPath = Path.Combine(directory, "manual.json") },
            Registration = new()
            {
                Enabled = true, DatabasePath = Path.Combine(directory, "registration.db"),
                OperatorRegistryPath = Path.Combine(directory, "registered.json"),
                MailConfigurationPath = Path.Combine(directory, "mail.json"),
                DataProtectionKeysPath = Path.Combine(directory, "keys"),
                AvailabilityPolicyPath = Path.Combine(directory, "policy.json"),
            },
            TombstonePath = Path.Combine(directory, "markers.json"),
        };
        var options = Options.Create(configuration);
        markers = new(options); registry = new(options, NullLogger<OperatorCodeRegistry>.Instance, markers);
        store = new(options);
        outbox = new(store, DataProtectionProvider.Create(new DirectoryInfo(configuration.Registration.DataProtectionKeysPath)), options, registry);
        client = new(handler); sender = new(options, outbox, registry, new Factory(client), markers);
        File.WriteAllText(configuration.Registration.MailConfigurationPath, "{\"ApiKey\":\"fake\"}");
    }

    [Theory]
    [InlineData("missing", false)] [InlineData("malformed", false)] [InlineData("null", false)] [InlineData("unreadable", false)]
    [InlineData("missing", true)] [InlineData("malformed", true)] [InlineData("null", true)] [InlineData("unreadable", true)]
    [InlineData("empty-key", false)] [InlineData("invalid-from", false)]
    public async Task MailSettingsOutagePreservesQueueAndDoesNotProvisionBeforeRepair(string fault, bool accessCode)
    {
        var first = Queue("first", DateTime.UtcNow.AddMinutes(-2));
        if (accessCode)
        {
            outbox.MarkSent(first, DateTime.UtcNow);
            outbox.BeginActivation(first.Secret, DateTime.UtcNow);
        }
        Queue("second", DateTime.UtcNow);
        Fault(configuration.Registration.MailConfigurationPath, fault);
        var before = DeliverySnapshot();
        for (var i = 0; i < 6; i++) Assert.False(await sender.ProcessOneAsync());
        Assert.Equal(before, DeliverySnapshot());
        Assert.Empty(registry.List()); Assert.Equal(0, handler.Sent);
        Repair(configuration.Registration.MailConfigurationPath, "{\"ApiKey\":\"fake\"}");
        Assert.True(await sender.ProcessOneAsync()); Assert.True(await sender.ProcessOneAsync());
        Assert.Equal(2, handler.Sent);
        Assert.Equal(accessCode ? 1 : 0, registry.List().Count);
    }

    [Theory]
    [InlineData("malformed")] [InlineData("null")] [InlineData("unreadable")]
    public void FailedMarkerReadsDenyAuthenticationAndPreserveBytesOnAdd(string fault)
    {
        var account = registry.Create("Account", null, true);
        markers.Add(account.Record.Id);
        Fault(configuration.TombstonePath, fault);
        var before = ReadEvenIfDenied(configuration.TombstonePath);
        Assert.False(registry.Authenticate("Bearer " + account.Code).IsAuthorized);
        Assert.Null(registry.FindActive(account.Code));
        Assert.ThrowsAny<IOException>(() => markers.Contains(account.Record.Id));
        Assert.ThrowsAny<IOException>(() => markers.Add("another-marker"));
        Assert.Equal(before, ReadEvenIfDenied(configuration.TombstonePath));
    }

    [Fact]
    public async Task ProvisioningAndDeliveryDoNotNeedWriteAccessToManualRegistryDirectory()
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var protectedDirectory = Path.Combine(directory, "configuration");
        Directory.CreateDirectory(protectedDirectory);
        configuration.OperatorAccess.RegistryPath = Path.Combine(protectedDirectory, "manual.json");
        File.WriteAllText(configuration.OperatorAccess.RegistryPath, "[]");
        var first = Queue("first", DateTime.UtcNow.AddMinutes(-2));
        outbox.MarkSent(first, DateTime.UtcNow.AddMinutes(-1));
        outbox.BeginActivation(first.Secret, DateTime.UtcNow.AddMinutes(-1));
        var accessCode = outbox.GetPending(DateTime.UtcNow)!;
        Queue("second", DateTime.UtcNow);
        // Model the service's read/search access to the root-owned configuration directory.
        File.SetUnixFileMode(protectedDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => File.WriteAllText(Path.Combine(protectedDirectory, "write-probe"), ""));
            Assert.True(await sender.ProcessOneAsync());
            Assert.True(await sender.ProcessOneAsync());
            Assert.Equal(2, handler.Sent);
            Assert.Null(outbox.GetPending(DateTime.UtcNow));
            Assert.True(registry.Authenticate("Bearer " + accessCode.Secret).IsAuthorized);
            Assert.Equal("[]", File.ReadAllText(configuration.OperatorAccess.RegistryPath));
            Assert.Equal(new[] { configuration.OperatorAccess.RegistryPath }, Directory.GetFiles(protectedDirectory));
        }
        finally
        {
            File.SetUnixFileMode(protectedDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public static IEnumerable<object[]> MutationFaults() =>
        from fault in new[] { "malformed", "null", "unreadable" }
        from action in new[] { "create", "provision", "revoke", "tier", "details", "quota", "regenerate", "scrub" }
        select new object[] { fault, action };

    [Theory]
    [MemberData(nameof(MutationFaults))]
    public void FailedMarkerReadRejectsEveryOperatorMutation(string fault, string action)
    {
        var account = registry.Create("Account", null, true);
        File.WriteAllText(configuration.Registration.OperatorRegistryPath, "[]");
        File.WriteAllText(configuration.TombstonePath, "[]"); Fault(configuration.TombstonePath, fault);
        var beforeManual = File.ReadAllBytes(configuration.OperatorAccess.RegistryPath);
        var beforeRegistered = File.ReadAllBytes(configuration.Registration.OperatorRegistryPath);
        Assert.ThrowsAny<IOException>(() => Mutate(action, account.Record.Id));
        Assert.Equal(beforeManual, File.ReadAllBytes(configuration.OperatorAccess.RegistryPath));
        Assert.Equal(beforeRegistered, File.ReadAllBytes(configuration.Registration.OperatorRegistryPath));
    }

    [Theory]
    [InlineData("malformed")] [InlineData("null")] [InlineData("unreadable")]
    public async Task FailedMarkerReadDefersMailWithoutSpendingAttempts(string fault)
    {
        var first = Queue("first", DateTime.UtcNow);
        markers.Add(first.RegistrationId); Fault(configuration.TombstonePath, fault);
        var before = DeliverySnapshot();
        Assert.False(await sender.ProcessOneAsync());
        Assert.Equal(before, DeliverySnapshot()); Assert.Equal(0, handler.Sent);
        Repair(configuration.TombstonePath, "[]");
        Assert.True(await sender.ProcessOneAsync()); Assert.Equal(1, handler.Sent);
    }

    [Fact]
    public void CamelCaseSettingsAreReadAndValidatedByHealth()
    {
        File.WriteAllText(configuration.Registration.MailConfigurationPath, "{\"apiKey\":\"fake\",\"from\":\"test@example.invalid\"}");
        Assert.Equal(HealthCheckState.Pass, HealthCheckService.CheckRegistrationMail(configuration.Registration.MailConfigurationPath).State);
    }

    [Theory]
    [InlineData("missing")] [InlineData("malformed")] [InlineData("null")] [InlineData("unreadable")]
    [InlineData("empty-key")] [InlineData("invalid-from")]
    public void MailHealthReportsTheSameSettingsFailuresAsDelivery(string fault)
    {
        Fault(configuration.Registration.MailConfigurationPath, fault);
        var result = HealthCheckService.CheckRegistrationMail(configuration.Registration.MailConfigurationPath);
        Assert.Equal(HealthCheckState.Fail, result.State);
        Assert.Contains("waiting for repair", result.Reason);
    }

    [Fact]
    public async Task ChildProcessCannotKeepAReleasedRegistryLockAlive()
    {
        using var child = new Process { StartInfo = new ProcessStartInfo("/usr/bin/perl")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
        child.StartInfo.ArgumentList.Add("-e");
        child.StartInfo.ArgumentList.Add("$|=1; print \"READY\\n\"; sleep 60;");
        var started = false;
        try
        {
            using (RegistryFileLock.Acquire(configuration.OperatorAccess.RegistryPath))
            {
                started = child.Start(); Assert.True(started);
                Assert.Equal("READY", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            }
            using var reacquired = RegistryFileLock.Acquire(configuration.OperatorAccess.RegistryPath);
            Assert.False(child.HasExited);
        }
        finally
        {
            if (started && !child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
        }
    }

    [Fact]
    public void PartialLockFailureReleasesEarlierLocksAndLeavesFilesUnchanged()
    {
        var created = registry.Create("Account", null, true);
        var original = File.ReadAllBytes(configuration.OperatorAccess.RegistryPath);
        using (RegistryFileLock.Acquire(configuration.Registration.OperatorRegistryPath))
        {
            var watch = Stopwatch.StartNew();
            Assert.Throws<RegistryLockTimeoutException>(() => registry.Revoke(created.Record.Id));
            Assert.InRange(watch.Elapsed.TotalSeconds, 4.9, 8);
            Assert.Equal(original, File.ReadAllBytes(configuration.OperatorAccess.RegistryPath));
            // The failed two-file acquisition must release the first file's lock.
            var manualLock = Directory.GetFiles(directory, "operator-*.lock").Single();
            using var first = RegistryFileLock.Acquire(manualLock[..^".lock".Length]);
        }
        Assert.True(registry.Revoke(created.Record.Id));
        Assert.False(registry.Authenticate("Bearer " + created.Code).IsAuthorized);
    }

    [Fact]
    public async Task WorkerDrainsEligibleMailWithoutMinuteGaps()
    {
        Queue("first", DateTime.UtcNow.AddSeconds(-3)); Queue("second", DateTime.UtcNow.AddSeconds(-2)); Queue("third", DateTime.UtcNow.AddSeconds(-1));
        using var services = new ServiceCollection().AddLogging().AddSingleton(new RegistrationRetentionService(store)).AddSingleton(sender).BuildServiceProvider();
        using var worker = new RegistrationDeliveryWorker(services, Options.Create(configuration));
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var time = Stopwatch.StartNew();
            while (handler.Sent < 3 && time.Elapsed < TimeSpan.FromSeconds(8)) await Task.Delay(20);
            Assert.Equal(3, handler.Sent);
        }
        finally { await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    void Mutate(string action, string id)
    {
        switch (action)
        {
            case "create": registry.Create("New", null, true); break;
            case "provision": registry.CreateRegisteredWithCode("new-id", "New", "new@example.invalid", null, "ftitc_op_new"); break;
            case "revoke": registry.Revoke(id); break;
            case "tier": registry.ChangeTier(id, InterpretationAccessTiers.Standard); break;
            case "details": registry.ChangeDetails(id, "Changed", null, null); break;
            case "quota": registry.ChangeQuota(id, 7, false); break;
            case "regenerate": registry.Regenerate(id); break;
            case "scrub": registry.Scrub(id); break;
            default: throw new ArgumentException(action);
        }
    }
    PendingRegistrationDelivery Queue(string name, DateTime now)
    {
        outbox.Submit(name, name + "@example.invalid", null, "terms", "privacy", now);
        return outbox.GetPending(now)!;
    }
    string DeliverySnapshot()
    {
        using var db = store.Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT registration_id,kind,state,attempts,next_attempt_utc,provisioning_succeeded,last_failure_code FROM registration_delivery ORDER BY registration_id";
        using var reader = cmd.ExecuteReader(); var rows = new List<string>();
        while (reader.Read()) rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(reader.GetValue)));
        return string.Join("\n", rows);
    }
    static void Fault(string path, string fault)
    {
        switch (fault)
        {
            case "missing": File.Delete(path); break;
            case "malformed": File.WriteAllText(path, "{broken"); break;
            case "null": File.WriteAllText(path, "null"); break;
            case "empty-key": File.WriteAllText(path, "{\"ApiKey\":\" \"}"); break;
            case "invalid-from": File.WriteAllText(path, "{\"ApiKey\":\"fake\",\"From\":\"bad\"}"); break;
            case "unreadable":
                if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                File.SetUnixFileMode(path, UnixFileMode.None);
                Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllText(path)); break;
        }
    }
    static byte[] ReadEvenIfDenied(string path)
    {
        if (OperatingSystem.IsWindows()) return File.ReadAllBytes(path);
        var mode = File.GetUnixFileMode(path); File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        try { return File.ReadAllBytes(path); } finally { File.SetUnixFileMode(path, mode); }
    }
    static void Repair(string path, string text)
    {
        if (!OperatingSystem.IsWindows() && File.Exists(path)) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.WriteAllText(path, text);
    }
    public void Dispose() { client.Dispose(); Directory.Delete(directory, true); }
    sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    sealed class RecordingMail : HttpMessageHandler
    {
        int sent;
        public int Sent => Volatile.Read(ref sent);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref sent); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }
    }
}
