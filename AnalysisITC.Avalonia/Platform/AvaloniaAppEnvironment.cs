using System;
using System.Globalization;
using System.IO;

using AnalysisITC.Platform;

namespace AnalysisITC.Platform.Avalonia
{
    public sealed class AvaloniaAppEnvironment : IAppEnvironment
    {
        public string LocaleIdentifier => CultureInfo.CurrentCulture.Name;
        public string ShortVersion => typeof(AvaloniaAppEnvironment).Assembly.GetName().Version?.ToString() ?? "1.0";
        public string BuildVersion => ShortVersion;
        public string ApplicationDataDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AnalysisITC",
            "Avalonia");

        public string GetResourcePath(string name, string extension)
        {
            var filename = string.IsNullOrWhiteSpace(extension)
                ? name
                : name + "." + extension.TrimStart('.');

            var baseCandidate = Path.Combine(AppContext.BaseDirectory, filename);
            if (File.Exists(baseCandidate)) return baseCandidate;

            var currentCandidate = Path.Combine(Environment.CurrentDirectory, filename);
            return File.Exists(currentCandidate) ? currentCandidate : null!;
        }
    }
}
