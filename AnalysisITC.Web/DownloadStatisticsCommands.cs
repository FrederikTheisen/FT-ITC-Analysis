namespace AnalysisITC.Web;

internal static class DownloadStatisticsCommands
{
    public static async Task<int> RunAsync(string[] args, DownloadStatisticsOptions options, TextWriter output, TextWriter error,
        HttpClient? http = null, Func<DateTimeOffset>? now = null)
    {
        if (args.Length is not (1 or 3) || args[0] is not ("collect" or "export") ||
            args.Length == 3 && (args[1] != "--database" || string.IsNullOrWhiteSpace(args[2])))
        { error.WriteLine("Usage: download-counts collect|export [--database PATH]"); return 2; }
        var configured = new DownloadStatisticsOptions { Enabled = options.Enabled, DatabasePath = args.Length == 3 ? args[2] : options.DatabasePath };
        var service = new DownloadStatisticsService(configured, http);
        try
        {
            if (args[0] == "export") { service.ExportCsv(output); return 0; }
            if (!service.Enabled) { output.WriteLine("Download statistics are disabled."); return 0; }
            var snapshot = (await service.CollectAsync((now ?? (() => DateTimeOffset.UtcNow))()))!;
            output.WriteLine($"Download snapshot: {snapshot.ObservedUtc:O}; {snapshot.Assets.Count} installers.");
            return 0;
        }
        catch
        {
            error.WriteLine("Download statistics unavailable; check GitHub connectivity and the statistics database.");
            return 1;
        }
    }
}
