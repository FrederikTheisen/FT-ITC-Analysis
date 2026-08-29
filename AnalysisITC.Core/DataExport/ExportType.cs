using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Utilities;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Export
{
    public class ExportTypeAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Extension { get; set; }

        public ExportTypeAttribute(string name, string description, string ext)
        {
            Name = name;
            Description = description;
            Extension = ext;
        }

        public string DotExtension() => "." + Extension;
    }

    public enum ExportType
    {
        [ExportType("Thermogram Data", "Export the thermogram data with optional baseline correction.", "csv")]
        Data,
        [ExportType("Integrated Peaks", "Export a file containing injections and integrated peaks.", "csv")]
        Peaks,
        [ExportType("Third Party", "Export a file containing injections and heats.", "csv")]
        ThirdParty,
        [ExportType(MarkdownStrings.ITCsimName, "Export a file compatible with " + MarkdownStrings.ITCsimName + " analysis. " + MarkdownStrings.ITCsimName + " provides analysis of ITC data by numeric simulation of the experiment using COPASI. COPASI is a free software that allows construction of arbitrary models with abstract parameters.", "csv")]
        ITCsim,
        [ExportType("CSV", "Export in comma separated format. Select exported columns in preferences.", "csv")]
        CSV,
        [ExportType("pytc", "Export a .dh file for analysis using pytc. pytc is a python software package for analyzing Isothermal Titration Calorimetry experiments. It does Bayesian and ML fitting. Performs global fits to multiple experiments.", "dh")]
        PYTC,
        [ExportType("MicroCal", "Export a MicroCal style table containing columns such as DH, INJV, Xt, Mt, XMt and so forth. The format is compatible with SEDPHAT analysis.", "dat")]
        MicroCal,
        [ExportType("Combined Data", "Export thermogram samples and integrated peaks side by side in one CSV file.", "csv")]
        InterchangeCsv
    }

    public enum ExportDataSelection
    {
        SelectedData,
        IncludedData,
        AllData
    }

    public static class ExportFormatDescription
    {
        public static string GetOutputUnits(ExportType export, IEnumerable<ExperimentData> data = null)
        {
            return export switch
            {
                ExportType.Data => "Time in s; power in W.",
                ExportType.Peaks => $"{GetXAxisUnits(data)}; enthalpy, SD, model, and residual are J/mol.",
                ExportType.InterchangeCsv => $"Time in s; power in W; peak {GetXAxisUnits(data)}; enthalpy, SD, model, and residual are J/mol.",
                ExportType.MicroCal => "DH in microcal; injection volume in uL; titrant and cell concentrations in mM; XMt is molar ratio; NDH, DY, and Fit are cal/mol.",
                ExportType.PYTC => "Injection volume in uL; heat in microcal; header concentrations in mM; cell volume in mL; temperature in C.",
                ExportType.ITCsim => "Molar ratio; injection volume in L; injection delay in s; peak heat in J/mol. Metadata concentrations are in uM and cell volume is in L.",
                _ => "Units depend on the selected legacy export columns."
            };
        }

        static string GetXAxisUnits(IEnumerable<ExperimentData> data)
        {
            var axes = data?
                .Where(item => item != null)
                .Select(item => item.AxisType)
                .Distinct()
                .ToList() ?? new List<AnalysisXAxisType>();

            if (axes.Count == 1)
            {
                return axes[0] switch
                {
                    AnalysisXAxisType.TitrantConcentration => "X is titrant concentration in M",
                    AnalysisXAxisType.ID => "X is injection number (dimensionless)",
                    _ => "X is molar ratio (dimensionless)"
                };
            }

            return "X depends on the experiment: molar ratio (dimensionless), titrant concentration in M, or injection number (dimensionless)";
        }
    }

    [Flags]
    public enum ExportColumns
    {
        None = 0,

        MolarRatio = 1 << 0,
        Included = 1 << 1,
        Peak = 1 << 2,
        PeakError = 1 << 3,
        Fit = 1 << 4,
        InjectionVolume = 1 << 5,
        InjectionDelay = 1 << 6,
        IntegrationLength = 1 << 7,
        CellConc = 1 << 8,
        SyrConc = 1 << 9,
        Temperature = 1 << 10,

        Concentrations = CellConc | SyrConc,
        InjectionInfo = InjectionVolume | InjectionDelay | PeakError | IntegrationLength | Temperature,

        Default = MolarRatio | Included | Peak | Fit,
        SelectionMinimal = MolarRatio | Peak | Fit,
        SelectionITCsim = MolarRatio | Included | InjectionVolume | InjectionDelay | Peak,
    }

    public class ExportAccessoryViewSettings
    {
        public List<ExperimentData> Data;

        public ExportType Export;
        public bool UnifyTimeAxis;
        public bool ExportBaselineCorrectDataPoints;
        public bool ExportFittedPeaks;
        public bool ExportOffsetCorrected;
        public ExportDataSelection Selection;
        public bool ExportConcentrations;
        public ExportColumns Columns;
        public string OutputBaseName;

        public bool FittedPeakExportEnabled;
        public bool BaselineCorrectionEnabled;

        static ExportAccessoryViewSettings Default(ExportType export)
        {
            var settings = new ExportAccessoryViewSettings()
            {
                Export = export,
                UnifyTimeAxis = AppSettings.UnifyTimeAxisForExport,
                ExportBaselineCorrectDataPoints = AppSettings.ExportBaselineCorrectedData,
                ExportFittedPeaks = AppSettings.ExportFitPointsWithPeaks,
                Selection = AppSettings.ExportSelectionMode,
                ExportOffsetCorrected = true,
                ExportConcentrations = true,
                Columns = AppSettings.ExportColumns,
                OutputBaseName = AppSettings.ExportOutputBaseName,
            };

            settings.SetData();

            return settings;
        }

        /// <summary>
        /// Default setting for data export
        /// </summary>
        /// <returns></returns>
        public static ExportAccessoryViewSettings DataDefault()
        {
            return Default(ExportType.Data);
        }

        /// <summary>
        /// Default settings for peak export
        /// </summary>
        /// <returns></returns>
        public static ExportAccessoryViewSettings PeaksDefault()
        {
            return Default(ExportType.Peaks);
        }

        public static ExportAccessoryViewSettings CreateDefault(ExportType export) => Default(export);

        public void SetData()
        {
            Data = Selection switch
            {
                ExportDataSelection.IncludedData => DataManager.Data.Where(d => d.Include).ToList(),
                ExportDataSelection.AllData => DataManager.Data,
                _ => new[] { DataManager.Current }.Where(d => d != null).ToList(),
            };

            BaselineCorrectionEnabled = Data.Any() && Data.All(d => d.BaseLineCorrectedDataPoints != null);
            FittedPeakExportEnabled = Data.Any() && Data.All(d => d.Solution != null);

            if (!BaselineCorrectionEnabled) ExportBaselineCorrectDataPoints = false;
            if (!FittedPeakExportEnabled)
            {
                ExportFittedPeaks = false;
                ExportOffsetCorrected = false;
            }

            if (string.IsNullOrWhiteSpace(OutputBaseName))
                OutputBaseName = Data.Count == 1 ? System.IO.Path.GetFileNameWithoutExtension(Data[0].Name) : "FT-ITC Export";
        }
    }
    }
