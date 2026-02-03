using System;
using AnalysisITC.Utils;

namespace AnalysisITC
{
    public class ExportTypeAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; set; }

        public ExportTypeAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    public enum ExportType
    {
        [ExportType("Thermogram Data", "Export the thermogram data with optional baseline correction.")]
        Data,
        [ExportType("Peak", "Export a file containing injections and heats.")]
        Peaks,
        [ExportType(MarkdownStrings.ITCsimName, "Export a file compatible with " + MarkdownStrings.ITCsimName + " analysis. " + MarkdownStrings.ITCsimName + " provides analysis of ITC data by numeric simulation of the experiment using COPASI. COPASI is a free software that allows construction of arbitrary models with abstract parameters.")]
        ITCsim,
        [ExportType("CSV", "Export in comma separated format. Select exported columns in preferences.")]
        CSV,
        [ExportType("pytc", "Export a .dh file for analysis using pytc. pytc is a python software package for analyzing Isothermal Titration Calorimetry experiments. It does Bayesian and ML fitting. Performs global fits to multiple experiments, has a clean Python API, and is designed for easy extension with new models.")]
        PYTC,
        [ExportType("MicroCal", "Export a MicroCal style table containing columns such as DH, INJV, Xt, Mt, XMt and so forth. The format is compatible with SEDPHAT analysis.")]
        MicroCal
    }

    public enum ExportDataSelection
    {
        SelectedData,
        IncludedData,
        AllData
    }

    [Flags]
    public enum ExportColumns
    {
        None = 0,

        MolarRatio = 1 << 0,
        Included = 1 << 1,
        Peak = 1 << 2,
        Fit = 1 << 3,
        InjectionVolume = 1 << 4,
        InjectionDelay = 1 << 5,
        CellConc = 1 << 6,
        SyrConc = 1 << 7,

        Concentrations = CellConc | SyrConc,
        InjectionInfo = InjectionVolume | InjectionDelay,

        Default = MolarRatio | Included | Peak | Fit,
        SelectionMinimal = MolarRatio | Peak | Fit,
        SelectionITCsim = MolarRatio | Included | InjectionVolume | InjectionDelay | Peak | Fit,
    }
}

