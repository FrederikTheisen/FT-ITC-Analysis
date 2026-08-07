using System;
using System.Collections.Generic;

namespace AnalysisITC.Core.Viewer
{
    public enum ViewerFileFormat
    {
        Ftxtc,
        Ftitc,
        Itc,
    }

    public sealed class ViewerDocument
    {
        public string DisplayName { get; internal set; }
        public string Format { get; internal set; }
        public long SizeBytes { get; internal set; }
        public string FormatVersion { get; internal set; }
        public List<string> Warnings { get; } = new List<string>();
        public List<ViewerExperiment> Experiments { get; } = new List<ViewerExperiment>();
        public List<ViewerAnalysisResult> AnalysisResults { get; } = new List<ViewerAnalysisResult>();
    }

    public sealed class ViewerAnalysisResult
    {
        public string Key { get; internal set; }
        public string Name { get; internal set; }
        public DateTime? Date { get; internal set; }
        public string Comments { get; internal set; }
        public string ModelName { get; internal set; }
        public bool IsGlobal { get; internal set; }
        public int ExperimentCount { get; internal set; }
        public double? Loss { get; internal set; }
        public ViewerSolverDto Solver { get; internal set; }
        public ViewerValidityDto Validity { get; internal set; }
        public ViewerTemperatureParameterEvaluationDto TemperatureParameterEvaluation { get; internal set; }
        public List<ViewerAnalysisResultMemberDto> Members { get; } = new List<ViewerAnalysisResultMemberDto>();
        public List<ViewerSettingDto> ModelOptions { get; } = new List<ViewerSettingDto>();
        public List<ViewerSettingDto> Constraints { get; } = new List<ViewerSettingDto>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public sealed class ViewerAnalysisResultMemberDto
    {
        public string ExperimentKey { get; internal set; }
        public string FitKey { get; internal set; }
        public string ExperimentName { get; internal set; }
        public double? TemperatureCelsius { get; internal set; }
        public double? Loss { get; internal set; }
        public bool SolutionValid { get; internal set; }
        public string AvailabilityMessage { get; internal set; }
    }

    public sealed class ViewerSolverDto
    {
        public string Algorithm { get; internal set; }
        public string Termination { get; internal set; }
        public string Convergence { get; internal set; }
        public int? Iterations { get; internal set; }
        public bool WeightedFitting { get; internal set; }
        public string ErrorEstimationMethod { get; internal set; }
        public string ErrorEstimationSummary { get; internal set; }
        public int BootstrapIterations { get; internal set; }
        public double? ElapsedSeconds { get; internal set; }
    }

    public sealed class ViewerValidityDto
    {
        public string Status { get; internal set; }
        public List<string> Reasons { get; } = new List<string>();
    }

    public sealed class ViewerSettingDto
    {
        public string Key { get; internal set; }
        public string Label { get; internal set; }
        public string Value { get; internal set; }
    }

    public sealed class ViewerTemperatureParameterEvaluationDto
    {
        public double DefaultTemperatureCelsius { get; internal set; }
        public double? MinimumTemperatureCelsius { get; internal set; }
        public double? MaximumTemperatureCelsius { get; internal set; }
        public bool IsTemperatureDependent { get; internal set; }
        public List<ViewerTemperatureDependenceDto> Dependences { get; } = new List<ViewerTemperatureDependenceDto>();
    }

    public sealed class ViewerTemperatureDependenceDto
    {
        public string Key { get; internal set; }
        public string Label { get; internal set; }
        public string Unit { get; internal set; }
        public string SlopeUnit { get; internal set; }
        public double ReferenceTemperatureCelsius { get; internal set; }
        public ViewerValueWithErrorDto Intercept { get; internal set; }
        public ViewerValueWithErrorDto Slope { get; internal set; }
    }

    public sealed class ViewerValueWithErrorDto
    {
        public double Value { get; internal set; }
        public double Sd { get; internal set; }
        public double? ConfidenceLower { get; internal set; }
        public double? ConfidenceUpper { get; internal set; }
    }

    public sealed class ViewerExperiment
    {
        public string Key { get; internal set; }
        public string Name { get; internal set; }
        public string SourceFileName { get; internal set; }
        public string SourceFormat { get; internal set; }
        public DateTime? Date { get; internal set; }
        public string Instrument { get; internal set; }
        public string Comments { get; internal set; }
        public double? TargetTemperatureCelsius { get; internal set; }
        public double? MeasuredTemperatureCelsius { get; internal set; }
        public double? SyringeConcentrationMicromolar { get; internal set; }
        public double? CellConcentrationMicromolar { get; internal set; }
        public double? CellVolumeMicroliters { get; internal set; }
        public double? StirringSpeedRpm { get; internal set; }
        public int InjectionCount { get; internal set; }
        public List<string> AvailableViews { get; } = new List<string>();
        public List<ViewerMetadataItem> Metadata { get; } = new List<ViewerMetadataItem>();
        public RawTraceDto Raw { get; internal set; }
        public IntegratedTraceDto Integrated { get; internal set; }
        public ProcessedTraceDto Processed { get; internal set; }
        public List<FitDto> Fits { get; } = new List<FitDto>();
        public string DefaultFitKey { get; internal set; }
    }

    public sealed class ViewerMetadataItem
    {
        public string Label { get; internal set; }
        public string Value { get; internal set; }
    }

    public sealed class RawTraceDto
    {
        public double[] TimeSeconds { get; internal set; }
        public double[] PowerMicrowatts { get; internal set; }
        public double[] TemperatureCelsius { get; internal set; }
        public int[] InjectionNumbers { get; internal set; }
        public double[] InjectionTimesSeconds { get; internal set; }
        public List<string> UnavailableChannels { get; } = new List<string>();
    }

    public sealed class IntegratedTraceDto
    {
        public int[] InjectionNumbers { get; internal set; }
        public bool[] Included { get; internal set; }
        public bool[] IsIntegrated { get; internal set; }
        public double?[] RawHeatMicrojoules { get; internal set; }
        public double?[] CorrectedHeatMicrojoules { get; internal set; }
        public double?[] HeatSdMicrojoules { get; internal set; }
        public double?[] MolarHeatKilojoulesPerMole { get; internal set; }
        public double?[] MolarHeatSdKilojoulesPerMole { get; internal set; }
        public double[] InjectionVolumeMicroliters { get; internal set; }
        public double[] InjectionTimeSeconds { get; internal set; }
        public double[] InjectionTemperatureCelsius { get; internal set; }
        public double[] CellConcentrationMicromolar { get; internal set; }
        public double[] TitrantConcentrationMicromolar { get; internal set; }
        public double[] AnalysisX { get; internal set; }
        public string AnalysisXAxisName { get; internal set; }
        public string AnalysisXAxisUnit { get; internal set; }
    }

    public sealed class ProcessedTraceDto
    {
        public double[] TimeSeconds { get; internal set; }
        public double[] RawPowerMicrowatts { get; internal set; }
        public double[] BaselinePowerMicrowatts { get; internal set; }
        public double[] CorrectedPowerMicrowatts { get; internal set; }
        public string BaselineMethod { get; internal set; }
        public double[] ControlPointTimesSeconds { get; internal set; }
        public double[] ControlPointPowerMicrowatts { get; internal set; }
        public double[] IntegrationStartSeconds { get; internal set; }
        public double[] IntegrationEndSeconds { get; internal set; }
    }

    public sealed class FitDto
    {
        public string Key { get; internal set; }
        public string ResultKey { get; internal set; }
        public string ResultName { get; internal set; }
        public string ModelName { get; internal set; }
        public bool IsGlobal { get; internal set; }
        public string AnalysisXAxisName { get; internal set; }
        public string AnalysisXAxisUnit { get; internal set; }
        public double[] X { get; internal set; }
        public double?[] ObservedKilojoulesPerMole { get; internal set; }
        public double?[] ObservationSdKilojoulesPerMole { get; internal set; }
        public double?[] FittedKilojoulesPerMole { get; internal set; }
        public double?[] ResidualKilojoulesPerMole { get; internal set; }
        public double?[] ConfidenceLowerKilojoulesPerMole { get; internal set; }
        public double?[] ConfidenceUpperKilojoulesPerMole { get; internal set; }
        public bool[] Included { get; internal set; }
        public double? Loss { get; internal set; }
        public string Convergence { get; internal set; }
        public List<FitParameterDto> Parameters { get; } = new List<FitParameterDto>();
    }

    public sealed class FitParameterDto
    {
        public string Key { get; internal set; }
        public string Label { get; internal set; }
        public bool IsDerived { get; internal set; }
        public bool IsLocked { get; internal set; }
        public bool IsGloballyDetermined { get; internal set; }
        public double Value { get; internal set; }
        public double Sd { get; internal set; }
        public double? ConfidenceLower { get; internal set; }
        public double? ConfidenceUpper { get; internal set; }
        public string Unit { get; internal set; }
    }

    public sealed class ViewerFileException : Exception
    {
        public ViewerFileException(string code, string message, Exception innerException = null)
            : base(message, innerException)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
