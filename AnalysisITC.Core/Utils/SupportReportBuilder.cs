using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AnalysisITC.Core.Application
{
    public static class SupportReportBuilder
    {
        public static DateTime AppStartTime { get; } = DateTime.Now;

        public static string SupportAddress => "support@ft-itc.org";

        public static string BuildEmailBody()
        {
            var builder = new StringBuilder();

            builder.AppendLine("Problem Report");
            builder.Append("Version: ");
            builder.AppendLine(AppVersion.FullVersionString);
            builder.Append("OS: ");
            builder.AppendLine(RuntimeInformation.OSDescription);
            builder.Append("Runtime: ");
            builder.AppendLine(RuntimeInformation.FrameworkDescription);
            builder.Append("Architecture: ");
            builder.AppendLine(RuntimeInformation.ProcessArchitecture.ToString());
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

            return builder.ToString();
        }

        public static string BuildFullReport()
        {
            var builder = new StringBuilder();

            builder.Append(BuildEmailBody());
            builder.AppendLine();
            builder.AppendLine("Full Application Log");
            builder.AppendLine("--------------------");
            builder.Append(AppEventHandler.GetLogReport());

            return builder.ToString();
        }
    }
}
