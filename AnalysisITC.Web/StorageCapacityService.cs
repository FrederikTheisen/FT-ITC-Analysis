using System.Globalization;
using Microsoft.Extensions.Options;

namespace AnalysisITC.Web;

public enum StorageCapacityStatus
{
    Available,
    Unavailable,
}

public sealed record StorageCapacityVolume(string MountPoint, long TotalBytes, long AvailableBytes)
{
    public long UsedBytes => Math.Max(0, TotalBytes - AvailableBytes);
    public double UsedPercent => TotalBytes <= 0 ? 0 : UsedBytes * 100d / TotalBytes;
    public bool IsLow => AvailableBytes < StorageCapacityService.LowSpaceThresholdBytes;
}

public sealed record StorageCapacityReport(
    StorageCapacityStatus Status,
    IReadOnlyList<StorageCapacityVolume> Volumes,
    string? Detail = null)
{
    public bool HasAttention => Status == StorageCapacityStatus.Unavailable || Volumes.Any(volume => volume.IsLow);
}

/// <summary>Reads filesystem capacity without inspecting file contents or application data.</summary>
public sealed class StorageCapacityService
{
    public const long LowSpaceThresholdBytes = 10L * 1024 * 1024 * 1024;

    readonly IReadOnlyList<string> paths;
    readonly Func<string, StorageCapacityVolume> reader;

    public StorageCapacityService(IOptions<InterpretationOptions> options)
        : this(PathsFor(options.Value), ReadVolume)
    {
    }

    internal StorageCapacityService(IEnumerable<string> paths, Func<string, StorageCapacityVolume> reader)
    {
        this.paths = paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        this.reader = reader;
    }

    public StorageCapacityReport Read()
    {
        var volumes = new Dictionary<string, StorageCapacityVolume>(PathComparer());
        try
        {
            foreach (var path in paths)
            {
                var volume = reader(path);
                if (string.IsNullOrWhiteSpace(volume.MountPoint)
                    || volume.TotalBytes <= 0
                    || volume.AvailableBytes < 0
                    || volume.AvailableBytes > volume.TotalBytes)
                    throw new IOException("invalid storage capacity");

                var key = volume.MountPoint.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (key.Length == 0) key = Path.DirectorySeparatorChar.ToString();
                volumes.TryAdd(key, volume with { MountPoint = key });
            }

            if (volumes.Count == 0) return Unavailable();
            return new(StorageCapacityStatus.Available, volumes.Values.OrderBy(volume => volume.MountPoint, PathComparer()).ToArray());
        }
        catch
        {
            return Unavailable();
        }
    }

    internal static string FormatBytes(long bytes)
    {
        const double kib = 1024d;
        const double mib = kib * 1024d;
        const double gib = mib * 1024d;
        const double tib = gib * 1024d;
        if (bytes >= tib) return $"{(bytes / tib).ToString("0.0", CultureInfo.InvariantCulture)} TiB";
        if (bytes >= gib) return $"{(bytes / gib).ToString("0.0", CultureInfo.InvariantCulture)} GiB";
        if (bytes >= mib) return $"{(bytes / mib).ToString("0.0", CultureInfo.InvariantCulture)} MiB";
        if (bytes >= kib) return $"{(bytes / kib).ToString("0.0", CultureInfo.InvariantCulture)} KiB";
        return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";
    }

    static IReadOnlyList<string> PathsFor(InterpretationOptions options) =>
    [
        AppContext.BaseDirectory,
        options.StatusEmailConfigurationPath,
        options.UsageLog.DatabasePath,
        options.Registration.DatabasePath,
        options.Registration.DataProtectionKeysPath,
        options.Registration.OperatorRegistryPath,
        options.Registration.SecretConfigurationPath,
        options.Registration.MailConfigurationPath,
        options.Registration.AvailabilityPolicyPath,
        options.OperatorAccess.RegistryPath,
        options.OperatorAccess.PresetRegistryPath,
        options.TombstonePath,
        options.PublicAccessRegistryPath,
    ];

    static StorageCapacityVolume ReadVolume(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var drive = FindDrive(fullPath);
        if (!drive.IsReady) throw new IOException("storage volume is not ready");
        return new(drive.RootDirectory.FullName, drive.TotalSize, drive.AvailableFreeSpace);
    }

    static DriveInfo FindDrive(string fullPath)
    {
        var candidates = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady)
            .Where(drive => IsWithin(fullPath, drive.RootDirectory.FullName))
            .OrderByDescending(drive => drive.RootDirectory.FullName.Length)
            .ToArray();
        if (candidates.Length > 0) return candidates[0];

        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root)) throw new IOException("storage root is unavailable");
        return new DriveInfo(root);
    }

    static bool IsWithin(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedRoot = root.EndsWith(Path.DirectorySeparatorChar) || root.EndsWith(Path.AltDirectorySeparatorChar)
            ? root : root + Path.DirectorySeparatorChar;
        return path.Equals(root, comparison) || path.StartsWith(normalizedRoot, comparison);
    }

    static StringComparer PathComparer() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    static StorageCapacityReport Unavailable() => new(
        StorageCapacityStatus.Unavailable,
        Array.Empty<StorageCapacityVolume>(),
        "storage capacity could not be read");
}
