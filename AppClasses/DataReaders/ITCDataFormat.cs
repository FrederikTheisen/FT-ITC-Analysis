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
                ITCDataFormat.VPITC,
                ITCDataFormat.TAITC,
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

    public enum ITCDataFormat
    {
        [ITCFormat("MicroCal ITC Data File","Data format produced by the MicroCal ITC200 instrument", ".itc")]
        ITC200,
        [ITCFormat("VP-ITC", "Data format produced by the VP-ITC instrument", ".vpitc")]
        VPITC,
        [ITCFormat("FT-ITC", "Data format produced by this software", ".ftitc")]
        FTITC,
        Unknown,
        [ITCFormat("TA Instruments Nano Analyze", "Data format exported from NanoAnalyze", ".ta")]
        TAITC,
        [ITCFormat("Integrated Heats File", "Exports from Origin and legacy DH exports", new[] { ".dat", ".aff", ".dh" })]
        IntegratedHeats,
        [ITCFormat("PEAQ-ITC Project File", "Exports from PEAQ-ITC", ".apj")]
        PEAQITCProject
    }

    public enum DilutionMethod
    {
        MicroCal,
        Exponential,
    }
}
