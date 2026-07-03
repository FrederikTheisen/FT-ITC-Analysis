using System;
using System.Linq;

using AnalysisITC.Platform;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Presentation;

namespace AnalysisITC.Core.Application
{
    public static class AppSettings
    {
        public static event EventHandler SettingsDidUpdate;
        public static event EventHandler SettingsApplied;

        static ISettingsStore Storage => PlatformServices.SettingsStore;
        public static string Locale { get; set; } = "en-US";

        //General
        public static double ReferenceTemperature { get; set; } = 25.0;
        public static EnergyUnit EnergyUnit { get; set; } = EnergyUnit.KiloJoule;
        public static ColorSchemes ColorScheme { get; set; } = ColorSchemes.Default;
        public static ColorSchemeGradientMode ColorSchemeGradientMode { get; set; } = ColorSchemeGradientMode.Smooth;
        public static ConcentrationUnit DefaultConcentrationUnit { get; set; } = ConcentrationUnit.µM;
        public static ITCInstrument DefaultDesignerInstrument { get; set; } = ITCInstrument.MicroCalITC200;
        public static int MaxDegreeOfParallelism { get; set; } = 10;
        public static bool PerformOnlineChecksOnLaunch { get; set; } = true;
        public static bool ConfirmRemoveDelete { get; set; } = true;

        public static bool Verbose { get; set; } = false;

        private static string lastDocumentPath = null;
        private static string[] lastDocumentPaths = null;
        public static string LastDocumentPath { get => lastDocumentPath; set { lastDocumentPath = NormalizeDocumentPath(value); Save(); } }
        public static string[] LastDocumentPaths { get => lastDocumentPaths; set { lastDocumentPaths = NormalizeDocumentPaths(value); Save(); } }

        //Processing
        public static PeakFitAlgorithm PeakFitAlgorithm { get; set; } = PeakFitAlgorithm.SingleExponential;
        public static bool DiscardIntegrationRegionForBaseline { get; set; } = true;
        public static bool IncludeBufferInIonicStrengthCalc { get; set; } = true;
        public static DilutionMethod DilutionCalculationMethod { get; set; } = DilutionMethod.MicroCal;
        public static BufferSubtractionMethod BufferSubtractionDefaultMethod { get; set; } = BufferSubtractionMethod.MatchedInjection;
        public static bool ReprocessIntegratedHeatDataOnLoad { get; set; } = true;
        public static SplineInterpolator.SplinePointDensity DefaultSplinePointDensity { get; set; } = SplineInterpolator.SplinePointDensity.Balanced;
        public static SplineInterpolator.SplineHandleMode DefaultSplineHandleMode { get; set; } = SplineInterpolator.SplineHandleMode.Mean;
        public static bool DefaultSplinePointTimeDragging { get; set; } = false;
        public static bool IntegrationRegionCopyIncludesStart { get; set; } = false;

        //Fitting
        public static bool InputAffinityAsDissociationConstant { get; set; } = true;

        public static ErrorEstimationMethod DefaultErrorEstimationMethod { get; set; } = ErrorEstimationMethod.BootstrapResiduals;
        public static int DefaultBootstrapIterations { get; set; } = 100;
        public static double MinimumTemperatureSpanForFitting { get; internal set; } = 2;
        public static double MinimumIonSpanForFitting { get; internal set; } = 0.01;
        public static bool IncludeConcentrationErrorsInBootstrap { get; set; } = false;
        public static double ConcentrationAutoVariance { get; set; } = 0.05;
        public static bool IsConcentrationAutoVarianceEnabled { get; set; } = ConcentrationAutoVariance > 0.001;

        public static double OptimizerTolerance { get; set; } = 0.5;
        public static int MaximumOptimizerIterations { get; set; } = 2000;
        public static bool EnableExtendedParameterLimits { get; set; } = false;
        public static ParameterLimitSetting ParameterLimitSetting { get; set; } = ParameterLimitSetting.Standard;
        public static SolverAlgorithm DefaultSolverAlgorithm { get; set; } = SolverAlgorithm.NelderMead;
        public static bool UseInjectionErrorWeightedFitting { get; set; } = false;

        //Analysis
        public static bool BuffersPreparedAtRoomTemperature { get; set; } = true;
        public static bool CreateSingleAnalysisResult { get; set; } = false;
        public static bool CreateGlobalAnalysisResult { get; set; } = true;
        public static bool AutoOpenNewAnalysisResult { get; set; } = true;
        public static FinalFigureDisplayParameters AnalysisParameterDisplay { get; set; } =
            FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Fitted | FinalFigureDisplayParameters.Derived;

        //Final figure
        public static double[] FinalFigureDimensions { get; set; } = new double[2] { 6.5, 10.0 };
        public static FinalFigureDisplayParameters FinalFigureParameterDisplay { get; set; } = FinalFigureDisplayParameters.Default;
        public static DisplayAttributeOptions DisplayAttributeOptions { get; set; } = DisplayAttributeOptions.Default;
        public static bool FinalFigureShowParameterBoxAsDefault { get; set; } = true;
        public static bool FinalFigureShowDetailsAsDefault { get; set; } = true;
        public static bool FinalFigureShowModelInfoAsDefault { get; set; } = true;
        public static NumberPrecision NumberPrecision { get; set; } = NumberPrecision.Standard;
        public static UncertaintyDisplayStyle UncertaintyDisplayStyle { get; set; } = UncertaintyDisplayStyle.StandardDeviation;
        public static bool ShowResidualGraph { get; set; } = true;
        public static bool ShowResidualGraphGap { get; set; } = true;
        public static bool UnifyResidualGraphAxis { get; set; } = false;
        public static LineSmoothness FitLineSmoothness { get; set; } = LineSmoothness.Spline;
        public static bool AutoAxesIgnoresBadData { get; set; } = true;

        //Export
        public static bool UnifyTimeAxisForExport { get; set; } = true;
        public static bool ExportBaselineCorrectedData { get; set; } = true;
        public static bool ExportFitPointsWithPeaks { get; set; } = true;
        public static ExportDataSelection ExportSelectionMode { get; set; } = ExportDataSelection.SelectedData;
        public static int NumOfDecimalsToExport { get; set; } = 2;
        public static ExportColumns ExportColumns { get; set; } = ExportColumns.Default;

        public static void Initialize()
        {
            AppEventHandler.PrintAndLog("Initializing Settings...");

            Load();
        }

        public static void Save()
        {
            ApplySettings();
            SaveToStorage();
            Storage.Synchronize();
            SettingsDidUpdate?.Invoke(null, null);
        }

        static void SaveToStorage()
        {
            Storage.SetDouble("ReferenceTemperature", ReferenceTemperature);
            Storage.SetInt("EnergyUnit", (int)EnergyUnit);
            Storage.SetInt("DefaultErrorEstimationMethod", (int)DefaultErrorEstimationMethod);
            Storage.SetInt("DefaultBootstrapIterations", DefaultBootstrapIterations);
            Storage.SetDouble("MinimumTemperatureSpanForFitting", MinimumTemperatureSpanForFitting);
            Storage.SetBool("IncludeConcentrationErrorsInBootstrap", IncludeConcentrationErrorsInBootstrap);
            Storage.SetDouble("OptimizerTolerance", OptimizerTolerance);
            Storage.SetInt("MaximumOptimizerIterations", MaximumOptimizerIterations);
            Storage.SetInt("ColorScheme", (int)ColorScheme);
            Storage.SetInt("ColorShcemeGradientMode", (int)ColorSchemeGradientMode);
            Storage.SetDouble("ConcentrationAutoVariance", ConcentrationAutoVariance);
            Storage.SetBool("UnifyTimeAxisForExport", UnifyTimeAxisForExport);
            Storage.SetBool("ExportFitPointsWithPeaks", ExportFitPointsWithPeaks);
            Storage.SetInt("ExportSelectionMode", (int)ExportSelectionMode);
            Storage.SetInt("FinalFigureParameterDisplay", (int)FinalFigureParameterDisplay);
            Storage.SetBool("ExportBaselineCorrectedData", ExportBaselineCorrectedData);
            Storage.SetInt("DefaultConcentrationUnit", (int)DefaultConcentrationUnit);
            Storage.SetInt("DefaultDesignerInstrument", (int)DefaultDesignerInstrument);
            Storage.SetBool("InputAffinityAsDissociationConstant", InputAffinityAsDissociationConstant);
            Storage.SetString("LastDocumentUrl", LastDocumentPath);
            Storage.SetBool("EnableExtendedParameterLimits", EnableExtendedParameterLimits);
            Storage.SetInt("ParameterLimitSetting", (int)ParameterLimitSetting);
            Storage.SetBool("BuffersPreparedAtRoomTemperature", BuffersPreparedAtRoomTemperature);
            Storage.SetBool("CreateSingleAnalysisResult", CreateSingleAnalysisResult);
            Storage.SetBool("CreateGlobalAnalysisResult", CreateGlobalAnalysisResult);
            Storage.SetBool("AutoOpenNewAnalysisResult", AutoOpenNewAnalysisResult);
            Storage.SetInt("AnalysisParameterDisplay", (int)AnalysisParameterDisplay);
            Storage.SetInt("NumOfDecimalsToExport", NumOfDecimalsToExport);
            Storage.SetDouble("MinimumIonSpanForFitting", MinimumIonSpanForFitting);
            Storage.SetBool("FinalFigureShowParameterBoxAsDefault", FinalFigureShowParameterBoxAsDefault);
            Storage.SetBool("FinalFigureShowDetailsAsDefault", FinalFigureShowDetailsAsDefault);
            Storage.SetBool("FinalFigureShowModelInfoAsDefault", FinalFigureShowModelInfoAsDefault);
            Storage.SetInt("PeakFitAlgorithm", (int)PeakFitAlgorithm);
            Storage.SetInt("NumberPrecision", (int)NumberPrecision);
            Storage.SetInt("UncertaintyDisplayStyle", (int)UncertaintyDisplayStyle);
            Storage.SetBool("IncludeBufferInIonicStrengthCalc", IncludeBufferInIonicStrengthCalc);
            Storage.SetInt("DisplayAttributeOptions", (int)DisplayAttributeOptions);
            Storage.SetInt("ExportColumns", (int)ExportColumns);
            Storage.SetBool("ShowResidualGraph", ShowResidualGraph);
            Storage.SetBool("ShowResidualGraphGap", ShowResidualGraphGap);
            Storage.SetBool("UnifyResidualGraphAxis", UnifyResidualGraphAxis);
            Storage.SetInt("FitLineSmoothness", (int)FitLineSmoothness);
            Storage.SetBool("DiscardIntegrationRegionForBaseline", DiscardIntegrationRegionForBaseline);
            Storage.SetInt("SolverAlgorithm", (int)DefaultSolverAlgorithm);
            Storage.SetBool("UseInjectionErrorWeightedFitting", UseInjectionErrorWeightedFitting);
            Storage.SetBool("AutoAxesIgnoresBadData", AutoAxesIgnoresBadData);
            Storage.SetInt("DilutionCalculationMethod", (int)DilutionCalculationMethod);
            Storage.SetInt("BufferSubtractionDefaultMethod", (int)BufferSubtractionDefaultMethod);
            Storage.SetBool("ReprocessIntegratedHeatDataOnLoad", ReprocessIntegratedHeatDataOnLoad);
            Storage.SetInt("DefaultSplinePointDensity", (int)DefaultSplinePointDensity);
            Storage.SetInt("DefaultSplineHandleMode", (int)DefaultSplineHandleMode);
            Storage.SetBool("DefaultSplinePointTimeDragging", DefaultSplinePointTimeDragging);
            Storage.SetBool("IntegrationRegionCopyIncludesStart", IntegrationRegionCopyIncludesStart);
            Storage.SetBool("PerformOnlineChecksOnLaunch", PerformOnlineChecksOnLaunch);
            Storage.SetBool("ConfirmRemoveDelete", ConfirmRemoveDelete);

            Storage.SetStringArray("LastDocumentUrls", LastDocumentPaths);
            Storage.SetDoubleArray("FinalFigureDimensions", FinalFigureDimensions);

            Storage.SetBool("IsSaved", true);
        }

        public static void Load()
        {
            if (!Storage.Contains("IsSaved") || !Storage.GetBool("IsSaved"))
            {
                Console.WriteLine("No settings are stored.");
            }
            else Console.WriteLine("There are {0} settings stored.", Storage.Count);

            ReferenceTemperature = Storage.GetDouble("ReferenceTemperature", ReferenceTemperature);
            EnergyUnit = (EnergyUnit)Storage.GetInt("EnergyUnit", (int)EnergyUnit);
            DefaultErrorEstimationMethod = (ErrorEstimationMethod)Storage.GetInt("DefaultErrorEstimationMethod", (int)DefaultErrorEstimationMethod);
            DefaultBootstrapIterations = Storage.GetInt("DefaultBootstrapIterations", DefaultBootstrapIterations);
            MinimumTemperatureSpanForFitting = Storage.GetDouble("MinimumTemperatureSpanForFitting", MinimumTemperatureSpanForFitting);
            IncludeConcentrationErrorsInBootstrap = Storage.GetBool("IncludeConcentrationErrorsInBootstrap", IncludeConcentrationErrorsInBootstrap);
            OptimizerTolerance = Storage.GetDouble("OptimizerTolerance", OptimizerTolerance);
            MaximumOptimizerIterations = Storage.GetInt("MaximumOptimizerIterations", MaximumOptimizerIterations);
            ColorScheme = (ColorSchemes)Storage.GetInt("ColorScheme", (int)ColorScheme);
            ColorSchemeGradientMode = (ColorSchemeGradientMode)Storage.GetInt("ColorShcemeGradientMode", (int)ColorSchemeGradientMode);
            ConcentrationAutoVariance = Storage.GetDouble("ConcentrationAutoVariance", ConcentrationAutoVariance);
            UnifyTimeAxisForExport = Storage.GetBool("UnifyTimeAxisForExport", UnifyTimeAxisForExport);
            ExportFitPointsWithPeaks = Storage.GetBool("ExportFitPointsWithPeaks", ExportFitPointsWithPeaks);
            ExportSelectionMode = (ExportDataSelection)Storage.GetInt("ExportSelectionMode", (int)ExportSelectionMode);
            EnableExtendedParameterLimits = Storage.GetBool("EnableExtendedParameterLimits", EnableExtendedParameterLimits);
            ParameterLimitSetting = (ParameterLimitSetting)Storage.GetInt("ParameterLimitSetting", (int)ParameterLimitSetting);
            FinalFigureParameterDisplay = (FinalFigureDisplayParameters)Storage.GetInt("FinalFigureParameterDisplay", (int)FinalFigureParameterDisplay);
            FinalFigureDimensions = Storage.GetDoubleArray("FinalFigureDimensions", FinalFigureDimensions);
            ExportBaselineCorrectedData = Storage.GetBool("ExportBaselineCorrectedData", ExportBaselineCorrectedData);
            DefaultConcentrationUnit = (ConcentrationUnit)Storage.GetInt("DefaultConcentrationUnit", (int)DefaultConcentrationUnit);
            DefaultDesignerInstrument = NormalizeDesignerInstrument(Storage.GetInt("DefaultDesignerInstrument", (int)DefaultDesignerInstrument));
            InputAffinityAsDissociationConstant = Storage.GetBool("InputAffinityAsDissociationConstant", InputAffinityAsDissociationConstant);
            lastDocumentPath = NormalizeDocumentPath(Storage.GetString("LastDocumentUrl"));
            BuffersPreparedAtRoomTemperature = Storage.GetBool("BuffersPreparedAtRoomTemperature", BuffersPreparedAtRoomTemperature);
            CreateSingleAnalysisResult = Storage.GetBool("CreateSingleAnalysisResult", CreateSingleAnalysisResult);
            CreateGlobalAnalysisResult = Storage.GetBool("CreateGlobalAnalysisResult", CreateGlobalAnalysisResult);
            AutoOpenNewAnalysisResult = Storage.GetBool("AutoOpenNewAnalysisResult", AutoOpenNewAnalysisResult);
            AnalysisParameterDisplay = (FinalFigureDisplayParameters)Storage.GetInt("AnalysisParameterDisplay", (int)AnalysisParameterDisplay);
            NumOfDecimalsToExport = Storage.GetInt("NumOfDecimalsToExport", NumOfDecimalsToExport);
            MinimumIonSpanForFitting = Storage.GetDouble("MinimumIonSpanForFitting", MinimumIonSpanForFitting);
            FinalFigureShowParameterBoxAsDefault = Storage.GetBool("FinalFigureShowParameterBoxAsDefault", FinalFigureShowParameterBoxAsDefault);
            FinalFigureShowDetailsAsDefault = Storage.GetBool("FinalFigureShowDetailsAsDefault", FinalFigureShowDetailsAsDefault);
            FinalFigureShowModelInfoAsDefault = Storage.GetBool("FinalFigureShowModelInfoAsDefault", FinalFigureShowModelInfoAsDefault);
            PeakFitAlgorithm = (PeakFitAlgorithm)Storage.GetInt("PeakFitAlgorithm", (int)PeakFitAlgorithm);
            NumberPrecision = (NumberPrecision)Storage.GetInt("NumberPrecision", (int)NumberPrecision);
            UncertaintyDisplayStyle = (UncertaintyDisplayStyle)Storage.GetInt("UncertaintyDisplayStyle", (int)UncertaintyDisplayStyle);
            IncludeBufferInIonicStrengthCalc = Storage.GetBool("IncludeBufferInIonicStrengthCalc", IncludeBufferInIonicStrengthCalc);
            DisplayAttributeOptions = (DisplayAttributeOptions)Storage.GetInt("DisplayAttributeOptions", (int)DisplayAttributeOptions);
            ExportColumns = (ExportColumns)Storage.GetInt("ExportColumns", (int)ExportColumns.Default);
            ShowResidualGraph = Storage.GetBool("ShowResidualGraph", ShowResidualGraph);
            ShowResidualGraphGap = Storage.GetBool("ShowResidualGraphGap", ShowResidualGraphGap);
            UnifyResidualGraphAxis = Storage.GetBool("UnifyResidualGraphAxis", UnifyResidualGraphAxis);
            FitLineSmoothness = (LineSmoothness)Storage.GetInt("FitLineSmoothness", (int)FitLineSmoothness);
            DiscardIntegrationRegionForBaseline = Storage.GetBool("DiscardIntegrationRegionForBaseline", DiscardIntegrationRegionForBaseline);
            DefaultSolverAlgorithm = (SolverAlgorithm)Storage.GetInt("SolverAlgorithm", (int)DefaultSolverAlgorithm);
            UseInjectionErrorWeightedFitting = Storage.GetBool("UseInjectionErrorWeightedFitting", UseInjectionErrorWeightedFitting);
            AutoAxesIgnoresBadData = Storage.GetBool("AutoAxesIgnoresBadData", AutoAxesIgnoresBadData);
            DilutionCalculationMethod = (DilutionMethod)Storage.GetInt("DilutionCalculationMethod", (int)DilutionCalculationMethod);
            BufferSubtractionDefaultMethod = NormalizeBufferSubtractionMethod(Storage.GetInt("BufferSubtractionDefaultMethod", (int)BufferSubtractionDefaultMethod));
            ReprocessIntegratedHeatDataOnLoad = Storage.GetBool("ReprocessIntegratedHeatDataOnLoad", ReprocessIntegratedHeatDataOnLoad);
            DefaultSplinePointDensity = (SplineInterpolator.SplinePointDensity)Storage.GetInt("DefaultSplinePointDensity", (int)DefaultSplinePointDensity);
            DefaultSplineHandleMode = (SplineInterpolator.SplineHandleMode)Storage.GetInt("DefaultSplineHandleMode", (int)DefaultSplineHandleMode);
            DefaultSplinePointTimeDragging = Storage.GetBool("DefaultSplinePointTimeDragging", DefaultSplinePointTimeDragging);
            IntegrationRegionCopyIncludesStart = Storage.GetBool("IntegrationRegionCopyIncludesStart", IntegrationRegionCopyIncludesStart);
            PerformOnlineChecksOnLaunch = Storage.GetBool("PerformOnlineChecksOnLaunch", PerformOnlineChecksOnLaunch);
            ConfirmRemoveDelete = Storage.GetBool("ConfirmRemoveDelete", ConfirmRemoveDelete);

            lastDocumentPaths = NormalizeDocumentPaths(Storage.GetStringArray("LastDocumentUrls"));

            ApplySettings();

            StatusBarManager.ClearAppStatus();
        }

        public static void Reset()
        {
            ReferenceTemperature = 25;
            EnergyUnit = EnergyUnit.KiloJoule;
            DefaultErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
            DefaultBootstrapIterations = 100;
            MinimumTemperatureSpanForFitting = 3;
            IncludeConcentrationErrorsInBootstrap = false;
            OptimizerTolerance = double.Epsilon;
            MaximumOptimizerIterations = 300000;
            ColorScheme = ColorSchemes.Default;
            ColorSchemeGradientMode = ColorSchemeGradientMode.Smooth;
            ConcentrationAutoVariance = 0.1;
            UnifyTimeAxisForExport = true;
            ExportFitPointsWithPeaks = true;
            ExportSelectionMode = ExportDataSelection.IncludedData; ;
            EnableExtendedParameterLimits = false;
            ParameterLimitSetting = ParameterLimitSetting.Standard;
            FinalFigureParameterDisplay = FinalFigureDisplayParameters.Default;
            FinalFigureDimensions = new double[] { 6.5, 10 };
            ExportBaselineCorrectedData = true;
            DefaultConcentrationUnit = ConcentrationUnit.µM;
            DefaultDesignerInstrument = ITCInstrument.MicroCalITC200;
            InputAffinityAsDissociationConstant = true;
            lastDocumentPath = null;
            lastDocumentPaths = null;
            IncludeBufferInIonicStrengthCalc = true;
            BuffersPreparedAtRoomTemperature = true;
            CreateSingleAnalysisResult = false;
            CreateGlobalAnalysisResult = true;
            AutoOpenNewAnalysisResult = true;
            AnalysisParameterDisplay = FinalFigureDisplayParameters.Model | FinalFigureDisplayParameters.Fitted | FinalFigureDisplayParameters.Derived;
            NumOfDecimalsToExport = 1;
            MinimumIonSpanForFitting = 0.03;
            FinalFigureShowParameterBoxAsDefault = true;
            FinalFigureShowDetailsAsDefault = true;
            FinalFigureShowModelInfoAsDefault = true;
            PeakFitAlgorithm = PeakFitAlgorithm.SingleExponential;
            NumberPrecision = NumberPrecision.Standard;
            UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviation;
            DisplayAttributeOptions = DisplayAttributeOptions.Default;
            ExportColumns = ExportColumns.Default;
            UseInjectionErrorWeightedFitting = false;
            DefaultSolverAlgorithm = SolverAlgorithm.NelderMead;
            DiscardIntegrationRegionForBaseline = true;
            FitLineSmoothness = LineSmoothness.Spline;
            ShowResidualGraph = true;
            ShowResidualGraphGap = true;
            UnifyResidualGraphAxis = false;
            AutoAxesIgnoresBadData = true;
            DilutionCalculationMethod = DilutionMethod.MicroCal;
            BufferSubtractionDefaultMethod = BufferSubtractionMethod.MatchedInjection;
            ReprocessIntegratedHeatDataOnLoad = true;
            DefaultSplinePointDensity = SplineInterpolator.SplinePointDensity.Balanced;
            DefaultSplineHandleMode = SplineInterpolator.SplineHandleMode.Mean;
            DefaultSplinePointTimeDragging = false;
            IntegrationRegionCopyIncludesStart = false;
            PerformOnlineChecksOnLaunch = true;
            ConfirmRemoveDelete = true;
        }

        static string NormalizeDocumentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile)
                return uri.LocalPath;

            return path;
        }

        static string[] NormalizeDocumentPaths(string[] paths)
        {
            if (paths == null) return null;

            return paths
                .Select(NormalizeDocumentPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
        }

        static BufferSubtractionMethod NormalizeBufferSubtractionMethod(int method)
        {
            if (Enum.IsDefined(typeof(BufferSubtractionMethod), method))
                return (BufferSubtractionMethod)method;

            return BufferSubtractionMethod.MatchedInjection;
        }

        static ITCInstrument NormalizeDesignerInstrument(int instrument)
        {
            var normalized = (ITCInstrument)instrument;

            if (ITCInstrumentAttribute.GetITCInstruments().Contains(normalized))
                return normalized;

            return ITCInstrument.MicroCalITC200;
        }

        public static void ApplySettings()
        {
            FittingOptionsController.BootstrapIterations = DefaultBootstrapIterations;
            FittingOptionsController.ErrorEstimationMethod = DefaultErrorEstimationMethod;
            FittingOptionsController.IncludeConcentrationVariance = IncludeConcentrationErrorsInBootstrap;
            FittingOptionsController.AutoConcentrationVariance = ConcentrationAutoVariance;
            FittingOptionsController.EnableAutoConcentrationVariance = IsConcentrationAutoVarianceEnabled;
            FittingOptionsController.Algorithm = DefaultSolverAlgorithm;
            FittingOptionsController.UseErrorWeightedFitting = UseInjectionErrorWeightedFitting;
            SettingsApplied?.Invoke(null, null);
        }
    }

    public enum PeakFitAlgorithm
    {
        SingleExponential,
        DoubleExponential,
    }

    public enum NumberPrecision
    {
        Strict,
        Standard,
        SingleDecimal,
        AllDecimals,
    }

    public enum UncertaintyDisplayStyle
    {
        Automatic,
        StandardDeviation,
        ConfidenceInterval,
        StandardDeviationAndConfidenceInterval,
        None,
    }

    public enum ParameterLimitSetting
    {
        Standard,
        Extended,
        NoLimit,
    }
}
