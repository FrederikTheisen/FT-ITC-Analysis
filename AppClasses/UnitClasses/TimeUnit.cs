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
