using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Utils;

namespace AnalysisITC
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
        [ExportType("Peak", "Export a file containing injections and heats.", "csv")]
        Peaks,
        [ExportType("Third Party", "Export a file containing injections and heats.", "csv")]
        ThirdParty,
        [ExportType(MarkdownStrings.ITCsimName, "Export a file compatible with " + MarkdownStrings.ITCsimName + " analysis. " + MarkdownStrings.ITCsimName + " provides analysis of ITC data by numeric simulation of the experiment using COPASI. COPASI is a free software that allows construction of arbitrary models with abstract parameters.", "csv")]
        ITCsim,
        [ExportType("CSV", "Export in comma separated format. Select exported columns in preferences.", "csv")]
        CSV,
        [ExportType("pytc", "Export a .dh file for analysis using pytc. pytc is a python software package for analyzing Isothermal Titration Calorimetry experiments. It does Bayesian and ML fitting. Performs global fits to multiple experiments, has a clean Python API, and is designed for easy extension with new models.", "dh")]
        PYTC,
        [ExportType("MicroCal", "Export a MicroCal style table containing columns such as DH, INJV, Xt, Mt, XMt and so forth. The format is compatible with SEDPHAT analysis.", "dat")]
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

        public bool FittedPeakExportEnabled;
        public bool BaselineCorrectionEnabled;

        static ExportAccessoryViewSettings Default()
        {
            var settings = new ExportAccessoryViewSettings()
            {
                Export = ExportType.Data,
                UnifyTimeAxis = AppSettings.UnifyTimeAxisForExport,
                ExportBaselineCorrectDataPoints = AppSettings.ExportBaselineCorrectedData,
                ExportFittedPeaks = AppSettings.ExportFitPointsWithPeaks,
                Selection = AppSettings.ExportSelectionMode,
                ExportOffsetCorrected = true,
                ExportConcentrations = true,
                Columns = AppSettings.ExportColumns,
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
            var s = Default();
            s.Export = ExportType.Data;

            return s;
        }

        /// <summary>
        /// Default settings for peak export
        /// </summary>
        /// <returns></returns>
        public static ExportAccessoryViewSettings PeaksDefault()
        {
            var s = Default();
            s.Export = ExportType.Peaks;

            return s;
        }

        public void SetData()
        {
            Data = Selection switch
            {
                ExportDataSelection.IncludedData => DataManager.Data.Where(d => d.Include).ToList(),
                ExportDataSelection.AllData => DataManager.Data,
                _ => new List<ExperimentData> { DataManager.Current },
            };

            BaselineCorrectionEnabled = !Data.Any(d => d.BaseLineCorrectedDataPoints == null);
            FittedPeakExportEnabled = !Data.Any(d => d.Solution == null);

            if (!BaselineCorrectionEnabled) ExportBaselineCorrectDataPoints = false;
            if (!FittedPeakExportEnabled)
            {
                ExportFittedPeaks = false;
                ExportOffsetCorrected = false;
            }
        }
    }
    }

