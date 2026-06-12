using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Foundation;

namespace AnalysisITC
{
    public class CitationInfo
    {
        public const string SoftwareTitle = "FT-ITC Analysis";
        public const string SoftwareAuthors = "Frederik Friis Theisen";
        public const string SoftwareDoi = "10.5281/zenodo.14832177";
        public const string SoftwareDoiUrl = "https://doi.org/10.5281/zenodo.14832177";
        public const string SoftwareRepositoryUrl = "https://github.com/FrederikTheisen/FT-ITC-Analysis";

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("authors")]
        public string Authors { get; set; }

        [JsonPropertyName("journal")]
        public string Journal { get; set; }

        [JsonPropertyName("year")]
        public string Year { get; set; }

        [JsonPropertyName("doi")]
        public string DOI { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }

        public string ToMarkdownDisplayString(bool includeVersion = false, string label = null)
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(label))
                sb.AppendLine($"**{label}**");

            if (!string.IsNullOrWhiteSpace(Message))
                sb.AppendLine(Message);

            if (!string.IsNullOrWhiteSpace(Title))
                sb.AppendLine(Title);

            var line2 = $"{Authors}, *{Journal}*, {Year}".Trim(' ', ',');
            if (!string.IsNullOrWhiteSpace(line2))
                sb.AppendLine(line2);

            if (!string.IsNullOrWhiteSpace(DOI))
                sb.AppendLine("**DOI**: " + DOI);

            if (includeVersion && !string.IsNullOrWhiteSpace(Version))
                sb.AppendLine("**Version**: " + Version);

            return sb.ToString().Trim();
        }

        public string ToPaperBibTeX()
        {
            string year = string.IsNullOrWhiteSpace(Year) ? DateTime.Now.Year.ToString() : Year;

            return
$@"@article{{ftitc-paper-{SanitizeBibTeXKeyPart(year)},
  title = {{{EscapeBibTeXValue(Title)}}},
  author = {{{EscapeBibTeXValue(Authors)}}},
  journal = {{{EscapeBibTeXValue(Journal)}}},
  year = {{{EscapeBibTeXValue(year)}}}{FormatBibTeXOptionalField("doi", DOI)}
}}";
        }

        public string ToSoftwareBibTeX()
        {
            string year = DateTime.Now.Year.ToString();

            return
$@"@software{{ftitc-analysis-{SanitizeBibTeXKeyPart(Version)},
  title = {{{EscapeBibTeXValue(Title)}}},
  author = {{{EscapeBibTeXValue(Authors)}}},
  year = {{{year}}},
  version = {{{EscapeBibTeXValue(Version)}}},
  doi = {{{EscapeBibTeXValue(DOI)}}},
  url = {{{EscapeBibTeXValue(SoftwareDoiUrl)}}},
  repository = {{{EscapeBibTeXValue(SoftwareRepositoryUrl)}}}
}}";
        }

        static string FormatBibTeXOptionalField(string fieldName, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return $",\n  {fieldName} = {{{EscapeBibTeXValue(value)}}}";
        }

        static string EscapeBibTeXValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            var sb = new StringBuilder();
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\':
                        sb.Append("\\textbackslash{}");
                        break;
                    case '{':
                        sb.Append("\\{");
                        break;
                    case '}':
                        sb.Append("\\}");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }

        static string SanitizeBibTeXKeyPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "current";

            var sb = new StringBuilder();
            foreach (var c in value)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            }

            return sb.Length == 0 ? "current" : sb.ToString();
        }
    }

    public static class CitationManager
    {
        private static string CachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "citation_cache.json");

        public static CitationInfo DefaultCitation => new CitationInfo
        {
            Title = "FT-ITC Analysis",
            Authors = "Frederik Friis Theisen",
            Journal = "Not Yet Published",
            Year = "2026",
            DOI = "",
            //Message = "Please cite this paper when using FT-ITC Analysis."
        };

        public static CitationInfo SoftwareCitation => new CitationInfo
        {
            Title = CitationInfo.SoftwareTitle,
            Authors = CitationInfo.SoftwareAuthors,
            Journal = "Zenodo",
            Year = DateTime.Now.Year.ToString(),
            DOI = CitationInfo.SoftwareDoi,
            Version = AppVersion.FullVersionString,
            //Message = "Cite the software version when exact reproducibility matters."
        };

        public static CitationInfo GetPaperCitation()
        {
            var cached = TryLoadCache();
            if (cached != null)
                return cached;

            var bundled = TryLoadBundledCitation();
            if (bundled != null)
                return bundled;

            return DefaultCitation;
        }

        public static string BuildCombinedBibTeX()
        {
            return GetPaperCitation().ToPaperBibTeX() + Environment.NewLine + Environment.NewLine + SoftwareCitation.ToSoftwareBibTeX() + Environment.NewLine;
        }

        public static async Task TryFetchOnlineCitation(bool forceOnlineCheck = false)
        {
            if (!forceOnlineCheck && !AppSettings.PerformOnlineChecksOnLaunch)
            {
                AppEventHandler.PrintAndLog("Citation Manager: Online citation check skipped by launch preference");
                return;
            }

            try
            {
                await Task.Run(() =>
                {
                    AppEventHandler.PrintAndLog("Citation Manager: Trying to update citation info...");
                    string url = "https://raw.githubusercontent.com/FrederikTheisen/FT-ITC-Analysis/refs/heads/master/citation.json";
                    using var client = new WebClient();
                    string json = client.DownloadString(url);

                    SaveCache(JsonSerializer.Deserialize<CitationInfo>(json));
                    AppEventHandler.PrintAndLog("Citation Manager: Successfully retrieved citation from online source");
                });
            }
            catch
            {
                AppEventHandler.PrintAndLog("Citation Manager: Failed to retrieve citation");
            }
        }

        private static CitationInfo TryLoadCache()
        {
            try
            {
                if (!File.Exists(CachePath))
                    return null;

                string json = File.ReadAllText(CachePath);
                return JsonSerializer.Deserialize<CitationInfo>(json);
            }
            catch
            {
                return null;
            }
        }

        private static CitationInfo TryLoadBundledCitation()
        {
            try
            {
                var path = NSBundle.MainBundle.PathForResource("citation", "json");
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return null;

                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<CitationInfo>(json);
            }
            catch
            {
                return null;
            }
        }

        private static void SaveCache(CitationInfo citation)
        {
            if (citation == null)
                return;

            try
            {
                string json = JsonSerializer.Serialize(citation);
                File.WriteAllText(CachePath, json);
            }
            catch
            {
            }
        }
    }
}
