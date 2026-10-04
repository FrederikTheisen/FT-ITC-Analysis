using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Application;

namespace TandemMixingReport
{
    /// <summary>
    /// Writes a PDF report of the model-free tandem mixing criterion on synthetic tandems with known
    /// mixing fractions and on real tandem projects. See README.md.
    /// </summary>
    static class Program
    {
        const string Usage =
            "Usage: dotnet run --project tools/TandemMixingReport -- [--out report.pdf] [project.ftxtc | folder ...]\n" +
            "  The default output is output/tandem-mixing-report.pdf in the repository (git-ignored).\n" +
            "  Synthetic cases and the repository fixture are always included. A folder adds its .ftxtc files.";

        static async Task<int> Main(string[] args)
        {
            string output = null;
            var inputs = new List<string>();
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--out" when index + 1 < args.Length:
                        output = args[++index];
                        break;
                    case "-h":
                    case "--help":
                        Console.WriteLine(Usage);
                        return 0;
                    default:
                        if (args[index].StartsWith("--"))
                        {
                            Console.Error.WriteLine($"Unknown option {args[index]}\n{Usage}");
                            return 2;
                        }
                        inputs.Add(args[index]);
                        break;
                }
            }

            var repositoryRoot = FindRepositoryRoot();
            if (repositoryRoot == null)
            {
                Console.Error.WriteLine("Could not find AnalysisITC.sln above the tool; run it from the repository.");
                return 2;
            }

            var projects = new List<string> { Path.Combine(repositoryRoot, ReportData.FixtureRelativePath) };
            foreach (var input in inputs)
            {
                if (Directory.Exists(input))
                    projects.AddRange(Directory.GetFiles(input, "*.ftxtc").OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
                else if (File.Exists(input))
                    projects.Add(input);
                else
                {
                    Console.Error.WriteLine($"Not found: {input}");
                    return 2;
                }
            }
            projects = projects.Select(Path.GetFullPath).Distinct().ToList();

            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            PreferencesState.Defaults().ApplyToSettings();

            // Core logs every scan step to standard output; keep the console for this tool's progress.
            var console = Console.Out;
            Console.SetOut(TextWriter.Null);
            var cases = new List<ReportCase>();
            try
            {
                foreach (var report in ReportData.SyntheticCases())
                {
                    cases.Add(report);
                    console.WriteLine($"synthetic  {report.Title}: {Describe(report)}");
                }

                foreach (var project in projects)
                {
                    var report = await ReportData.RealCase(project);
                    cases.Add(report);
                    console.WriteLine($"real       {report.Title}: {Describe(report)}");
                }
            }
            finally
            {
                Console.SetOut(console);
            }

            var header = new ReportHeader
            {
                Commit = GitDescription(repositoryRoot),
                Generated = DateTime.Now,
                DilutionMethod = AppSettings.DilutionCalculationMethod.ToString(),
            };
            var outputPath = Path.GetFullPath(output ?? Path.Combine(repositoryRoot, "output", "tandem-mixing-report.pdf"));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            PdfReport.Write(outputPath, header, cases);
            Console.WriteLine($"Wrote {outputPath}");
            return 0;
        }

        static string Describe(ReportCase report)
        {
            if (report.Failure != null) return $"skipped ({report.Failure})";
            var chosen = string.Join(" / ", report.ChosenFractions.Select(f => $"{100 * f:0.0}%"));
            return report.TrueFractions == null
                ? $"chosen {chosen}"
                : $"chosen {chosen}, true {string.Join(" / ", report.TrueFractions.Select(f => $"{100 * f:0.0}%"))}";
        }

        static string FindRepositoryRoot()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                    if (File.Exists(Path.Combine(directory.FullName, "AnalysisITC.sln"))) return directory.FullName;
            }

            return null;
        }

        static string GitDescription(string repositoryRoot)
        {
            var commit = Git(repositoryRoot, "rev-parse --short HEAD");
            if (string.IsNullOrWhiteSpace(commit)) return "unknown";
            var dirty = !string.IsNullOrWhiteSpace(Git(repositoryRoot, "status --porcelain"));
            return dirty ? $"{commit.Trim()} (uncommitted changes)" : commit.Trim();
        }

        static string Git(string directory, string arguments)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("git", $"-C \"{directory}\" {arguments}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });
                var text = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0 ? text : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
