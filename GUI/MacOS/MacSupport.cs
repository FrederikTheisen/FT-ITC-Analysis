using System;
using System.Reflection;
using Foundation;

namespace AnalysisITC.GUI.MacOS
{
    public static class MacSupport
	{
        static string constant = "102114101100101114105107116104101105115101110064103109097105108046099111109";

        public static Version Version { get; } = Assembly.GetExecutingAssembly().GetName().Version;
        static DateTime BuildDate { get; } = new DateTime(2000, 1, 1).AddDays(Version.Build).AddSeconds(Version.Revision * 2);

        internal static int Revision => Version.Revision;
        internal static int Build => Version.Build;
        internal static int Minor => Version.Minor;
        internal static int Major => Version.Major;

        internal static string VersionString => $"{Version} ({BuildDate.ToString("dd/MM/yyyy HH:mm:ss")})";

        internal static string OperatingSystem => NSProcessInfo.ProcessInfo.OperatingSystemVersionString;

        public static readonly DateTime AppStartTime = DateTime.Now;

        public static void Test()
		{
            var sub = "FT-ITC Analysis Support";
            var msg = "Problem Report" + Environment.NewLine;
            msg += "Version: " + VersionString + Environment.NewLine;
            msg += "OS: MacOS " + OperatingSystem + Environment.NewLine + Environment.NewLine;
            msg += "Please describe the issue:" + Environment.NewLine + Environment.NewLine + Environment.NewLine;
            msg += "How to reproduce the issue:" + Environment.NewLine + Environment.NewLine + Environment.NewLine;
            msg += "If relevant, please include the file which causes the issue." + Environment.NewLine + Environment.NewLine;
            msg += "Thank you";

            S(sub, msg);
        }

        internal static void S(string subject, string body)
        {
            string r = "";

            var _con = constant;
            while (_con.Length > 0)
            {
                var c = int.Parse(_con.Substring(0, 3));
                _con = _con.Substring(3);
                r += (char)c;
            }

            var sub = Uri.EscapeDataString(subject);
            var msg = Uri.EscapeDataString(body);

            var str = "mailto:" + System.Uri.EscapeUriString(r) + "?subject=" + sub + "&body=" + msg;
            var url = NSUrl.FromString(str);
            AppKit.NSWorkspace.SharedWorkspace.OpenUrl(url);
        }
    }
}

