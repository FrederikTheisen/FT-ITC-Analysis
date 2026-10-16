using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Export
{
    public sealed class FigureExportTarget
    {
        public ExperimentData Experiment { get; }
        public string Path { get; }

        public FigureExportTarget(ExperimentData experiment, string path)
        {
            Experiment = experiment ?? throw new ArgumentNullException(nameof(experiment));
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }
    }

    public static class FigureExportPlanner
    {
        public static IReadOnlyList<FigureExportTarget> CreateTargets(IEnumerable<ExperimentData> experiments, string folderPath)
        {
            if (experiments == null) throw new ArgumentNullException(nameof(experiments));
            if (string.IsNullOrWhiteSpace(folderPath)) throw new ArgumentException("A destination folder is required.", nameof(folderPath));
            var items = experiments.ToList();
            var stems = items.Select(item => SanitizeFileName(item?.Name)).ToList();
            var naturalNames = new HashSet<string>(stems.Select(stem => stem + ".pdf"), StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new string[items.Count];

            // Claim every first natural name before making generated suffixes, so a
            // duplicate cannot steal another experiment's natural '(2)' filename.
            for (var i = 0; i < items.Count; i++)
            {
                var natural = stems[i] + ".pdf";
                if (used.Add(natural)) names[i] = natural;
            }

            for (var i = 0; i < items.Count; i++)
            {
                if (names[i] != null) continue;
                for (var suffix = 2; ; suffix++)
                {
                    var candidate = stems[i] + " (" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture) + ").pdf";
                    if (naturalNames.Contains(candidate) || !used.Add(candidate)) continue;
                    names[i] = candidate;
                    break;
                }
            }

            return items.Select((item, index) => new FigureExportTarget(item, System.IO.Path.Combine(folderPath, names[index]))).ToList();
        }

        public static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string((value ?? string.Empty).Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(sanitized) || sanitized == "." || sanitized == ".."
                ? "Untitled Figure"
                : sanitized;
        }

        public static string GetProjectFolderName(string projectPath, DateTime localTime)
        {
            var name = string.IsNullOrWhiteSpace(projectPath)
                ? null
                : Path.GetFileNameWithoutExtension(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(name)
                ? localTime.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)
                : SanitizeFileName(name);
        }
    }
}
