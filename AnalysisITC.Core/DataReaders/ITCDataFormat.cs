using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Utilities;

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
        [System.ComponentModel.Description("Ideal continuous mixing")]
        Exponential = 1,
        [System.ComponentModel.Description("Discrete displacement")]
        DiscreteDisplacement = 2,
    }

    // Legacy includes the historical Exponential-concentration/endpoint-heat combination.
    public enum InjectionHeatMethod
    {
        Legacy = 0,
        DumasSimpson = 1,
        DiscreteDisplacement = 2,
    }

    public static class InjectionBookkeeping
    {
        public const string SavedProcessingLabel = "Saved processing — unchanged";
        public const string Help = "MicroCal uses the documented concentration and displacement corrections. "
            + "Ideal continuous mixing models each injection as continuous ideal mixing and accounts for displaced heat along that trajectory. "
            + "Discrete displacement treats each injection as replacement of part of the previous cell mixture, "
            + "accounting for both concentrations and displaced binding heat (Freire et al., 2009). "
            + "FT-ITC retains its equilibrium solvers; no method is assumed to be empirically superior.";

        public static string DisplayName(this DilutionMethod method) => method switch
        {
            DilutionMethod.MicroCal => "MicroCal",
            DilutionMethod.Exponential => "Ideal continuous mixing",
            DilutionMethod.DiscreteDisplacement => "Discrete displacement",
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };

        public static InjectionHeatMethod HeatMethodFor(DilutionMethod method) => method switch
        {
            DilutionMethod.MicroCal => InjectionHeatMethod.Legacy,
            DilutionMethod.Exponential => InjectionHeatMethod.DumasSimpson,
            DilutionMethod.DiscreteDisplacement => InjectionHeatMethod.DiscreteDisplacement,
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
    }
}
