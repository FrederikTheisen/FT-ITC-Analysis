using System;
using System.Collections.Generic;
using System.Globalization;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>
    /// Formats report generation times as "2 Oct 2026, 10:41 EEST". Platform time zone names are
    /// localized, so abbreviations come from known IANA and Windows zone IDs; unknown zones fall back
    /// to a compact UTC offset such as "UTC+3" or "UTC+5:30".
    /// </summary>
    public static class ReportTimestampFormatter
    {
        static readonly (string Standard, string Daylight) Utc = ("UTC", "UTC");
        static readonly (string, string) Gmt = ("GMT", "GMT");
        static readonly (string, string) Uk = ("GMT", "BST");
        static readonly (string, string) Ireland = ("GMT", "IST");
        static readonly (string, string) Western = ("WET", "WEST");
        static readonly (string, string) Central = ("CET", "CEST");
        static readonly (string, string) Eastern = ("EET", "EEST");
        static readonly (string, string) Moscow = ("MSK", "MSK");
        static readonly (string, string) Israel = ("IST", "IDT");
        static readonly (string, string) India = ("IST", "IST");
        static readonly (string, string) China = ("CST", "CST");
        static readonly (string, string) Japan = ("JST", "JST");
        static readonly (string, string) Korea = ("KST", "KST");
        static readonly (string, string) AustraliaWestern = ("AWST", "AWST");
        static readonly (string, string) AustraliaCentral = ("ACST", "ACDT");
        static readonly (string, string) AustraliaEastern = ("AEST", "AEDT");
        static readonly (string, string) NewZealand = ("NZST", "NZDT");
        static readonly (string, string) UsEastern = ("EST", "EDT");
        static readonly (string, string) UsCentral = ("CST", "CDT");
        static readonly (string, string) UsMountain = ("MST", "MDT");
        static readonly (string, string) Arizona = ("MST", "MST");
        static readonly (string, string) UsPacific = ("PST", "PDT");
        static readonly (string, string) Alaska = ("AKST", "AKDT");
        static readonly (string, string) Hawaii = ("HST", "HST");

        static readonly Dictionary<string, (string Standard, string Daylight)> Abbreviations = Build();

        static Dictionary<string, (string Standard, string Daylight)> Build()
        {
            var map = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            void Add((string, string) abbreviation, params string[] ids)
            {
                foreach (var id in ids) map[id] = abbreviation;
            }

            Add(Utc, "UTC", "Etc/UTC", "Etc/UCT", "Etc/Universal", "Etc/Zulu", "Universal", "Zulu");
            Add(Gmt, "GMT", "Etc/GMT", "Etc/Greenwich", "Greenwich", "Atlantic/Reykjavik", "Greenwich Standard Time");
            Add(Uk, "Europe/London", "Europe/Guernsey", "Europe/Jersey", "Europe/Isle_of_Man", "GB", "GMT Standard Time");
            Add(Ireland, "Europe/Dublin", "Eire");
            Add(Western, "Europe/Lisbon", "Portugal", "Atlantic/Canary", "Atlantic/Madeira", "Atlantic/Faroe", "Atlantic/Faeroe");
            Add(Central, "CET", "Europe/Amsterdam", "Europe/Andorra", "Europe/Belgrade", "Europe/Berlin",
                "Europe/Bratislava", "Europe/Brussels", "Europe/Budapest", "Europe/Busingen", "Europe/Copenhagen",
                "Europe/Gibraltar", "Europe/Ljubljana", "Europe/Luxembourg", "Europe/Madrid", "Europe/Malta",
                "Europe/Monaco", "Europe/Oslo", "Europe/Paris", "Europe/Podgorica", "Europe/Prague", "Europe/Rome",
                "Europe/San_Marino", "Europe/Sarajevo", "Europe/Skopje", "Europe/Stockholm", "Europe/Tirane",
                "Europe/Vaduz", "Europe/Vatican", "Europe/Vienna", "Europe/Warsaw", "Europe/Zagreb", "Europe/Zurich",
                "Arctic/Longyearbyen", "Africa/Ceuta", "Poland",
                "W. Europe Standard Time", "Romance Standard Time", "Central Europe Standard Time",
                "Central European Standard Time");
            Add(Eastern, "EET", "Europe/Athens", "Europe/Bucharest", "Europe/Chisinau", "Europe/Helsinki",
                "Europe/Kiev", "Europe/Kyiv", "Europe/Mariehamn", "Europe/Nicosia", "Europe/Riga", "Europe/Sofia",
                "Europe/Tallinn", "Europe/Uzhgorod", "Europe/Vilnius", "Europe/Zaporozhye", "Asia/Nicosia",
                "Asia/Famagusta", "Asia/Beirut", "Africa/Cairo", "Egypt",
                "FLE Standard Time", "GTB Standard Time", "E. Europe Standard Time", "Middle East Standard Time",
                "Egypt Standard Time");
            Add(Moscow, "Europe/Moscow", "Europe/Simferopol", "W-SU", "Russian Standard Time");
            Add(Israel, "Asia/Jerusalem", "Asia/Tel_Aviv", "Israel", "Israel Standard Time");
            Add(India, "Asia/Kolkata", "Asia/Calcutta", "India Standard Time");
            Add(China, "Asia/Shanghai", "Asia/Chongqing", "Asia/Harbin", "Asia/Macau", "Asia/Taipei", "PRC",
                "China Standard Time", "Taipei Standard Time");
            Add(Japan, "Asia/Tokyo", "Japan", "Tokyo Standard Time");
            Add(Korea, "Asia/Seoul", "ROK", "Korea Standard Time");
            Add(AustraliaWestern, "Australia/Perth", "Australia/West", "W. Australia Standard Time");
            Add(AustraliaCentral, "Australia/Adelaide", "Australia/Broken_Hill", "Australia/South",
                "Cen. Australia Standard Time");
            Add(("ACST", "ACST"), "Australia/Darwin", "Australia/North", "AUS Central Standard Time");
            Add(AustraliaEastern, "Australia/Sydney", "Australia/Melbourne", "Australia/Canberra", "Australia/Hobart",
                "Australia/ACT", "Australia/NSW", "Australia/Victoria", "Australia/Tasmania",
                "AUS Eastern Standard Time", "Tasmania Standard Time");
            Add(("AEST", "AEST"), "Australia/Brisbane", "Australia/Lindeman", "Australia/Queensland",
                "E. Australia Standard Time");
            Add(NewZealand, "Pacific/Auckland", "Antarctica/McMurdo", "NZ", "New Zealand Standard Time");
            Add(UsEastern, "America/New_York", "America/Detroit", "America/Toronto", "America/Nassau",
                "America/Indiana/Indianapolis", "America/Indianapolis", "America/Kentucky/Louisville",
                "America/Louisville", "US/Eastern", "EST5EDT", "Eastern Standard Time", "US Eastern Standard Time");
            Add(UsCentral, "America/Chicago", "America/Winnipeg", "America/Menominee", "America/Indiana/Knox",
                "US/Central", "CST6CDT", "Central Standard Time");
            Add(UsMountain, "America/Denver", "America/Edmonton", "America/Boise", "US/Mountain", "MST7MDT",
                "Mountain Standard Time");
            Add(Arizona, "America/Phoenix", "US/Arizona", "US Mountain Standard Time");
            Add(UsPacific, "America/Los_Angeles", "America/Vancouver", "America/Tijuana", "US/Pacific", "PST8PDT",
                "Pacific Standard Time");
            Add(Alaska, "America/Anchorage", "America/Juneau", "America/Sitka", "US/Alaska", "Alaskan Standard Time");
            Add(Hawaii, "Pacific/Honolulu", "US/Hawaii", "Hawaiian Standard Time");
            return map;
        }

        public static string Format(DateTime utc) => Format(utc, TimeZoneInfo.Local);

        public static string Format(DateTime utc, TimeZoneInfo zone)
        {
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            utc = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
            return local.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " " + ZoneLabel(utc, zone);
        }

        static string ZoneLabel(DateTime utc, TimeZoneInfo zone)
        {
            if (Abbreviations.TryGetValue(zone.Id ?? "", out var abbreviation))
                return zone.IsDaylightSavingTime(utc) ? abbreviation.Daylight : abbreviation.Standard;

            var offset = zone.GetUtcOffset(utc);
            if (offset == TimeSpan.Zero) return "UTC";
            var sign = offset < TimeSpan.Zero ? "-" : "+";
            var magnitude = offset.Duration();
            return "UTC" + sign + magnitude.Hours.ToString(CultureInfo.InvariantCulture)
                + (magnitude.Minutes == 0 ? "" : ":" + magnitude.Minutes.ToString("00", CultureInfo.InvariantCulture));
        }
    }
}
