using System;
using System.Globalization;
using AnalysisITC.Core.Application;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>Desktop date presentation, independent of the numeric process culture.</summary>
    public static class LocalDateTimeFormatter
    {
        public static CultureInfo ResolveCulture()
        {
            var locale = AppSettings.Locale;
            if (!string.IsNullOrWhiteSpace(locale))
            {
                try { return CultureInfo.GetCultureInfo(locale); }
                catch (CultureNotFoundException) { }
            }
            return CultureInfo.CurrentUICulture ?? CultureInfo.InvariantCulture;
        }

        public static string ShortDate(DateTime value) => value.ToString("d", ResolveCulture());
        public static string ShortTime(DateTime value) => value.ToString("t", ResolveCulture());
        public static string ShortDateTime(DateTime value) => value.ToString("g", ResolveCulture());
        public static string LongDateTime(DateTime value) => value.ToString("F", ResolveCulture());
    }
}
