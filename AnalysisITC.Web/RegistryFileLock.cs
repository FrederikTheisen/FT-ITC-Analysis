using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AnalysisITC.Web;

/// <summary>Persistent sibling locks shared by service and admin processes.</summary>
internal sealed class RegistryFileLock : IDisposable
{
    readonly List<FileStream> streams = [];

    public static RegistryFileLock Acquire(params string[] paths)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Registry locking requires a Unix host.");
        var result = new RegistryFileLock();
        var timer = Stopwatch.StartNew();
        try
        {
            foreach (var path in paths.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                result.streams.Add(AcquireOne(path + ".lock", timer));
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    static FileStream AcquireOne(string lockPath, Stopwatch timer)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        var createExclusive = OperatingSystem.IsMacOS() ? 0xA02 : 0xC2; // O_CREAT | O_EXCL | O_RDWR
        var closeOnExec = OperatingSystem.IsMacOS() ? 0x01000000 : 0x80000; // O_CLOEXEC
        var fd = open(lockPath, createExclusive | closeOnExec, Convert.ToUInt32("660", 8));
        var created = fd >= 0;
        if (fd < 0 && Marshal.GetLastPInvokeError() == 17) fd = open(lockPath, 0x2 | closeOnExec, 0); // EEXIST; O_RDWR
        if (fd < 0) throw new IOException("Could not open the registry lock.");
        var stream = new FileStream(new SafeFileHandle(new IntPtr(fd), ownsHandle: true), FileAccess.ReadWrite);
        try
        {
            if (created && fchmod(fd, Convert.ToUInt32("660", 8)) != 0)
                throw new IOException("Could not set registry lock permissions.");
            while (true)
            {
                if (flock(fd, 2 | 4) == 0) return stream; // LOCK_EX | LOCK_NB
                var error = Marshal.GetLastPInvokeError();
                if (error is not 11 and not 35) throw new IOException("Could not lock the registry.");
                if (timer.ElapsedMilliseconds >= 5000) throw new RegistryLockTimeoutException();
                Thread.Sleep(25);
            }
        }
        catch { stream.Dispose(); throw; }
    }

    public void Dispose()
    {
        for (var i = streams.Count - 1; i >= 0; i--) streams[i].Dispose();
        streams.Clear();
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "flock")]
    static extern int flock(int fd, int operation);
    [DllImport("libc", SetLastError = true, EntryPoint = "open", CharSet = CharSet.Ansi)]
    static extern int open(string path, int flags, uint mode);
    [DllImport("libc", SetLastError = true, EntryPoint = "fchmod")]
    static extern int fchmod(int fd, uint mode);
}

internal sealed class RegistryLockTimeoutException : IOException
{
    public RegistryLockTimeoutException() : base("The registry is busy; try again shortly.") { }
}
