using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;

namespace AnalysisITC
{
    public class CitationInfo
    {
        public string Title { get; set; }
        public string Authors { get; set; }
        public string Journal { get; set; }
        public string Year { get; set; }
        public string DOI { get; set; }
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
                sb.AppendLine("Version: " + Version);

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
  version = {{{Version}}}
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
                string url = "https://example.org/ftitc/citation.json";
                using var client = new WebClient();
                string json = client.DownloadString(url);

                SaveCache(JsonSerializer.Deserialize<CitationInfo>(json));
            }
            catch
            {

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