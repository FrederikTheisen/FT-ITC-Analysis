using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AnalysisITC.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

// Investigation only: references the production assembly unchanged. All storage is disposable.
var mode = args[0];
var root = Path.GetFullPath(args[1]);
if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The investigation uses Unix permissions and FIFOs.");
if (!root.StartsWith("/private/tmp/registration-reliability-", StringComparison.Ordinal))
    throw new ArgumentException("Use a /private/tmp/registration-reliability-* directory.");
Directory.CreateDirectory(root);
var options = Fixture.Configure(root);
var values = Options.Create(options);
var markers = new OperatorTombstoneRegistry(values);
var registry = new OperatorCodeRegistry(values, NullLogger<OperatorCodeRegistry>.Instance, markers);
switch (mode)
{
    case "seed":
        var manual = registry.Create("Manual", null, true, name: "Original", email: "manual@example.invalid");
        registry.CreateRegisteredWithCode("11111111111111111111111111111111", "Registered", "registered@example.invalid", null, "ftitc_op_registered_fixture");
        Emit(new { manualId = manual.Record.Id, manual.Code, registeredId = "11111111111111111111111111111111" });
        break;
    case "mutate":
        var action = args[2]; var id = args[3];
        Console.WriteLine("START"); Console.Out.Flush();
        if (action == "provision")
            Emit(registry.CreateRegisteredWithCode("22222222222222222222222222222222", "New", "new@example.invalid", null, "ftitc_op_new_fixture"));
        else if (action == "regenerate") Emit(registry.Regenerate(id));
        else if (action == "scrub") Emit(registry.Scrub(id));
        else if (action == "marker") Emit(markers.Add(id));
        else
        {
            using var services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
            var command = action switch
            {
                "create" => new[] { "operator-code", "create", "--label", "Concurrent", "--no-expiry" },
                "details" => new[] { "operator-code", "set-details", id, "--name", "Changed", "--email", "changed@example.invalid" },
                "tier" => new[] { "operator-code", "set-tier", id, "standard" },
                "quota" => new[] { "operator-code", "set-quota", id, "7" },
                _ => new[] { "operator-code", action, id },
            };
            var output = new StringWriter(); var error = new StringWriter();
            var exitCode = await InterpretationAdminCommands.RunAsync(command, services, output, error);
            Emit(new { exitCode, output = output.ToString(), error = error.ToString() });
            if (exitCode != 0) Environment.ExitCode = exitCode;
        }
        break;
    case "auth": Emit(new { authorized = registry.Authenticate("Bearer " + args[2]).IsAuthorized }); break;
    case "faults": await Faults(root); break;
    case "worker": await Worker(root); break;
    case "worker-outage": await WorkerOutage(root); break;
    default: throw new ArgumentException(mode);
}

static void Emit(object? value) => Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { IncludeFields = true }));

static async Task Faults(string root)
{
    foreach (var kind in new[] { "activation", "access-code" })
    foreach (var fault in new[] { "missing", "malformed", "null", "unreadable" })
    {
        using var f = new Fixture(Path.Combine(root, "mail-" + kind + "-" + fault));
        var first = f.Queue("first", DateTime.UtcNow.AddMinutes(-2));
        if (kind == "access-code")
        {
            f.Activate(first);
            f.Sql("UPDATE registration_delivery SET next_attempt_utc='" + DateTime.UtcNow.AddMinutes(-2).ToString("O") + "'");
        }
        f.Queue("second", DateTime.UtcNow.AddMinutes(-1));
        var path = f.Options.Registration.MailConfigurationPath;
        Inject(path, fault);
        var before = f.Rows(); var failures = new List<string>();
        for (var i = 0; i < 6; i++)
            try { await f.Sender.ProcessOneAsync(); failures.Add("none"); }
            catch (Exception e) { failures.Add(e.GetType().Name); }
        var after = f.Rows();
        var provisioned = f.Registry.List().Any(x => x.Id == first.RegistrationId);
        var noRequests = f.Handler.Events.Count == 0;
        Repair(path); await f.Sender.ProcessOneAsync(); await f.Sender.ProcessOneAsync();
        Emit(new { test = "mail", kind, fault, before, after, failures, provisioned, noRequests,
            repaired = f.Rows(), sent = f.Handler.Events.Count });
    }
    foreach (var fault in new[] { "absent", "valid", "malformed", "null", "unreadable" })
    {
        using var f = new Fixture(Path.Combine(root, "markers-" + fault));
        var pending = f.Queue("marked", DateTime.UtcNow.AddMinutes(-1));
        var manual = f.Registry.Create("Manual", null, true);
        f.Markers.Add(manual.Record.Id); f.Markers.Add(pending.RegistrationId);
        var scrubbed = f.Registry.Create("Scrubbed", null, true);
        f.Registry.Scrub(scrubbed.Record.Id);
        var path = f.Options.TombstonePath;
        var original = File.ReadAllText(path);
        if (fault == "absent") File.Delete(path);
        else if (fault != "valid") Inject(path, fault);
        var blocked = !f.Registry.Authenticate("Bearer " + manual.Code).IsAuthorized;
        var scrubbedDenied = !f.Registry.Authenticate("Bearer " + scrubbed.Code).IsAuthorized;
        bool? contains = null; string? containsError = null;
        try { contains = f.Markers.Contains(manual.Record.Id); } catch (Exception e) { containsError = e.GetType().Name; }
        await f.Sender.ProcessOneAsync();
        var beforeAdd = fault == "unreadable" ? "permission-denied" : File.Exists(path) ? File.ReadAllText(path) : "absent";
        string? addError = null;
        try { f.Markers.Add("33333333333333333333333333333333"); } catch (Exception e) { addError = e.GetType().Name; }
        if (File.Exists(path)) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Emit(new { test = "markers", fault, contains, containsError, blocked, scrubbedDenied, sent = f.Handler.Events.Count,
            delivery = f.Rows(), original, beforeAdd, afterAdd = File.Exists(path) ? File.ReadAllText(path) : "absent", addError });
    }
    // Observable controls for payload isolation, delayed rows, expiry, and shutdown during HTTP.
    using (var f = new Fixture(Path.Combine(root, "payload")))
    {
        var bad = f.Queue("bad", DateTime.UtcNow.AddMinutes(-2)); f.Queue("good", DateTime.UtcNow.AddMinutes(-1));
        f.Sql("UPDATE registration_delivery SET protected_code='unreadable' WHERE registration_id='" + bad.RegistrationId + "'");
        var first = await f.Sender.ProcessOneAsync(); var second = await f.Sender.ProcessOneAsync();
        Emit(new { test = "payload", first, second, rows = f.Rows(), sent = f.Handler.Events.Count });
    }
    using (var f = new Fixture(Path.Combine(root, "delayed")))
    {
        var delayed = f.Queue("delayed", DateTime.UtcNow.AddMinutes(-2));
        f.Outbox.MarkRetry(delayed, 1, DateTime.UtcNow.AddMinutes(10), "provider_unavailable");
        f.Queue("eligible", DateTime.UtcNow.AddMinutes(-1)); await f.Sender.ProcessOneAsync();
        Emit(new { test = "delayed", rows = f.Rows(), sent = f.Handler.Events.Count });
    }
    using (var f = new Fixture(Path.Combine(root, "expiry")))
    {
        var queuedAt = DateTime.UtcNow.AddHours(-24); f.Queue("expired", queuedAt);
        Emit(new { test = "expiry", beforeBoundary = f.Outbox.GetPending(queuedAt.AddHours(24).AddTicks(-1)) is not null,
            atBoundary = f.Outbox.GetPending(queuedAt.AddHours(24)) is not null });
    }
    using (var f = new Fixture(Path.Combine(root, "cancel")))
    {
        f.Queue("cancel", DateTime.UtcNow); f.Handler.Block = true;
        using var services = f.Services(); using var worker = new RegistrationDeliveryWorker(services, Options.Create(f.Options));
        await worker.StartAsync(CancellationToken.None); await f.Handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var timer = Stopwatch.StartNew(); await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Emit(new { test = "cancel", stopMilliseconds = timer.Elapsed.TotalMilliseconds, rows = f.Rows() });
    }
}

static void Inject(string path, string fault)
{
    if (fault == "missing") { File.Delete(path); return; }
    if (fault == "malformed") File.WriteAllText(path, "[{broken-json");
    if (fault == "null") File.WriteAllText(path, "null");
    if (fault == "unreadable")
    {
        File.SetUnixFileMode(path, UnixFileMode.None);
        try { File.ReadAllText(path); throw new Exception("Permission fixture did not deny reads"); }
        catch (UnauthorizedAccessException) { }
    }
}
static void Repair(string path)
{
    if (File.Exists(path)) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    File.WriteAllText(path, "{\"ApiKey\":\"synthetic-never-sent\"}");
}

static async Task Worker(string root)
{
    using var f = new Fixture(root);
    using var services = f.Services();
    using var worker = new RegistrationDeliveryWorker(services, Options.Create(f.Options));
    var start = DateTime.UtcNow; var timer = Stopwatch.StartNew(); var next = 0; var completed = new List<object>();
    var activated = new HashSet<string>();
    f.Queue("load-0", DateTime.UtcNow); next++;
    await worker.StartAsync(CancellationToken.None);
    while (timer.Elapsed.TotalSeconds < 735)
    {
        while (next < 100 && timer.Elapsed.TotalSeconds >= next * 6)
        { f.Queue("load-" + next, DateTime.UtcNow); next++; }
        foreach (var e in f.Handler.Events.ToArray())
        {
            if (e.Token is null || activated.Contains(e.Id)) continue;
            var access = f.Outbox.BeginActivation(e.Token, DateTime.UtcNow);
            if (access is not null) activated.Add(e.Id);
        }
        while (f.Handler.Events.Count > completed.Count)
        {
            var e = f.Handler.Events.ToArray()[completed.Count];
            var row = f.Queued[e.Id];
            // Handler captured the due timestamp before HTTP success can replace activation with access-code.
            var result = new { test = "dispatch", elapsedSeconds = (e.At - start).TotalSeconds,
                kind = e.Token is null ? "access-code" : "activation", e.Id,
                queueWaitSeconds = (e.At - e.EligibleAt).TotalSeconds };
            completed.Add(result); Emit(result);
        }
        await Task.Delay(20);
    }
    var stop = Stopwatch.StartNew(); await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    var rows = f.Rows();
    Emit(new { test = "worker-summary", arrivals = next, elapsedSeconds = timer.Elapsed.TotalSeconds,
        messages = completed.Count, activations = activated.Count, stopMilliseconds = stop.Elapsed.TotalMilliseconds,
        rows, drainCompleted = next == 100 && f.Handler.Events.Count == 200,
        drainSeconds = f.Handler.Events.Count == 200 ? (f.Handler.Events.Last().At - start).TotalSeconds : (double?)null,
        fakeProvider = "immediate local HTTP 200; no network" });
}

static async Task WorkerOutage(string root)
{
    using var f = new Fixture(root);
    f.Queue("first", DateTime.UtcNow.AddMinutes(-2)); f.Queue("second", DateTime.UtcNow.AddMinutes(-1));
    File.Delete(f.Options.Registration.MailConfigurationPath);
    using var services = f.Services();
    using var worker = new RegistrationDeliveryWorker(services, Options.Create(f.Options));
    var timer = Stopwatch.StartNew(); var before = f.Rows();
    await worker.StartAsync(CancellationToken.None);
    await Task.Delay(TimeSpan.FromSeconds(65));
    Emit(new { test = "worker-outage", elapsedSeconds = timer.Elapsed.TotalSeconds, before, after = f.Rows(), sent = f.Handler.Events.Count });
    Repair(f.Options.Registration.MailConfigurationPath);
    Emit(new { test = "worker-repaired", elapsedSeconds = timer.Elapsed.TotalSeconds });
    while (timer.Elapsed.TotalSeconds < 190 && f.Handler.Events.Count < 2) await Task.Delay(20);
    await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    Emit(new { test = "worker-recovered", elapsedSeconds = timer.Elapsed.TotalSeconds, rows = f.Rows(), sent = f.Handler.Events.Count });
}

sealed class Fixture : IDisposable
{
    public InterpretationOptions Options { get; }
    public SelfRegistrationStore Store { get; }
    public RegistrationDeliveryOutbox Outbox { get; }
    public OperatorCodeRegistry Registry { get; }
    public OperatorTombstoneRegistry Markers { get; }
    public RegistrationMailSender Sender { get; }
    public FakeMail Handler { get; }
    public ConcurrentDictionary<string, DateTime> Queued { get; } = new();
    readonly HttpClient client;
    public Fixture(string root)
    {
        Directory.CreateDirectory(root); Options = Configure(root); var values = Microsoft.Extensions.Options.Options.Create(Options);
        Store = new(values); Markers = new(values);
        Registry = new(values, NullLogger<OperatorCodeRegistry>.Instance, Markers);
        var protection = DataProtectionProvider.Create(new DirectoryInfo(Options.Registration.DataProtectionKeysPath), b => b.SetApplicationName("FT-ITC.PublicRegistration"));
        Outbox = new(Store, protection, values, Registry);
        Handler = new FakeMail(this); client = new HttpClient(Handler);
        Sender = new(values, Outbox, Registry, new Factory(client), Markers);
        File.WriteAllText(Options.Registration.MailConfigurationPath, "{\"ApiKey\":\"synthetic-never-sent\"}");
    }
    public static InterpretationOptions Configure(string root) => new()
    {
        Registration = new() { Enabled = true, DatabasePath = Path.Combine(root, "registration.db"),
            OperatorRegistryPath = Path.Combine(root, "registered.json"), DataProtectionKeysPath = Path.Combine(root, "keys"),
            MailConfigurationPath = Path.Combine(root, "mail.json"), SecretConfigurationPath = Path.Combine(root, "secret.json"),
            AvailabilityPolicyPath = Path.Combine(root, "registration-policy.json") },
        OperatorAccess = new() { Enabled = true, RegistryPath = Path.Combine(root, "manual.json"), PresetRegistryPath = Path.Combine(root, "presets.json") },
        TombstonePath = Path.Combine(root, "markers.json"), PublicAccessRegistryPath = Path.Combine(root, "public.json"),
        UsageLog = new() { DatabasePath = Path.Combine(root, "usage.db") },
        AvailabilityPolicyPath = Path.Combine(root, "availability.json"), StatusEmailConfigurationPath = Path.Combine(root, "status.json"),
    };
    public PendingRegistrationDelivery Queue(string label, DateTime now)
    {
        var submitted = Outbox.Submit(label, label + "@example.invalid", null, "terms", "privacy", now);
        Queued[submitted.RegistrationId] = now;
        // Each fault probe queues the first row before later ones; the load probe only needs its id.
        return Outbox.GetPending(now) ?? throw new Exception("Expected a pending row");
    }
    public void Activate(PendingRegistrationDelivery pending)
    { Outbox.MarkSent(pending, DateTime.UtcNow); Outbox.BeginActivation(pending.Secret, DateTime.UtcNow); }
    public void Sql(string sql) { using var db = Store.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    public List<Dictionary<string, object?>> Rows()
    {
        using var db = Store.Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT registration_id,kind,state,attempts,next_attempt_utc,provisioning_succeeded,last_failure_code,expires_at_utc FROM registration_delivery ORDER BY next_attempt_utc";
        using var reader = cmd.ExecuteReader(); var rows = new List<Dictionary<string, object?>>();
        while (reader.Read()) rows.Add(Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName, i => reader.IsDBNull(i) ? null : reader.GetValue(i)));
        return rows;
    }
    public ServiceProvider Services() => new ServiceCollection().AddLogging(b => b.AddConsole())
        .AddSingleton(new RegistrationRetentionService(Store)).AddSingleton(Sender).BuildServiceProvider();
    public void Dispose() => client.Dispose();
    sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
}

sealed class FakeMail(Fixture fixture) : HttpMessageHandler
{
    public ConcurrentQueue<MailEvent> Events { get; } = new();
    public bool Block { get; set; }
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Started.TrySetResult();
        if (Block) await Task.Delay(Timeout.InfiniteTimeSpan, token);
        var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        var text = body.RootElement.GetProperty("text").GetString()!;
        var secret = Regex.Match(text, @"ftitc_act_[A-Za-z0-9_-]+");
        var pending = fixture.Outbox.GetPending(DateTime.UtcNow)!;
        var row = fixture.Rows().Single(x => (string)x["registration_id"]! == pending.RegistrationId);
        Events.Enqueue(new(pending.RegistrationId, DateTime.UtcNow, DateTime.Parse((string)row["next_attempt_utc"]!).ToUniversalTime(), secret.Success ? secret.Value : null));
        return new(HttpStatusCode.OK);
    }
}
sealed record MailEvent(string Id, DateTime At, DateTime EligibleAt, string? Token);
