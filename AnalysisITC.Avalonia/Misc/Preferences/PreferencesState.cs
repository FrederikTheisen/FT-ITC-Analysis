using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Avalonia.Preferences;

internal sealed class PreferencesState
{
    public double ReferenceTemperature { get; set; }
    public EnergyUnit EnergyUnit { get; set; }
    public ConcentrationUnit DefaultConcentrationUnit { get; set; }
    public double MinimumTemperatureSpanForFitting { get; set; }
    public double MinimumIonSpanForFitting { get; set; }
    public NumberPrecision NumberPrecision { get; set; }
    public UncertaintyDisplayStyle UncertaintyDisplayStyle { get; set; }
    public bool IncludeBufferInIonicStrengthCalc { get; set; }
    public bool ConfirmRemoveDelete { get; set; }

    public DilutionMethod DilutionCalculationMethod { get; set; }
    public PeakFitAlgorithm PeakFitAlgorithm { get; set; }
    public BufferSubtractionMethod BufferSubtractionDefaultMethod { get; set; }
    public bool DiscardIntegrationRegionForBaseline { get; set; }
    public bool ReprocessIntegratedHeatDataOnLoad { get; set; }
    public SplineInterpolator.SplinePointDensity DefaultSplinePointDensity { get; set; }
    public SplineInterpolator.SplineHandleMode DefaultSplineHandleMode { get; set; }
    public bool DefaultSplinePointTimeDragging { get; set; }
    public bool IntegrationRegionCopyIncludesStart { get; set; }

    public SolverAlgorithm DefaultSolverAlgorithm { get; set; }
    public ErrorEstimationMethod DefaultErrorEstimationMethod { get; set; }
    public int DefaultBootstrapIterations { get; set; }
    public bool IncludeConcentrationErrorsInBootstrap { get; set; }
    public double ConcentrationAutoVariance { get; set; }
    public double OptimizerTolerance { get; set; }
    public int MaximumOptimizerIterations { get; set; }
    public ParameterLimitSetting ParameterLimitSetting { get; set; }
    public bool UseInjectionErrorWeightedFitting { get; set; }
    public bool CreateSingleAnalysisResult { get; set; }
    public bool CreateGlobalAnalysisResult { get; set; }
    public bool AutoOpenNewAnalysisResult { get; set; }

    public ExportDataSelection ExportSelectionMode { get; set; }
    public ExportColumns ExportColumns { get; set; }
    public int NumOfDecimalsToExport { get; set; }
    public bool UnifyTimeAxisForExport { get; set; }
    public bool ExportBaselineCorrectedData { get; set; }
    public bool ExportFitPointsWithPeaks { get; set; }
    public double FinalFigureWidthCentimeters { get; set; }
    public double FinalFigureHeightCentimeters { get; set; }
    public bool ShowResidualGraph { get; set; }
    public bool ShowResidualGraphGap { get; set; }
    public bool UnifyResidualGraphAxis { get; set; }
    public LineSmoothness FitLineSmoothness { get; set; }
    public bool FinalFigureShowParameterBoxAsDefault { get; set; }
    public bool FinalFigureShowDetailsAsDefault { get; set; }
    public bool FinalFigureShowModelInfoAsDefault { get; set; }
    public FinalFigureDisplayParameters FinalFigureParameterDisplay { get; set; }
    public DisplayAttributeOptions DisplayAttributeOptions { get; set; }
    public bool AutoAxesIgnoresBadData { get; set; }

    public static PreferencesState FromSettings()
    {
        return new PreferencesState
        {
            ReferenceTemperature = AppSettings.ReferenceTemperature,
            EnergyUnit = AppSettings.EnergyUnit,
            DefaultConcentrationUnit = AppSettings.DefaultConcentrationUnit,
            MinimumTemperatureSpanForFitting = AppSettings.MinimumTemperatureSpanForFitting,
            MinimumIonSpanForFitting = AppSettings.MinimumIonSpanForFitting,
            NumberPrecision = AppSettings.NumberPrecision,
            UncertaintyDisplayStyle = AppSettings.UncertaintyDisplayStyle,
            IncludeBufferInIonicStrengthCalc = AppSettings.IncludeBufferInIonicStrengthCalc,
            ConfirmRemoveDelete = AppSettings.ConfirmRemoveDelete,

            DilutionCalculationMethod = AppSettings.DilutionCalculationMethod,
            PeakFitAlgorithm = AppSettings.PeakFitAlgorithm,
            BufferSubtractionDefaultMethod = AppSettings.BufferSubtractionDefaultMethod,
            DiscardIntegrationRegionForBaseline = AppSettings.DiscardIntegrationRegionForBaseline,
            ReprocessIntegratedHeatDataOnLoad = AppSettings.ReprocessIntegratedHeatDataOnLoad,
            DefaultSplinePointDensity = AppSettings.DefaultSplinePointDensity,
            DefaultSplineHandleMode = AppSettings.DefaultSplineHandleMode,
            DefaultSplinePointTimeDragging = AppSettings.DefaultSplinePointTimeDragging,
            IntegrationRegionCopyIncludesStart = AppSettings.IntegrationRegionCopyIncludesStart,

            DefaultSolverAlgorithm = AppSettings.DefaultSolverAlgorithm,
            DefaultErrorEstimationMethod = AppSettings.DefaultErrorEstimationMethod,
            DefaultBootstrapIterations = AppSettings.DefaultBootstrapIterations,
            IncludeConcentrationErrorsInBootstrap = AppSettings.IncludeConcentrationErrorsInBootstrap,
            ConcentrationAutoVariance = AppSettings.ConcentrationAutoVariance,
            OptimizerTolerance = AppSettings.OptimizerTolerance,
            MaximumOptimizerIterations = AppSettings.MaximumOptimizerIterations,
            ParameterLimitSetting = AppSettings.ParameterLimitSetting,
            UseInjectionErrorWeightedFitting = AppSettings.UseInjectionErrorWeightedFitting,
            CreateSingleAnalysisResult = AppSettings.CreateSingleAnalysisResult,
            CreateGlobalAnalysisResult = AppSettings.CreateGlobalAnalysisResult,
            AutoOpenNewAnalysisResult = AppSettings.AutoOpenNewAnalysisResult,

            ExportSelectionMode = AppSettings.ExportSelectionMode,
            ExportColumns = AppSettings.ExportColumns,
            NumOfDecimalsToExport = AppSettings.NumOfDecimalsToExport,
            UnifyTimeAxisForExport = AppSettings.UnifyTimeAxisForExport,
            ExportBaselineCorrectedData = AppSettings.ExportBaselineCorrectedData,
            ExportFitPointsWithPeaks = AppSettings.ExportFitPointsWithPeaks,
            FinalFigureWidthCentimeters = AppSettings.FinalFigureDimensions.Length > 0 ? AppSettings.FinalFigureDimensions[0] : 6.5,
            FinalFigureHeightCentimeters = AppSettings.FinalFigureDimensions.Length > 1 ? AppSettings.FinalFigureDimensions[1] : 10.0,
            ShowResidualGraph = AppSettings.ShowResidualGraph,
            ShowResidualGraphGap = AppSettings.ShowResidualGraphGap,
            UnifyResidualGraphAxis = AppSettings.UnifyResidualGraphAxis,
            FitLineSmoothness = AppSettings.FitLineSmoothness,
            FinalFigureShowParameterBoxAsDefault = AppSettings.FinalFigureShowParameterBoxAsDefault,
            FinalFigureShowDetailsAsDefault = AppSettings.FinalFigureShowDetailsAsDefault,
            FinalFigureShowModelInfoAsDefault = AppSettings.FinalFigureShowModelInfoAsDefault,
            FinalFigureParameterDisplay = AppSettings.FinalFigureParameterDisplay,
            DisplayAttributeOptions = AppSettings.DisplayAttributeOptions,
            AutoAxesIgnoresBadData = AppSettings.AutoAxesIgnoresBadData
        };
    }

    public static PreferencesState Defaults()
    {
        return new PreferencesState
        {
            ReferenceTemperature = 25,
            EnergyUnit = EnergyUnit.KiloJoule,
            DefaultConcentrationUnit = ConcentrationUnit.µM,
            MinimumTemperatureSpanForFitting = 3,
            MinimumIonSpanForFitting = 0.03,
            NumberPrecision = NumberPrecision.Standard,
            UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviation,
            IncludeBufferInIonicStrengthCalc = true,
            ConfirmRemoveDelete = true,

            DilutionCalculationMethod = DilutionMethod.MicroCal,
            PeakFitAlgorithm = PeakFitAlgorithm.SingleExponential,
            BufferSubtractionDefaultMethod = BufferSubtractionMethod.MatchedInjection,
            DiscardIntegrationRegionForBaseline = true,
            ReprocessIntegratedHeatDataOnLoad = true,
            DefaultSplinePointDensity = SplineInterpolator.SplinePointDensity.Balanced,
            DefaultSplineHandleMode = SplineInterpolator.SplineHandleMode.Mean,
            DefaultSplinePointTimeDragging = false,
            IntegrationRegionCopyIncludesStart = false,

            DefaultSolverAlgorithm = SolverAlgorithm.NelderMead,
            DefaultErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
            DefaultBootstrapIterations = 100,
            IncludeConcentrationErrorsInBootstrap = false,
            ConcentrationAutoVariance = 0.1,
            OptimizerTolerance = double.Epsilon,
            MaximumOptimizerIterations = 300000,
            ParameterLimitSetting = AnalysisITC.Core.Application.ParameterLimitSetting.Standard,
            UseInjectionErrorWeightedFitting = false,
            CreateSingleAnalysisResult = false,
            CreateGlobalAnalysisResult = true,
            AutoOpenNewAnalysisResult = true,

            ExportSelectionMode = ExportDataSelection.IncludedData,
            ExportColumns = ExportColumns.Default,
            NumOfDecimalsToExport = 1,
            UnifyTimeAxisForExport = true,
            ExportBaselineCorrectedData = true,
            ExportFitPointsWithPeaks = true,
            FinalFigureWidthCentimeters = 6.5,
            FinalFigureHeightCentimeters = 10.0,
            ShowResidualGraph = true,
            ShowResidualGraphGap = true,
            UnifyResidualGraphAxis = false,
            FitLineSmoothness = LineSmoothness.Spline,
            FinalFigureShowParameterBoxAsDefault = true,
            FinalFigureShowDetailsAsDefault = true,
            FinalFigureShowModelInfoAsDefault = true,
            FinalFigureParameterDisplay = FinalFigureDisplayParameters.Default,
            DisplayAttributeOptions = DisplayAttributeOptions.Default,
            AutoAxesIgnoresBadData = true
        };
    }

    public void Apply()
    {
        AppSettings.ReferenceTemperature = ReferenceTemperature;
        AppSettings.EnergyUnit = EnergyUnit;
        AppSettings.DefaultConcentrationUnit = DefaultConcentrationUnit;
        AppSettings.MinimumTemperatureSpanForFitting = MinimumTemperatureSpanForFitting;
        AppSettings.MinimumIonSpanForFitting = MinimumIonSpanForFitting;
        AppSettings.NumberPrecision = NumberPrecision;
        AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle;
        AppSettings.IncludeBufferInIonicStrengthCalc = IncludeBufferInIonicStrengthCalc;
        AppSettings.ConfirmRemoveDelete = ConfirmRemoveDelete;

        AppSettings.DilutionCalculationMethod = DilutionCalculationMethod;
        AppSettings.PeakFitAlgorithm = PeakFitAlgorithm;
        AppSettings.BufferSubtractionDefaultMethod = BufferSubtractionDefaultMethod;
        AppSettings.DiscardIntegrationRegionForBaseline = DiscardIntegrationRegionForBaseline;
        AppSettings.ReprocessIntegratedHeatDataOnLoad = ReprocessIntegratedHeatDataOnLoad;
        AppSettings.DefaultSplinePointDensity = DefaultSplinePointDensity;
        AppSettings.DefaultSplineHandleMode = DefaultSplineHandleMode;
        AppSettings.DefaultSplinePointTimeDragging = DefaultSplinePointTimeDragging;
        AppSettings.IntegrationRegionCopyIncludesStart = IntegrationRegionCopyIncludesStart;

        AppSettings.DefaultSolverAlgorithm = DefaultSolverAlgorithm;
        AppSettings.DefaultErrorEstimationMethod = DefaultErrorEstimationMethod;
        AppSettings.DefaultBootstrapIterations = DefaultBootstrapIterations;
        AppSettings.IncludeConcentrationErrorsInBootstrap = IncludeConcentrationErrorsInBootstrap;
        AppSettings.ConcentrationAutoVariance = ConcentrationAutoVariance;
        AppSettings.IsConcentrationAutoVarianceEnabled = ConcentrationAutoVariance > double.Epsilon;
        AppSettings.OptimizerTolerance = OptimizerTolerance;
        AppSettings.MaximumOptimizerIterations = MaximumOptimizerIterations;
        AppSettings.ParameterLimitSetting = ParameterLimitSetting;
        AppSettings.EnableExtendedParameterLimits = ParameterLimitSetting != AnalysisITC.Core.Application.ParameterLimitSetting.Standard;
        AppSettings.UseInjectionErrorWeightedFitting = UseInjectionErrorWeightedFitting;
        AppSettings.CreateSingleAnalysisResult = CreateSingleAnalysisResult;
        AppSettings.CreateGlobalAnalysisResult = CreateGlobalAnalysisResult;
        AppSettings.AutoOpenNewAnalysisResult = AutoOpenNewAnalysisResult;

        AppSettings.ExportSelectionMode = ExportSelectionMode;
        AppSettings.ExportColumns = ExportColumns;
        AppSettings.NumOfDecimalsToExport = NumOfDecimalsToExport;
        AppSettings.UnifyTimeAxisForExport = UnifyTimeAxisForExport;
        AppSettings.ExportBaselineCorrectedData = ExportBaselineCorrectedData;
        AppSettings.ExportFitPointsWithPeaks = ExportFitPointsWithPeaks;
        AppSettings.FinalFigureDimensions = new[] { FinalFigureWidthCentimeters, FinalFigureHeightCentimeters };
        AppSettings.ShowResidualGraph = ShowResidualGraph;
        AppSettings.ShowResidualGraphGap = ShowResidualGraphGap;
        AppSettings.UnifyResidualGraphAxis = UnifyResidualGraphAxis;
        AppSettings.FitLineSmoothness = FitLineSmoothness;
        AppSettings.FinalFigureShowParameterBoxAsDefault = FinalFigureShowParameterBoxAsDefault;
        AppSettings.FinalFigureShowDetailsAsDefault = FinalFigureShowDetailsAsDefault;
        AppSettings.FinalFigureShowModelInfoAsDefault = FinalFigureShowModelInfoAsDefault;
        AppSettings.FinalFigureParameterDisplay = FinalFigureParameterDisplay;
        AppSettings.DisplayAttributeOptions = DisplayAttributeOptions;
        AppSettings.AutoAxesIgnoresBadData = AutoAxesIgnoresBadData;

        AppSettings.Save();
    }
}
