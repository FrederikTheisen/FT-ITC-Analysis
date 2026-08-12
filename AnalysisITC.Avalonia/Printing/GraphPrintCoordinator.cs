using System;
using System.Threading.Tasks;

using Avalonia.Controls;

using AnalysisITC.Core.Application;

namespace AnalysisITC.Avalonia.Printing;

internal enum PrintOutcome
{
    Printed,
    Saved,
    Canceled
}

internal interface IGraphPrintBackend
{
    Task<PrintOutcome> PrintAsync(Window owner, GraphPrintPayload payload);
}

internal static class GraphPrintCoordinator
{
    internal static Func<IGraphPrintBackend>? BackendFactoryOverride { get; set; }

    public static async Task PrintAsync(Window owner, GraphPrintTarget target)
    {
        try
        {
            StatusBarManager.SetStatus("Preparing graph for printing...", 0);
            using var payload = await target.CaptureAsync();
            var backend = BackendFactoryOverride?.Invoke() ?? CreateBackend();
            var outcome = await PrintPreparedAsync(owner, payload, backend);
            StatusBarManager.SetStatus(outcome switch
            {
                PrintOutcome.Printed => "Print job submitted",
                PrintOutcome.Saved => "PDF saved",
                _ => "Printing canceled"
            }, 3000);
        }
        catch (Exception ex)
        {
            AppEventHandler.DisplayHandledException(ex);
            StatusBarManager.SetStatus($"Printing failed: {ex.Message}", 5000);
        }
    }

    internal static Task<PrintOutcome> PrintPreparedAsync(
        Window owner,
        GraphPrintPayload payload,
        IGraphPrintBackend backend)
        => backend.PrintAsync(owner, payload);

    static IGraphPrintBackend CreateBackend()
    {
        if (OperatingSystem.IsMacOS()) return new MacGraphPrintBackend();
        if (OperatingSystem.IsWindows()) return new WindowsGraphPrintBackend();
        if (OperatingSystem.IsLinux()) return new CupsGraphPrintBackend();
        throw new PlatformNotSupportedException("Printing is not supported on this platform.");
    }
}
