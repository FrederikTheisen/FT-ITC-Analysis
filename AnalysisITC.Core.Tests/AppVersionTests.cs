using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AnalysisITC.Core.Application;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class AppVersionTestCollectionDefinition
    {
        public const string Name = "App version update checks";
    }

    [Collection(AppVersionTestCollectionDefinition.Name)]
    public sealed class AppVersionTests : IDisposable
    {
        readonly IAppEnvironment originalEnvironment;
        readonly ITextDownloadService originalDownloader;
        readonly IAppNotificationService originalNotificationService;
        readonly bool originalOnlineCheckPreference;

        public AppVersionTests()
        {
            originalEnvironment = PlatformServices.AppEnvironment;
            originalDownloader = PlatformServices.TextDownloadService;
            originalNotificationService = PlatformServices.AppNotificationService;
            originalOnlineCheckPreference = AppSettings.PerformOnlineChecksOnLaunch;
            AppSettings.PerformOnlineChecksOnLaunch = true;
        }

        public void Dispose()
        {
            PlatformServices.RegisterAppEnvironment(originalEnvironment);
            PlatformServices.RegisterTextDownloadService(originalDownloader);
            PlatformServices.RegisterAppNotificationService(originalNotificationService);
            AppSettings.PerformOnlineChecksOnLaunch = originalOnlineCheckPreference;
        }

        [Fact]
        public async Task NewerReleaseMapsMetadataAndShowsReleaseNotification()
        {
            const string body = "## What's Changed\n- Added [correlation plots](https://example.test/plots)\n> Fixed **exports** with `quoted code`.";
            const string releaseUrl = "https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/tag/v1.6.0";
            var downloader = new StubTextDownloadService(ReleaseJson("v1.6.0", "FT-ITC Analysis 1.6.0", body, releaseUrl));
            var notifications = new RecordingNotificationService();
            PlatformServices.RegisterAppEnvironment(new FixedAppEnvironment("1.5.0.0"));
            PlatformServices.RegisterTextDownloadService(downloader);
            PlatformServices.RegisterAppNotificationService(notifications);

            var result = await AppVersion.CheckForUpdatesAsync(forceOnlineCheck: true);

            Assert.NotNull(result);
            Assert.Equal(AppVersion.LatestReleaseApiUrl, downloader.RequestedUrl);
            Assert.Equal("1.5.0", result.CurrentVersion);
            Assert.Equal("1.6.0", result.LatestVersion);
            Assert.Equal("FT-ITC Analysis 1.6.0", result.LatestTitle);
            Assert.Equal(body, result.ReleaseNotes);
            Assert.Equal(releaseUrl, result.ReleaseUrl);
            Assert.True(result.IsUpdateAvailable);
            var entry = Assert.Single(result.NewerEntries);
            Assert.Equal("1.6.0", entry.Version);
            Assert.Contains("• Added correlation plots", entry.Notes);

            var alert = Assert.Single(notifications.Alerts);
            Assert.Equal("New Version Available!", alert.Title);
            Assert.Equal(releaseUrl, alert.ActionUrl);
            Assert.Contains("Installed version: 1.5.0", alert.Message);
            Assert.Contains("Newest version: 1.6.0", alert.Message);
            Assert.Contains("What changed:", alert.Message);
            Assert.Contains("• Added correlation plots", alert.Message);
            Assert.Contains("Fixed exports with quoted code.", alert.Message);
            Assert.DoesNotContain("https://example.test/plots", alert.Message);
            Assert.DoesNotContain("##", alert.Message);
        }

        [Theory]
        [InlineData("v1.5.1", true)]
        [InlineData("v1.5.0", false)]
        [InlineData("v1.4.9", false)]
        public void ComparesReleaseTagWithInstalledVersion(string tagName, bool expectedUpdate)
        {
            var result = AppVersion.BuildCheckResult(ReleaseJson(tagName), "1.5.0");

            Assert.NotNull(result);
            Assert.Equal(expectedUpdate, result.IsUpdateAvailable);
            Assert.Equal(expectedUpdate ? 1 : 0, result.NewerEntries.Count);
        }

        [Theory]
        [InlineData("v1.5.0")]
        [InlineData("v1.4.9")]
        public async Task CurrentOrOlderReleaseDoesNotShowAutomaticNotification(string tagName)
        {
            PlatformServices.RegisterAppEnvironment(new FixedAppEnvironment("1.5.0.0"));
            PlatformServices.RegisterTextDownloadService(new StubTextDownloadService(ReleaseJson(tagName)));
            var notifications = new RecordingNotificationService();
            PlatformServices.RegisterAppNotificationService(notifications);

            var result = await AppVersion.CheckForUpdatesAsync(forceOnlineCheck: true);

            Assert.NotNull(result);
            Assert.False(result.IsUpdateAvailable);
            Assert.Empty(notifications.Alerts);
        }

        [Theory]
        [InlineData("not-a-version", false, false)]
        [InlineData("v1.6.0", true, false)]
        [InlineData("v1.6.0", false, true)]
        public void RejectsInvalidOrNonStableRelease(string tagName, bool draft, bool prerelease)
        {
            var result = AppVersion.BuildCheckResult(
                ReleaseJson(tagName, draft: draft, prerelease: prerelease),
                "1.5.0");

            Assert.Null(result);
        }

        [Fact]
        public void RejectsMalformedJson()
        {
            Assert.Null(AppVersion.BuildCheckResult("{not json", "1.5.0"));
        }

        [Fact]
        public void EmptyReleaseBodyUsesFallbackMessage()
        {
            var result = AppVersion.BuildCheckResult(ReleaseJson("v1.6.0", body: null), "1.5.0");

            var message = AppVersion.BuildUpdateMessage(result);

            Assert.Contains("No release summary was provided. View the release page for details.", message);
            Assert.DoesNotContain("Release notes shortened", message);
        }

        [Fact]
        public void MarkdownPreviewIsPlainTextAndOmitsRedundantChangesHeading()
        {
            const string markdown = "## What's Changed\n* Added *analysis* for [new files](https://example.test)\n> Fixed ~~obsolete~~ `formatting`\n---\n![Screenshot](https://example.test/image.png)";

            var preview = AppVersion.BuildReleaseNotesPreview(markdown, out var wasTruncated);

            Assert.False(wasTruncated);
            Assert.Equal("• Added analysis for new files\nFixed obsolete formatting\nScreenshot", preview);
        }

        [Fact]
        public void ReleasePreviewIsLimitedToSixNonEmptyLines()
        {
            var markdown = string.Join("\n\n", Enumerable.Range(1, 8).Select(index => $"- Change {index}"));

            var preview = AppVersion.BuildReleaseNotesPreview(markdown, out var wasTruncated);

            Assert.True(wasTruncated);
            Assert.Equal(6, preview.Split('\n').Length);
            Assert.True(preview.Length <= 600);
            Assert.DoesNotContain("Change 7", preview);
        }

        [Fact]
        public void ReleasePreviewIsLimitedToSixHundredCharactersAtWordBoundary()
        {
            var markdown = string.Join(" ", Enumerable.Repeat("completeword", 100));

            var preview = AppVersion.BuildReleaseNotesPreview(markdown, out var wasTruncated);

            Assert.True(wasTruncated);
            Assert.True(preview.Length <= 600);
            Assert.EndsWith("completeword", preview);

            var result = AppVersion.BuildCheckResult(ReleaseJson("v1.6.0", body: markdown), "1.5.0");
            Assert.Contains("Release notes shortened. View the release for complete details.", AppVersion.BuildUpdateMessage(result));
        }

        [Theory]
        [InlineData("http://github.com/FrederikTheisen/FT-ITC-Analysis/releases/tag/v1.6.0")]
        [InlineData("https://example.com/FrederikTheisen/FT-ITC-Analysis/releases/tag/v1.6.0")]
        [InlineData("https://github.com/other/project/releases/tag/v1.6.0")]
        [InlineData(null)]
        public void UnsafeOrMissingReleaseUrlUsesLatestReleasePage(string candidate)
        {
            Assert.Equal(
                "https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/latest",
                AppVersion.ValidateReleaseUrl(candidate));
        }

        [Fact]
        public async Task NetworkFailureDuringBackgroundCheckIsSilent()
        {
            var downloader = new StubTextDownloadService(new InvalidOperationException("offline"));
            var notifications = new RecordingNotificationService();
            PlatformServices.RegisterTextDownloadService(downloader);
            PlatformServices.RegisterAppNotificationService(notifications);

            var result = await AppVersion.CheckForUpdatesAsync(forceOnlineCheck: true);

            Assert.Null(result);
            Assert.Empty(notifications.Alerts);
        }

        [Fact]
        public async Task NetworkFailureDuringExplicitCheckShowsGenericMessage()
        {
            PlatformServices.RegisterTextDownloadService(new StubTextDownloadService(new InvalidOperationException("offline")));
            var notifications = new RecordingNotificationService();
            PlatformServices.RegisterAppNotificationService(notifications);

            var result = await AppVersion.CheckForUpdatesAsync(showUpToDateMessage: true);

            Assert.Null(result);
            var alert = Assert.Single(notifications.Alerts);
            Assert.Equal("Update Check", alert.Title);
            Assert.Equal("Unable to retrieve update information right now.", alert.Message);
        }

        [Fact]
        public async Task DisabledLaunchPreferenceSkipsDownload()
        {
            var downloader = new StubTextDownloadService(ReleaseJson("v1.6.0"));
            PlatformServices.RegisterTextDownloadService(downloader);
            AppSettings.PerformOnlineChecksOnLaunch = false;

            var result = await AppVersion.CheckForUpdatesAsync();

            Assert.Null(result);
            Assert.Equal(0, downloader.CallCount);
        }

        static string ReleaseJson(
            string tagName,
            string name = "v1.6.0",
            string body = "A release summary.",
            string htmlUrl = "https://github.com/FrederikTheisen/FT-ITC-Analysis/releases/tag/v1.6.0",
            bool draft = false,
            bool prerelease = false)
        {
            return JsonSerializer.Serialize(new
            {
                tag_name = tagName,
                name,
                body,
                html_url = htmlUrl,
                draft,
                prerelease
            });
        }

        sealed class StubTextDownloadService : ITextDownloadService
        {
            readonly string response;
            readonly Exception exception;

            public StubTextDownloadService(string response)
            {
                this.response = response;
            }

            public StubTextDownloadService(Exception exception)
            {
                this.exception = exception;
            }

            public int CallCount { get; private set; }
            public string RequestedUrl { get; private set; }

            public Task<string> DownloadStringAsync(string url)
            {
                CallCount++;
                RequestedUrl = url;

                if (exception != null)
                    return Task.FromException<string>(exception);

                return Task.FromResult(response);
            }
        }

        sealed class RecordingNotificationService : IAppNotificationService
        {
            public List<RecordedAlert> Alerts { get; } = new List<RecordedAlert>();

            public void ShowInfoAlert(string title, string message, bool useLeftAlignedAccessory = false, string actionUrl = null)
            {
                Alerts.Add(new RecordedAlert(title, message, useLeftAlignedAccessory, actionUrl));
            }
        }

        sealed class RecordedAlert
        {
            public RecordedAlert(string title, string message, bool usesLeftAlignedAccessory, string actionUrl)
            {
                Title = title;
                Message = message;
                UsesLeftAlignedAccessory = usesLeftAlignedAccessory;
                ActionUrl = actionUrl;
            }

            public string Title { get; }
            public string Message { get; }
            public bool UsesLeftAlignedAccessory { get; }
            public string ActionUrl { get; }
        }

        sealed class FixedAppEnvironment : IAppEnvironment
        {
            public FixedAppEnvironment(string version)
            {
                ShortVersion = version;
            }

            public string LocaleIdentifier => "en-US";
            public string ShortVersion { get; }
            public string BuildVersion => ShortVersion;
            public string ApplicationDataDirectory => Path.GetTempPath();
            public string AutoSaveDirectory => Path.GetTempPath();
            public string GetResourcePath(string name, string extension) => null;
        }
    }
}
