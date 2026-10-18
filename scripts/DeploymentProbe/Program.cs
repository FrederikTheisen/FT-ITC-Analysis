// Loads each real deployed executable in a separate process. Only an explicitly
// marked rehearsal directory is accepted; this tool never opens production stores.
using System.Data.Common;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

if (args.Length is < 3 or > 4 || args[0] is not ("initialize" or "admit" or "verify" or "registry-create" or "registry-touch" or "registry-revoke"))
    throw new ArgumentException("Usage: DeploymentProbe MODE RELEASE REHEARSAL_DIRECTORY [REGISTRY_ID]");
var release = Path.GetFullPath(args[1]);
var directory = Path.GetFullPath(args[2]);
if (!File.Exists(Path.Combine(directory, ".ftitc-rehearsal")))
    throw new InvalidOperationException("Explicit rehearsal marker is required");
if (directory is "/var/lib/ftitc-web" or "/etc/ftitc-web")
    throw new InvalidOperationException("Production storage is forbidden");
AssemblyLoadContext.Default.Resolving += (_, name) =>
    File.Exists(Path.Combine(release, name.Name + ".dll")) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(release, name.Name + ".dll")) : null;
AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
{
    var extension = OperatingSystem.IsMacOS() ? ".dylib" : ".so";
    foreach (var path in new[] { Path.Combine(release, name), Path.Combine(release, "lib" + name + extension), Path.Combine(release, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", "lib" + name + extension) })
        if (File.Exists(path)) return NativeLibrary.Load(path);
    return IntPtr.Zero;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(release, "AnalysisITC.Web.dll"));
Type TypeOf(string name) => assembly.GetType("AnalysisITC.Web." + name, throwOnError: true)!;
void Set(object instance, string name, object? value)
{
    var property = instance.GetType().GetProperty(name);
    if (property is not null) property.SetValue(instance, value);
    else instance.GetType().GetField(name)!.SetValue(instance, value);
}
object Get(object instance, string name) => instance.GetType().GetProperty(name)!.GetValue(instance)!;
var options = Activator.CreateInstance(TypeOf("InterpretationOptions"))!;
Set(Get(options, "UsageLog"), "Enabled", true);
Set(Get(options, "UsageLog"), "DatabasePath", Path.Combine(directory, "usage.db"));
Set(options, "PublicAccessRegistryPath", Path.Combine(directory, "public.json"));
Set(options, "AvailabilityPolicyPath", Path.Combine(directory, "availability.json"));
Set(options, "TombstonePath", Path.Combine(directory, "tombstones.json"));
Set(Get(options, "OperatorAccess"), "RegistryPath", Path.Combine(directory, "operators.json"));
Set(Get(options, "OperatorAccess"), "PresetRegistryPath", Path.Combine(directory, "presets.json"));
Set(Get(options, "Registration"), "OperatorRegistryPath", Path.Combine(directory, "registered.json"));
Set(Get(options, "Registration"), "DatabasePath", Path.Combine(directory, "registration.db"));
var wrapped = typeof(Options).GetMethod("Create")!.MakeGenericMethod(options.GetType()).Invoke(null, new[] { options })!;
var dependencies = new Dictionary<Type, object> { [wrapped.GetType().GetInterfaces().Single(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IOptions<>))] = wrapped };
object Create(string name)
{
    var type = TypeOf(name);
    var loggerType = typeof(NullLogger<>).MakeGenericType(type);
    var logger = loggerType.GetProperty("Instance")?.GetValue(null) ?? loggerType.GetField("Instance")!.GetValue(null)!;
    dependencies[typeof(Microsoft.Extensions.Logging.ILogger<>).MakeGenericType(type)] = logger;
    foreach (var constructor in type.GetConstructors().OrderByDescending(constructor => constructor.GetParameters().Length))
    {
        var parameters = constructor.GetParameters();
        if (parameters.All(parameter => dependencies.ContainsKey(parameter.ParameterType) || parameter.HasDefaultValue))
        {
            var value = constructor.Invoke(parameters.Select(parameter => dependencies.GetValueOrDefault(parameter.ParameterType) ?? parameter.DefaultValue).ToArray());
            dependencies[type] = value;
            return value;
        }
    }
    throw new InvalidOperationException("No supported constructor for " + name);
}
var publicRegistry = Create("PublicAccessRegistry");
if (args[0].StartsWith("registry-", StringComparison.Ordinal))
{
    var method = args[0] switch { "registry-create" => "Create", "registry-touch" => "TryTouch", _ => "Revoke" };
    var parameters = method == "Create" ? Array.Empty<object>() : new object[] { args[3] };
    var result = publicRegistry.GetType().GetMethod(method)!.Invoke(publicRegistry, parameters)!;
    if (method == "Create")
    {
        var record = result.GetType().GetField("Item1")!.GetValue(result)!;
        Console.WriteLine(Get(record, "Id"));
    }
    else Console.WriteLine(method + "=" + result);
    return;
}
Create("OperatorTombstoneRegistry");
Create("OperatorCodeRegistry");
var store = Create("InterpretationUsageStore");
object? Invoke(string method, params object?[] values) => store.GetType().GetMethods().Single(member => member.Name == method && member.GetParameters().Length == values.Length).Invoke(store, values);
using var db = (DbConnection)Invoke("OpenForCommand")!;
object Scalar(string sql)
{
    using var command = db.CreateCommand(); command.CommandText = sql;
    return command.ExecuteScalar()!;
}
const string installation = "de000000000000000000000000000076";
if (args[0] == "admit")
{
    Invoke("EndMaintenance");
    for (var index = 1; index <= 2; index++)
    {
        var request = Activator.CreateInstance(TypeOf("InterpretationUsageRequest"))!;
        Set(request, "ServerExecutionId", "deployment-roundtrip-" + index);
        Set(request, "ClientRequestId", "deployment-roundtrip-client-" + index);
        Set(request, "OperatorCodeId", installation);
        Set(request, "AccessTier", "public");
        Set(request, "StartedUtc", DateTime.UtcNow);
        var result = Invoke("TryAdmit", request, null, DateTime.UtcNow.AddDays(-1))!;
        if (Get(result, "Status").ToString() != "Admitted") throw new InvalidOperationException("Previous executable did not admit the rehearsal request: " + Get(result, "Status"));
        if (index == 1)
        {
            Set(request, "CompletedUtc", DateTime.UtcNow);
            Set(request, "Outcome", "success");
            Set(request, "HttpStatus", 200);
            Invoke("FinalizeRequest", request);
        }
    }
    Console.WriteLine("previous-executable admissions=2; summaries=1; provider-dispatches=0");
}
else if (args[0] == "verify")
{
    var count = Convert.ToInt32(Scalar("SELECT count(*) FROM executions WHERE execution_id IN ('deployment-roundtrip-1','deployment-roundtrip-2') AND access_tier='public' AND request_count_eligible=1"));
    if (count != 2) throw new InvalidOperationException("Candidate did not classify/count both old-writer admissions");
    var usage = Invoke("GetPublicUsage", installation, DateTime.UtcNow.AddDays(-1), new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc))!;
    if (Convert.ToInt32(Get(usage, "RequestCount")) != 2) throw new InvalidOperationException("Public allowance did not count both admissions");
    Console.WriteLine("round-trip passed; classified-public=2; eligible=2; summaries=1");
}
else
{
    Console.WriteLine("candidate migration initialized; executions=" + Scalar("SELECT count(*) FROM executions"));
}
