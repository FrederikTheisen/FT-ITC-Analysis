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
        [ITCFormat("Integrated Heats File", "Exports from Origin and legacy DH exports", new[] { ".dat", ".aff", ".dh" })]
        IntegratedHeats = 6,
        [ITCFormat("PEAQ-ITC Project File", "Exports from PEAQ-ITC", ".apj")]
        PEAQITCProject = 7
    }

    public enum DilutionMethod
    {
        MicroCal,
        Exponential,
    }
}
