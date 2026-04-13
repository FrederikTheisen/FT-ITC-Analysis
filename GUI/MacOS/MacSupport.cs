using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AppKit;
using CoreGraphics;
using Foundation;

namespace AnalysisITC.GUI.MacOS
{
    public static class MacSupport
	{
        static readonly string constant = "102114101100101114105107116104101105115101110064103109097105108046099111109";
        static readonly Dictionary<IntPtr, SupportShareSession> ActiveShareSessions = new Dictionary<IntPtr, SupportShareSession>();
        const string SupportReportPrefix = "ft-itc-support-report-";

        internal static string VersionString => AppVersion.FullVersionString;

        internal static string OperatingSystem => NSProcessInfo.ProcessInfo.OperatingSystemVersionString;
        internal static string SupportAddress => GetSupportAddress();

        public static readonly DateTime AppStartTime = DateTime.Now;

        public static void StartSupportEmail()
		{
            CleanupStaleSupportFiles();

            var subject = "FT-ITC Analysis Support";
            var body = BuildSupportEmailBody();
            var reportPath = CreateSupportReportFile();
            var reportUrl = NSUrl.CreateFileUrl(reportPath, false, null);
            var items = new NSObject[] { new NSString(body), reportUrl };
            var service = NSSharingService.GetSharingService(NSSharingServiceName.ComposeEmail);

            if (service != null && service.CanPerformWithItems(items))
            {
                service.Recipients = new NSObject[] { new NSString(SupportAddress) };
                service.Subject = subject;
                ConfigureSharingPresentation(service);
                RegisterCleanup(service, reportPath);
                service.PerformWithItems(items);
                return;
            }

            TryDeleteFile(reportPath);
            CopyToClipboard(BuildSupportReport());
            ShowComposeUnavailableAlert();
        }

        public static void CopySupportReportToClipboard()
        {
            CopyToClipboard(BuildSupportReport());
            ShowCopiedAlert();
        }

        static string BuildSupportEmailBody()
        {
            var builder = new StringBuilder();

            builder.AppendLine("Problem Report");
            builder.Append("Version: ");
            builder.AppendLine(VersionString);
            builder.Append("OS: MacOS ");
            builder.AppendLine(OperatingSystem);
            builder.Append("App Started: ");
            builder.AppendLine(AppStartTime.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine();
            builder.AppendLine("Please describe the issue:");
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("How to reproduce the issue:");
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("If relevant, please include the file which causes the issue.");
            builder.AppendLine();
            builder.AppendLine("Recent Activity");
            builder.AppendLine("---------------");
            builder.Append(AppEventHandler.GetRecentLogSummary());
            builder.AppendLine();
            builder.AppendLine("Tip: Use Help > Copy Support Report to copy the full application log.");

            return builder.ToString();
        }

        static string BuildSupportReport()
        {
            var builder = new StringBuilder();

            builder.Append(BuildSupportEmailBody());
            builder.AppendLine();
            builder.AppendLine("Full Application Log");
            builder.AppendLine("--------------------");
            builder.Append(AppEventHandler.GetLogReport());

            return builder.ToString();
        }

        static void CopyToClipboard(string report)
        {
            var pasteboard = NSPasteboard.GeneralPasteboard;
            pasteboard.ClearContents();
            pasteboard.SetStringForType(report, NSPasteboard.NSStringType);
        }

        static string CreateSupportReportFile()
        {
            var fileName = $"{SupportReportPrefix}{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.txt";
            var reportPath = Path.Combine(Path.GetTempPath(), fileName);

            File.WriteAllText(reportPath, BuildSupportReport(), Encoding.UTF8);

            return reportPath;
        }

        static void RegisterCleanup(NSSharingService service, string reportPath)
        {
            var session = new SupportShareSession(service, reportPath);
            session.SharedHandler = (_, __) => DetachShareSession(service.Handle, deleteFile: false);
            session.FailedHandler = (_, __) => DetachShareSession(service.Handle, deleteFile: true);

            service.DidShareItems += session.SharedHandler;
            service.DidFailToShareItems += session.FailedHandler;

            ActiveShareSessions[service.Handle] = session;
        }

        static void ConfigureSharingPresentation(NSSharingService service)
        {
            service.SourceWindowForShareItems = (_, __, ___) => NSApplication.SharedApplication.MainWindow;
            service.SourceFrameOnScreenForShareItem = (_, __) => CGRect.Empty;
            service.TransitionImageForShareItem = (_, __, ___) => null;
        }

        static void DetachShareSession(IntPtr handle, bool deleteFile)
        {
            if (!ActiveShareSessions.TryGetValue(handle, out var session))
            {
                return;
            }

            session.Service.DidShareItems -= session.SharedHandler;
            session.Service.DidFailToShareItems -= session.FailedHandler;
            if (deleteFile)
            {
                TryDeleteFile(session.ReportPath);
            }
            ActiveShareSessions.Remove(handle);
        }

        static void CleanupStaleSupportFiles()
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(Path.GetTempPath(), $"{SupportReportPrefix}*.txt"))
                {
                    var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(file);
                    if (age > TimeSpan.FromDays(3))
                    {
                        TryDeleteFile(file);
                    }
                }
            }
            catch
            {
            }
        }

        static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        static void ShowComposeUnavailableAlert()
        {
            var alert = new NSAlert
            {
                MessageText = "Email compose is unavailable",
                InformativeText = $"The support report has been copied to the clipboard. Please email it to {SupportAddress}.",
                AlertStyle = NSAlertStyle.Informational
            };

            alert.AddButton("OK");

            var window = NSApplication.SharedApplication.MainWindow;
            if (window != null)
            {
                alert.BeginSheet(window);
            }
            else
            {
                alert.RunModal();
            }
        }

        static void ShowCopiedAlert()
        {
            var alert = new NSAlert
            {
                MessageText = "Support report copied",
                InformativeText = "The full support report, including the application log, has been copied to the clipboard.",
                AlertStyle = NSAlertStyle.Informational
            };

            alert.AddButton("OK");

            var window = NSApplication.SharedApplication.MainWindow;
            if (window != null)
            {
                alert.BeginSheet(window);
            }
            else
            {
                alert.RunModal();
            }
        }

        static string GetSupportAddress()
        {
            string recipient = "";
            var _con = constant;
            while (_con.Length > 0)
            {
                var c = int.Parse(_con.Substring(0, 3));
                _con = _con.Substring(3);
                recipient += (char)c;
            }

            return recipient;
        }

        sealed class SupportShareSession
        {
            public SupportShareSession(NSSharingService service, string reportPath)
            {
                Service = service;
                ReportPath = reportPath;
            }

            public NSSharingService Service { get; }
            public string ReportPath { get; }
            public EventHandler<NSSharingServiceItemsEventArgs> SharedHandler { get; set; }
            public EventHandler<NSSharingServiceDidFailToShareItemsEventArgs> FailedHandler { get; set; }
        }
    }
}
