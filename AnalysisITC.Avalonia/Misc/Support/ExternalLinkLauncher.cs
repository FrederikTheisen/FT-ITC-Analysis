using System;
using System.Diagnostics;

using AnalysisITC.Core.Application;

namespace AnalysisITC.Avalonia.Support;

internal static class ExternalLinkLauncher
{
    public static bool TryOpen(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            AppEventHandler.AddLog(ex);
            return false;
        }
    }
}
