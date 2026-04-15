using System;
using System.Collections.Generic;
using System.Linq;
using AppKit;

namespace AnalysisITC
{
    public enum StoichiometryPreset
    {
        OneToTetramer,
        OneToTrimer,
        OneToDimer,
        OneToOne,
        TwoSites,
        ThreeSites,
        FourSites
    }

    public sealed class StoichiometryOption
    {
        public StoichiometryPreset Preset { get; }
        public string Title { get; }
        public double Factor { get; }

        public StoichiometryOption(StoichiometryPreset preset, string title, double factor)
        {
            Preset = preset;
            Title = title;
            Factor = factor;
        }

        public override string ToString() => Title;
    }

    public static class StoichiometryOptions
    {
        public static readonly IReadOnlyList<StoichiometryOption> Presets =
            new List<StoichiometryOption>
            {
                new(StoichiometryPreset.OneToTetramer,"One to tetramer (1:4)",0.25),
                new(StoichiometryPreset.OneToTrimer,  "One to trimer (1:3)",  1.0 / 3.0),
                new(StoichiometryPreset.OneToDimer,   "One to dimer (1:2)",   0.5),
                new(StoichiometryPreset.OneToOne,     "One to one (1:1)",     1.0),
                new(StoichiometryPreset.TwoSites,     "Two sites (2:1)",      2.0),
                new(StoichiometryPreset.ThreeSites,   "Three sites (3:1)",    3.0),
                new(StoichiometryPreset.FourSites,    "Four sites (4:1)",     4.0),
            };

        public static StoichiometryOption Default => Presets[3];

        public static StoichiometryOption Get(StoichiometryPreset preset)
        {
            return Presets.First(x => x.Preset == preset);
        }

        public static StoichiometryOption GetClosest(double factor, double tolerance = 1e-5)
        {
            foreach (var item in Presets)
            {
                if (Math.Abs(item.Factor - factor) < tolerance)
                    return item;
            }

            return Default;
        }

        public static string FormatAsTitle(double factor, double tolerance = 1e-9)
        {
            return GetClosest(factor, tolerance).Title;
        }

        public static string FormatAsParameter(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "";

            double tolerance = 1e-4;

            // General integer-like values (one or more)
            var rounded = Math.Round(value);
            if (Math.Abs(value - rounded) < tolerance)
                return rounded.ToString("F0") + ":1";
           
            // General reciprocal-style values (e.g. 0.2 -> 1:5)
            if (value > 0 && value < 1)
            {
                var reciprocal = Math.Round(1.0 / value);
                if (Math.Abs(value - (1.0 / reciprocal)) < tolerance)
                    return $"1:{reciprocal}";
            }

            return value.ToString();
        }
    }
}