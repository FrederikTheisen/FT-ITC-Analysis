using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Utilities;
using System.ComponentModel;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.DataReaders
{
    public class ITCFormatAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> Extensions { get; set; }

        public ITCFormatAttribute(string name, string description, string extension)
        {
            Name = name;
            Description = description;
            Extensions = new List<string> { extension };
        }

        public ITCFormatAttribute(string name, string description, string[] extensions)
        {
            Name = name;
            Description = description;
            Extensions = extensions.ToList();
        }

        public static List<ITCDataFormat> GetAllFormats()
        {
            return new List<ITCDataFormat>
            {
                ITCDataFormat.ITC200,
                ITCDataFormat.TAITC,
                ITCDataFormat.FTXTC,
                ITCDataFormat.FTITC,
                ITCDataFormat.IntegratedHeats,
                ITCDataFormat.PEAQITCProject,
                ITCDataFormat.OriginProject,
                ITCDataFormat.NanoITC,
            };
        }

        public static string[] GetAllExtensions()
        {
            var formats = GetAllFormats();

            var extensions = new List<string>();
            foreach (var format in formats)
                extensions.AddRange(format.GetProperties().Extensions);

            return extensions.ToArray();
        }

    }

    // Keep persisted FTITC source ordinals stable; ordinal 1 belonged to the removed legacy format.
    public enum ITCDataFormat
    {
        [ITCFormat("MicroCal ITC Data File","Data format produced by the MicroCal ITC200 instrument", ".itc")]
        ITC200 = 0,
        [ITCFormat("FT-ITC", "Data format produced by this software", ".ftitc")]
        FTITC = 2,
        [ITCFormat("FT-ITC Project", "Versioned FT-ITC project package", ".ftxtc")]
        FTXTC = 3,
        Unknown = 4,
        [ITCFormat("TA Instruments Nano Analyze", "Data format exported from NanoAnalyze", ".ta")]
        TAITC = 5,
        [ITCFormat("Integrated Heats File", "Delimited integrated heats and fixed-layout DH files", new[] { ".dat", ".aff", ".dh" })]
        IntegratedHeats = 6,
        [ITCFormat("PEAQ-ITC Project File", "Exports from PEAQ-ITC", ".apj")]
        PEAQITCProject = 7,
        [ITCFormat("Origin ITC Project File", "Legacy Origin project containing ITC data", ".opj")]
        OriginProject = 8,
        [ITCFormat("TA Instruments NanoITC Data File", "Native data format produced by NanoITC instruments", ".nitc")]
        NanoITC = 9
    }

    public enum DilutionMethod
    {
        MicroCal = 0,
        [Description("Ideal continuous mixing")]
        Exponential = 1,
        [Description("Discrete displacement")]
        DiscreteDisplacement = 2,
    }

    // Legacy includes the historical Exponential-concentration/endpoint-heat combination.
    public enum InjectionHeatMethod
    {
        MicroCal = 0,
        IdealContinuousMixing = 1,
        DiscreteDisplacement = 2,
    }

    public static class InjectionBookkeeping
    {
        public const string SavedProcessingLabel = "Saved processing — unchanged";
        public const string Help = "Injection bookkeeping controls the cell concentrations and displaced binding heat used by fitting. The selected method is explained below.";

        public static string Description(DilutionMethod? method) => method switch
        {
            DilutionMethod.DiscreteDisplacement => "Recommended starting point for ordinary pulse injections. The injection displaces the previous cell mixture before appreciable mixing; the remaining material then mixes with the injected solution.",
            DilutionMethod.Exponential => "Consider when injection is slow relative to cell mixing. The cell mixes throughout the injection, so the displaced solution changes composition continuously. Also useful for comparing the opposite mixing limit.",
            DilutionMethod.MicroCal => "Use to reproduce or compare MicroCal analyses. This follows its documented displaced-volume convention, including the concentration approximation.",
            _ => "The saved processing method is unknown. Select a method before recalculating concentrations.",
        };

        public static string DisplayName(this DilutionMethod method) => method switch
        {
            DilutionMethod.MicroCal => "MicroCal",
            DilutionMethod.Exponential => "Ideal continuous mixing",
            DilutionMethod.DiscreteDisplacement => "Discrete displacement",
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };

        public static InjectionHeatMethod HeatMethodFor(DilutionMethod method) => method switch
        {
            DilutionMethod.MicroCal => InjectionHeatMethod.MicroCal,
            DilutionMethod.Exponential => InjectionHeatMethod.IdealContinuousMixing,
            DilutionMethod.DiscreteDisplacement => InjectionHeatMethod.DiscreteDisplacement,
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
    }
}
