using System;

namespace AnalysisITC
{
    public class TimeUnitAttribute : Attribute
    {
        public string Name { get; set; }
        public string Short { get; set; }
        public string Letter { get; set; }
        public double Mod { get; set; }

        public TimeUnitAttribute(string name, string shortname, string letter, double mod)
        {
            Name = name;
            Short = shortname;
            Letter = letter;
            Mod = mod;
        }

        public static string FormatTimeSpanShort(TimeSpan time)
        {
            if (time < TimeSpan.Zero)
                return "-" + FormatTimeSpanShort(time.Negate());

            if (time.TotalSeconds < 1)
                return $"{time.TotalMilliseconds:0} ms";

            if (time.TotalMinutes < 1)
                return $"{time.TotalSeconds:0.#} s";

            if (time.TotalHours < 1)
                return $"{time.TotalMinutes:0.#} min";

            if (time.TotalDays < 1)
                return $"{time.TotalHours:0.#} h";

            return $"{time.TotalDays:0.#} d";
        }
    }

    public enum TimeUnit
    {
        [TimeUnit("Seconds", "sec", "s", 1)]
        Second,
        [TimeUnit("Minutes", "min", "m", 1/60.0)]
        Minute,
        [TimeUnit("Hours", "hour", "h", 1/3600.0)]
        Hour
    }
}
