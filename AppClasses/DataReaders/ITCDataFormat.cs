using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC;
using AppKit;
using Foundation;
using UniformTypeIdentifiers;
using Utilities;

namespace DataReaders
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
                ITCDataFormat.PEAQITC,
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

        public static UTType[] DataFiles()
        {
            List<UTType> types = new List<UTType>();

            types.AddRange(UTType.GetTypes("itc", UTTagClass.FilenameExtension, UTTypes.Data).ToList());
            types.AddRange(UTType.GetTypes("ta", UTTagClass.FilenameExtension, UTTypes.Data).ToList());
            types.AddRange(UTType.GetTypes("dat", UTTagClass.FilenameExtension, UTTypes.Data).ToList());
            types.AddRange(UTType.GetTypes("aff", UTTagClass.FilenameExtension, UTTypes.Data).ToList());
            types.AddRange(UTType.GetTypes("apj", UTTagClass.FilenameExtension, UTTypes.Data).ToList());

            return types.ToArray();
        }

        public static UTType[] ProjectFile()
        {
            return UTType.GetTypes("ftitc", UTTagClass.FilenameExtension, UTTypes.Data);
        }

        public static UTType[] GetAllUTTypes()
        {
            var list = new List<UTType>();
            list.AddRange(DataFiles());
            list.AddRange(ProjectFile());

            return list.ToArray();
        }
    }

    public enum ITCDataFormat
    {
        [ITCFormat("MicroCal ITC-200","Data format produced by the MicroCal ITC200 instrument", ".itc")]
        ITC200,
        [ITCFormat("VP-ITC", "Data format produced by the VP-ITC instrument", ".vpitc")]
        VPITC,
        [ITCFormat("FT-ITC", "Data format produced by this software", ".ftitc")]
        FTITC,
        Unknown,
        [ITCFormat("TA Instruments", "Data format exported from NanoAnalyze", ".ta")]
        TAITC,
        [ITCFormat("Integrated Heats", "Exports from Origin", new[] { ".dat", ".aff" })]
        IntegratedHeats,
        [ITCFormat("PEAQ-ITC", "Exports from PEAQ-ITC", ".apj")]
        PEAQITC
    }
}
