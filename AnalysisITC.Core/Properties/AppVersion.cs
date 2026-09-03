using AnalysisITC.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AnalysisITC.Core.Application
{

public static class AppVersion
{
    internal const string LatestReleaseApiUrl = "https://api.github.com/repos/FrederikTheisen/FT-ITC-Analysis/releases/latest";
    const string LatestReleasePageUrl = "https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest";
    const string ReleasePathPrefix = "/FrederikTheisen/FT-ITC-Analysis/releases/";
    const int ReleaseNotesMaximumLines = 6;
    const int ReleaseNotesMaximumCharacters = 600;

    static readonly Regex MarkdownImageRegex = new Regex(@"!\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);
    static readonly Regex MarkdownLinkRegex = new Regex(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled);
    static readonly Regex MarkdownHeadingRegex = new Regex(@"^\s{0,3}#{1,6}\s*", RegexOptions.Compiled);
    static readonly Regex MarkdownBulletRegex = new Regex(@"^\s*[-*+]\s+", RegexOptions.Compiled);
    static readonly Regex MarkdownQuoteRegex = new Regex(@"^\s*>\s?", RegexOptions.Compiled);
    static readonly Regex MarkdownEmphasisRegex = new Regex(@"(?<![*_])([*_])([^*_]+)\1(?![*_])", RegexOptions.Compiled);
    static readonly Regex MarkdownStrikethroughRegex = new Regex(@"~~([^~]+)~~", RegexOptions.Compiled);
    static readonly Regex HtmlTagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);
    static readonly Regex HorizontalRuleRegex = new Regex(@"^\s*([-*_]\s*){3,}$", RegexOptions.Compiled);
    static readonly Regex InlineWhitespaceRegex = new Regex(@"[ \t]+", RegexOptions.Compiled);

    static bool AutomaticCheckStarted;

    static string ShortVersion =>
        PlatformServices.AppEnvironment.ShortVersion;

    static string BuildVersion =>
        PlatformServices.AppEnvironment.BuildVersion;

    /// <summary>
    /// Returns the full app version x.y.z...
    /// </summary>
    public static string FullVersionString
    {
        get
        {
            var v = ShortVersion ?? BuildVersion ?? "?.?.?";
            return FormatVersion(v, 3);
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

    static string FormatVersion(string version, int maxComponents)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "?.?.?";

        var components = version
            .Split('.')
            .Take(maxComponents)
            .ToArray();

        return components.Length == 0 ? version : string.Join(".", components);
    }

    public static void CheckForUpdatesInBackground()
    {
        if (AutomaticCheckStarted)
            return;

        if (!AppSettings.PerformOnlineChecksOnLaunch)
        {
            AppEventHandler.PrintAndLog("AppVersion: Online update check skipped by launch preference");
            return;
        }

        AutomaticCheckStarted = true;
        _ = CheckForUpdatesAsync(false, false);
    }

    public static async Task<AppVersionCheckResult> CheckForUpdatesAsync(bool showUpToDateMessage = false, bool forceOnlineCheck = false)
    {
        if (!forceOnlineCheck && !showUpToDateMessage && !AppSettings.PerformOnlineChecksOnLaunch)
        {
            AppEventHandler.PrintAndLog("AppVersion: Online update check skipped by launch preference");
            return null;
        }

        try
        {
            AppEventHandler.PrintAndLog("AppVersion: Checking GitHub for the latest release...");

            var releaseJson = await TryFetchLatestRelease();
            if (string.IsNullOrWhiteSpace(releaseJson))
            {
                AppEventHandler.PrintAndLog("AppVersion: No GitHub release information available");

                if (showUpToDateMessage)
                    ShowInfoAlert("Update Check", "Unable to retrieve update information right now.");

                return null;
            }

            var result = BuildCheckResult(releaseJson, FullVersionString);
            if (result == null)
            {
                AppEventHandler.PrintAndLog("AppVersion: Invalid GitHub release information");

                if (showUpToDateMessage)
                    ShowInfoAlert("Update Check", "The online release information could not be read.");

                return null;
            }

            if (result.IsUpdateAvailable)
            {
                AppEventHandler.PrintAndLog($"AppVersion: Update available ({FullVersionString} -> {result.LatestVersion})");

                ShowInfoAlert("New Version Available!", BuildUpdateMessage(result), true, result.ReleaseUrl);
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

    static async Task<string> TryFetchLatestRelease()
    {
        try
        {
            return await PlatformServices.TextDownloadService.DownloadStringAsync(LatestReleaseApiUrl);
        }
        catch (Exception ex)
        {
            AppEventHandler.AddLog(ex);
            return null;
        }
    }

    internal static AppVersionCheckResult BuildCheckResult(string releaseJson, string currentVersionText)
    {
        GitHubReleaseInfo release;

        try
        {
            release = JsonSerializer.Deserialize<GitHubReleaseInfo>(releaseJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (release == null || release.Draft || release.Prerelease || !TryParseVersion(release.TagName, out var latest))
            return null;

        if (!TryParseVersion(currentVersionText, out var current))
            current = new Version(0, 0);

        var latestVersionText = FormatVersion(NormalizeVersionText(release.TagName), 3);
        var title = string.IsNullOrWhiteSpace(release.Name) ? null : release.Name.Trim();
        var isUpdateAvailable = latest.CompareTo(current) > 0;
        var latestEntry = new AppVersionEntry
        {
            Version = latestVersionText,
            Title = title
        };

        foreach (var line in GetPlainTextReleaseNoteLines(release.Body))
            latestEntry.Notes.Add(line);

        return new AppVersionCheckResult
        {
            CurrentVersion = FormatVersion(currentVersionText, 3),
            LatestVersion = latestVersionText,
            LatestTitle = title,
            ReleaseNotes = release.Body,
            ReleaseUrl = ValidateReleaseUrl(release.HtmlUrl),
            IsUpdateAvailable = isUpdateAvailable,
            NewerEntries = isUpdateAvailable
                ? new List<AppVersionEntry> { latestEntry }
                : new List<AppVersionEntry>()
        };
    }

    static string NormalizeVersionText(string versionText)
    {
        var normalized = versionText?.Trim() ?? string.Empty;
        return normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(1)
            : normalized;
    }

    static bool TryParseVersion(string versionText, out Version version)
    {
        version = null;

        if (string.IsNullOrWhiteSpace(versionText))
            return false;

        return Version.TryParse(NormalizeVersionText(versionText), out version);
    }

    internal static string ValidateReleaseUrl(string candidate)
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(ReleasePathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return LatestReleasePageUrl;
        }

        return uri.AbsoluteUri;
    }

    static void ShowInfoAlert(string title, string message, bool useLeftAlignedAccessory = false, string actionUrl = null)
    {
        PlatformServices.AppNotificationService.ShowInfoAlert(title, message, useLeftAlignedAccessory, actionUrl);
    }

    internal static string BuildUpdateMessage(AppVersionCheckResult result)
    {
        var sb = new StringBuilder();
        if (HasMeaningfulReleaseTitle(result.LatestTitle, result.LatestVersion))
        {
            sb.AppendLine(result.LatestTitle.Trim());
            sb.AppendLine();
        }

        sb.AppendLine($"Installed version: {result.CurrentVersion}");
        sb.AppendLine($"Newest version: {result.LatestVersion}");
        sb.AppendLine();
        sb.AppendLine("What changed:");

        var preview = BuildReleaseNotesPreview(result.ReleaseNotes, out var wasTruncated);
        if (string.IsNullOrWhiteSpace(preview))
        {
            sb.AppendLine("No release summary was provided. View the release page for details.");
        }
        else
        {
            sb.AppendLine(preview);

            if (wasTruncated)
            {
                sb.AppendLine();
                sb.AppendLine("Release notes shortened. View the release for complete details.");
            }
        }

        return sb.ToString().Trim();
    }

    static bool HasMeaningfulReleaseTitle(string title, string version)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        var trimmed = title.Trim();
        return !string.Equals(trimmed, version, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(NormalizeVersionText(trimmed), version, StringComparison.OrdinalIgnoreCase);
    }

    internal static string BuildReleaseNotesPreview(string markdown, out bool wasTruncated)
    {
        var lines = GetPlainTextReleaseNoteLines(markdown);
        var selectedLines = new List<string>();
        var characterCount = 0;
        wasTruncated = false;

        foreach (var line in lines)
        {
            if (selectedLines.Count >= ReleaseNotesMaximumLines)
            {
                wasTruncated = true;
                break;
            }

            var separatorLength = selectedLines.Count == 0 ? 0 : 1;
            var remainingCharacters = ReleaseNotesMaximumCharacters - characterCount - separatorLength;
            if (remainingCharacters <= 0)
            {
                wasTruncated = true;
                break;
            }

            if (line.Length > remainingCharacters)
            {
                var shortened = TruncateAtWordBoundary(line, remainingCharacters);
                if (!string.IsNullOrWhiteSpace(shortened))
                {
                    selectedLines.Add(shortened);
                    characterCount += separatorLength + shortened.Length;
                }

                wasTruncated = true;
                break;
            }

            selectedLines.Add(line);
            characterCount += separatorLength + line.Length;
        }

        if (selectedLines.Count < lines.Count)
            wasTruncated = true;

        return string.Join("\n", selectedLines);
    }

    static List<string> GetPlainTextReleaseNoteLines(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return new List<string>();

        var lines = new List<string>();
        foreach (var rawLine in markdown.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None))
        {
            var line = rawLine?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(line) || HorizontalRuleRegex.IsMatch(line))
                continue;

            line = MarkdownImageRegex.Replace(line, "$1");
            line = MarkdownLinkRegex.Replace(line, "$1");
            line = MarkdownHeadingRegex.Replace(line, string.Empty);
            line = MarkdownBulletRegex.Replace(line, "• ");
            line = MarkdownQuoteRegex.Replace(line, string.Empty);
            line = HtmlTagRegex.Replace(line, string.Empty);
            line = line.Replace("**", string.Empty)
                .Replace("__", string.Empty)
                .Replace("`", string.Empty)
                .Trim();
            line = MarkdownEmphasisRegex.Replace(line, "$2");
            line = MarkdownStrikethroughRegex.Replace(line, "$1");
            line = InlineWhitespaceRegex.Replace(line, " ");

            if (lines.Count == 0 && IsGenericChangesHeading(line))
                continue;

            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }

        return lines;
    }

    static bool IsGenericChangesHeading(string line)
    {
        return string.Equals(line, "What's Changed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(line, "What Changed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(line, "Changes", StringComparison.OrdinalIgnoreCase);
    }

    static string TruncateAtWordBoundary(string text, int maximumLength)
    {
        if (string.IsNullOrEmpty(text) || maximumLength <= 0)
            return string.Empty;

        if (text.Length <= maximumLength)
            return text;

        var candidate = text.Substring(0, maximumLength).TrimEnd();
        var lastWhitespace = candidate.LastIndexOf(' ');
        if (lastWhitespace > 0)
            candidate = candidate.Substring(0, lastWhitespace).TrimEnd();

        return candidate;
    }
}

internal sealed class GitHubReleaseInfo
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("body")]
    public string Body { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }
}

public class AppVersionEntry
{
    public string Version { get; set; }
    public string Title { get; set; }
    public List<string> Notes { get; } = new List<string>();
}

public class AppVersionCheckResult
{
    public string CurrentVersion { get; set; }
    public string LatestVersion { get; set; }
    public string LatestTitle { get; set; }
    public string ReleaseNotes { get; set; }
    public string ReleaseUrl { get; set; }
    public bool IsUpdateAvailable { get; set; }
    public List<AppVersionEntry> NewerEntries { get; set; } = new List<AppVersionEntry>();
}

}
