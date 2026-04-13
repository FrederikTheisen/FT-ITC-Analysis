using AnalysisITC;
using AppKit;
using Foundation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

public static class AppVersion
{
    const string VersionFileUrl = "https://raw.githubusercontent.com/FrederikTheisen/FT-ITC-Analysis/refs/heads/master/VERSION";

    static string VersionCachePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ftitc_version_cache.txt");

    static string VersionStatePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ftitc_version_state.txt");

    static bool AutomaticCheckStarted;

    static string ShortVersion =>
        NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString();

    static string BuildVersion =>
        NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleVersion")?.ToString();

    /// <summary>
    /// Returns the full app version x.y.z...
    /// </summary>
    public static string FullVersionString
    {
        get
        {
            var v = ShortVersion ?? BuildVersion ?? "?.?.?";
            return v;
        }
    }

    /// <summary>
    /// Return app version major.minor
    /// </summary>
    public static string ShortVersionString
    {
        get
        {
            var vs = FullVersionString.Split('.');
            return vs.Length >= 2 ? $"{vs[0]}.{vs[1]}" : FullVersionString;
        }
    }

    public static void CheckForUpdatesInBackground()
    {
        if (AutomaticCheckStarted)
            return;

        AutomaticCheckStarted = true;
        _ = CheckForUpdatesAsync(false, false);
    }

    public static async Task<AppVersionCheckResult> CheckForUpdatesAsync(bool showUpToDateMessage = false, bool forceOnlineCheck = false)
    {
        try
        {
            AppEventHandler.PrintAndLog("AppVersion: Checking for updates...");

            var versionFileText = await TryFetchVersionFile(forceOnlineCheck);
            if (string.IsNullOrWhiteSpace(versionFileText))
            {
                AppEventHandler.PrintAndLog("AppVersion: No version file available");

                if (showUpToDateMessage)
                    ShowInfoAlert("Update Check", "Unable to retrieve update information right now.");

                return null;
            }

            var result = BuildCheckResult(versionFileText);
            if (result == null)
            {
                AppEventHandler.PrintAndLog("AppVersion: Invalid version file");

                if (showUpToDateMessage)
                    ShowInfoAlert("Update Check", "The online version file could not be read.");

                return null;
            }

            if (result.IsUpdateAvailable)
            {
                var shouldNotify = showUpToDateMessage || ShouldNotifyForVersion(result.LatestVersion);
                AppEventHandler.PrintAndLog($"AppVersion: Update available ({FullVersionString} -> {result.LatestVersion})");

                if (shouldNotify)
                {
                    ShowInfoAlert($"Update available: v{result.LatestVersion}", BuildUpdateMessage(result));
                    SaveLastPromptedVersion(result.LatestVersion);
                }
            }
            else
            {
                AppEventHandler.PrintAndLog($"AppVersion: Application is up to date ({FullVersionString})");

                if (showUpToDateMessage)
                {
                    ShowInfoAlert(
                        "FT-ITC is up to date",
                        $"You are using version {FullVersionString}, which matches the newest version listed online.");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            AppEventHandler.PrintAndLog("AppVersion: Update check failed");
            AppEventHandler.AddLog(ex);

            if (showUpToDateMessage)
                ShowInfoAlert("Update Check", $"Unable to check for updates.\n{ex.Message}");

            return null;
        }
    }

    static async Task<string> TryFetchVersionFile(bool forceOnlineCheck)
    {
        var online = await TryDownloadVersionFile();
        if (!string.IsNullOrWhiteSpace(online))
            return online;

        if (forceOnlineCheck)
            return null;

        return TryLoadCachedVersionFile();
    }

    static async Task<string> TryDownloadVersionFile()
    {
        try
        {
            return await Task.Run(() =>
            {
                using var client = new WebClient();
                var text = client.DownloadString(VersionFileUrl);

                if (!string.IsNullOrWhiteSpace(text))
                    SaveVersionCache(text);

                return text;
            });
        }
        catch
        {
            return null;
        }
    }

    static string TryLoadCachedVersionFile()
    {
        try
        {
            if (!File.Exists(VersionCachePath))
                return null;

            return File.ReadAllText(VersionCachePath);
        }
        catch
        {
            return null;
        }
    }

    static void SaveVersionCache(string versionFileText)
    {
        try
        {
            File.WriteAllText(VersionCachePath, versionFileText);
        }
        catch
        {
        }
    }

    static AppVersionCheckResult BuildCheckResult(string versionFileText)
    {
        var entries = ParseVersionEntries(versionFileText);
        if (entries.Count == 0)
            return null;

        if (!TryParseVersion(FullVersionString, out var current))
            current = new Version(0, 0);

        var latestEntry = entries
            .Where(e => TryParseVersion(e.Version, out _))
            .OrderByDescending(e => ParseVersionOrDefault(e.Version))
            .FirstOrDefault();

        if (latestEntry == null || !TryParseVersion(latestEntry.Version, out var latest))
            return null;

        var newerEntries = entries
            .Where(e => TryParseVersion(e.Version, out var parsed) && parsed.CompareTo(current) > 0)
            .OrderByDescending(e => ParseVersionOrDefault(e.Version))
            .ToList();

        return new AppVersionCheckResult
        {
            CurrentVersion = FullVersionString,
            LatestVersion = latestEntry.Version,
            IsUpdateAvailable = latest.CompareTo(current) > 0,
            NewerEntries = newerEntries
        };
    }

    static List<AppVersionEntry> ParseVersionEntries(string versionFileText)
    {
        var entries = new List<AppVersionEntry>();
        AppVersionEntry currentEntry = null;

        foreach (var rawLine in versionFileText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = rawLine?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (TryParseVersionHeader(line, out var version))
            {
                currentEntry = new AppVersionEntry { Version = version };
                entries.Add(currentEntry);
                continue;
            }

            if (currentEntry != null)
                currentEntry.Notes.Add(line);
        }

        return entries;
    }

    static bool TryParseVersionHeader(string line, out string version)
    {
        version = null;

        const string compactPrefix = "#VERSION";
        const string spacedPrefix = "# VERSION";

        string remainder = null;

        if (line.StartsWith(compactPrefix, StringComparison.OrdinalIgnoreCase))
            remainder = line.Substring(compactPrefix.Length);
        else if (line.StartsWith(spacedPrefix, StringComparison.OrdinalIgnoreCase))
            remainder = line.Substring(spacedPrefix.Length);

        if (remainder == null)
            return false;

        var parsedVersion = remainder.Trim();
        if (!TryParseVersion(parsedVersion, out _))
            return false;

        version = parsedVersion;
        return true;
    }

    static bool ShouldNotifyForVersion(string latestVersion)
    {
        var lastPromptedVersion = TryLoadLastPromptedVersion();
        return string.IsNullOrWhiteSpace(lastPromptedVersion) ||
               !string.Equals(lastPromptedVersion, latestVersion, StringComparison.OrdinalIgnoreCase);
    }

    static string TryLoadLastPromptedVersion()
    {
        try
        {
            if (!File.Exists(VersionStatePath))
                return null;

            return File.ReadAllText(VersionStatePath).Trim();
        }
        catch
        {
            return null;
        }
    }

    static void SaveLastPromptedVersion(string version)
    {
        try
        {
            File.WriteAllText(VersionStatePath, version ?? string.Empty);
        }
        catch
        {
        }
    }

    static bool TryParseVersion(string versionText, out Version version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(versionText))
            return false;

        var normalized = versionText.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(1);

        return Version.TryParse(normalized, out version);
    }

    static Version ParseVersionOrDefault(string versionText)
    {
        return TryParseVersion(versionText, out var parsed) ? parsed : new Version(0, 0);
    }

    static void ShowInfoAlert(string title, string message)
    {
        NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            using var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Informational,
                MessageText = title,
                InformativeText = message
            };

            alert.AddButton("OK");
            alert.RunModal();
        });
    }

    static string BuildUpdateMessage(AppVersionCheckResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Installed version: v{result.CurrentVersion}");
        sb.AppendLine($"Newest version online: v{result.LatestVersion}");

        var entriesToShow = result.NewerEntries.Take(3).ToList();
        if (entriesToShow.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("New since your version:");

            foreach (var entry in entriesToShow)
            {
                sb.AppendLine($"v{entry.Version}");

                foreach (var note in entry.Notes.Take(5))
                    sb.AppendLine($"- {note}");

                sb.AppendLine();
            }
        }

        return sb.ToString().Trim();
    }
}

public class AppVersionEntry
{
    public string Version { get; set; }
    public List<string> Notes { get; } = new List<string>();
}

public class AppVersionCheckResult
{
    public string CurrentVersion { get; set; }
    public string LatestVersion { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public List<AppVersionEntry> NewerEntries { get; set; } = new List<AppVersionEntry>();
}
