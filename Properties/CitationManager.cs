using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace AnalysisITC
{
    public class CitationInfo
    {
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

        public string ToDisplayString()
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(Title))
                sb.AppendLine(Title);

            var line2 = $"{Authors}, {Journal}, {Year}".Trim(' ', ',');
            if (!string.IsNullOrWhiteSpace(line2))
                sb.AppendLine(line2);

            if (!string.IsNullOrWhiteSpace(DOI))
                sb.AppendLine("DOI: " + DOI);

            if (!string.IsNullOrWhiteSpace(Version))
                sb.AppendLine("Version: " + AppVersion.ShortVersionString);

            return sb.ToString().Trim();
        }

        public string ToBibTeX()
        {
            string year = string.IsNullOrWhiteSpace(Year) ? DateTime.Now.Year.ToString() : Year;

            return
$@"@article{{ftitc{year},
  title = {{{Title}}},
  author = {{{Authors}}},
  journal = {{{Journal}}},
  year = {{{year}}},
  doi = {{{DOI}}},
  version = {{{AppVersion.ShortVersionString}}}
}}";
        }
    }

    public static class CitationManager
    {
        private static string CachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "citation_cache.json");

        public static CitationInfo DefaultCitation => new CitationInfo
        {
            Title = "FT-ITC Analysis",
            Authors = "Frederik Friis Theisen",
            Journal = "",
            Year = "",
            DOI = "",
            Version = AppVersion.ShortVersionString
        };

        public static CitationInfo GetCitation()
        {
            var cached = TryLoadCache();
            if (cached != null)
                return cached;

            return DefaultCitation;
        }

        public static void TryFetchOnlineCitation()
        {
            try
            {
                Task.Run(async () =>
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

        private static void SaveCache(CitationInfo citation)
        {
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