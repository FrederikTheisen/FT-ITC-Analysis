using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Export
{
    /// <summary>
    /// Version 1.x of the FT-ITC project container. The package is a checksummed ZIP
    /// whose numeric traces are stored in typed, little-endian FTXB payloads.
    /// </summary>
    internal static class FTXTCFormat
    {
        internal const string Extension = ".ftxtc";
        internal const string FormatName = "ftxtc";
        internal const int SchemaMajor = 1;
        internal const int SchemaMinor = 6;
        internal const int ProjectSchemaVersion = 4;
        internal const string ManifestPath = "manifest.json";
        internal const string ProjectPath = "project.json";
        internal const int MaxEntries = 10000;
        internal const long MaxEntryBytes = 512L * 1024 * 1024;
        internal const long MaxPackageBytes = 2L * 1024 * 1024 * 1024;

        internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

        static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = false,
                WriteIndented = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            return options;
        }

        internal static byte[] JsonBytes<T>(T value) =>
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonOptions));

        internal static T ReadJson<T>(byte[] bytes, string path)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                    ?? throw new InvalidDataException($"FTXTC entry '{path}' is empty.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"FTXTC entry '{path}' is not valid JSON.", ex);
            }
        }

        internal static string Sha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
        }

        internal static string NormalizeEntryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("FTXTC contains an empty entry path.");
            var normalized = path.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal)
                || normalized.Split('/').Any(part => part.Length == 0 || part == "." || part == "..")
                || normalized.IndexOf(':') >= 0)
                throw new InvalidDataException($"FTXTC contains an unsafe entry path: '{path}'.");
            return normalized;
        }
    }

    internal sealed class FtxtcManifest
    {
        public string Format { get; set; } = FTXTCFormat.FormatName;
        public int SchemaMajor { get; set; } = FTXTCFormat.SchemaMajor;
        public int SchemaMinor { get; set; } = FTXTCFormat.SchemaMinor;
        public string WriterVersion { get; set; }
        public string Root { get; set; } = FTXTCFormat.ProjectPath;
        public List<FtxtcManifestEntry> Entries { get; set; } = new List<FtxtcManifestEntry>();
    }

    internal sealed class FtxtcManifestEntry
    {
        public string Path { get; set; }
        public string MediaType { get; set; }
        public long Length { get; set; }
        public string Sha256 { get; set; }
    }

    internal sealed class FtxtcProject
    {
        public int ProjectSchemaVersion { get; set; } = FTXTCFormat.ProjectSchemaVersion;
        public List<FtxtcExperimentReference> Experiments { get; set; } = new List<FtxtcExperimentReference>();
        public List<FtxtcSolutionReference> Solutions { get; set; } = new List<FtxtcSolutionReference>();
        public List<FtxtcResultReference> Results { get; set; } = new List<FtxtcResultReference>();
        public List<FtxtcReportReference> Reports { get; set; } = new List<FtxtcReportReference>();
        public List<FtxtcContentReference> ContentOrder { get; set; } = new List<FtxtcContentReference>();
    }

    internal sealed class FtxtcContentReference
    {
        public string Type { get; set; }
        public string Id { get; set; }
    }

    /// <summary>
    /// Future schema steps migrate parsed storage DTOs here, before any domain
    /// object is constructed. Writers always emit only the current DTO schema.
    /// </summary>
    internal static class FtxtcStorageMigrationPipeline
    {
        internal static FtxtcProject MigrateToCurrent(FtxtcProject project, int packageSchemaMinor)
        {
            if (project == null) throw new InvalidDataException("FTXTC root project is missing.");
            var expectedProjectSchema = packageSchemaMinor switch
            {
                0 => 1,
                <= 4 => 2,
                5 => 3,
                _ => FTXTCFormat.ProjectSchemaVersion,
            };
            if (project.ProjectSchemaVersion != expectedProjectSchema)
                throw new NotSupportedException($"No FTXTC storage migration is available for project schema {project.ProjectSchemaVersion}.");

            if (packageSchemaMinor <= 4)
            {
                project.ContentOrder = project.Experiments
                    .Select(reference => new FtxtcContentReference { Type = "experiment", Id = reference.Id })
                    .Concat(project.Results.Select(reference => new FtxtcContentReference { Type = "result", Id = reference.Id }))
                    .ToList();
            }

            if (packageSchemaMinor <= 5) project.Reports = new List<FtxtcReportReference>();
            project.Reports = project.Reports ?? new List<FtxtcReportReference>();
            project.ProjectSchemaVersion = FTXTCFormat.ProjectSchemaVersion;

            return project;
        }
    }

    internal sealed class FtxtcExperimentReference
    {
        public string Id { get; set; }
        public string Metadata { get; set; }
        public string Thermogram { get; set; }
        public string Baseline { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string CorrectedTrace { get; set; }
    }

    internal sealed class FtxtcSolutionReference
    {
        public string Id { get; set; }
        public string ExperimentId { get; set; }
        public string Metadata { get; set; }
        public string Bootstrap { get; set; }
    }

    internal sealed class FtxtcResultReference
    {
        public string Id { get; set; }
        public string Metadata { get; set; }
    }

    internal sealed class FtxtcReportReference
    {
        public string Id { get; set; }
        public string Metadata { get; set; }
    }

    internal sealed class FtxtcReportState
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; }
        public string Name { get; set; }
        public DateTime Date { get; set; }
        public string Comments { get; set; }
        public List<string> ResultIds { get; set; } = new List<string>();
        public AnalysisStudyContext StudyContext { get; set; }
        public AnalysisInterpretationOptions InterpretationSettings { get; set; }
        public AnalysisInterpretationRecord ApprovedInterpretation { get; set; }
    }

    internal sealed class FtxtcExperimentState
    {
        public string Id { get; set; }
        public string FileName { get; set; }
        public string Name { get; set; }
        public DateTime Date { get; set; }
        public string DateSource { get; set; }
        public string Comments { get; set; }
        public bool Included { get; set; }
        public string SourceFormat { get; set; }
        public string Instrument { get; set; }
        public FtxtcFloatWithError CellConcentration { get; set; }
        public FtxtcFloatWithError SyringeConcentration { get; set; }
        public double CellVolume { get; set; }
        public double StirringSpeed { get; set; }
        public string FeedbackMode { get; set; }
        public double TargetTemperature { get; set; }
        public double MeasuredTemperature { get; set; }
        public double InitialDelay { get; set; }
        public double TargetPowerDifference { get; set; }
        public string AverageHeatDirection { get; set; }
        public string AttachedSolutionId { get; set; }
        public List<FtxtcAttributeState> Attributes { get; set; } = new List<FtxtcAttributeState>();
        public List<FtxtcTandemSegmentState> Segments { get; set; } = new List<FtxtcTandemSegmentState>();
        public FtxtcProcessorState Processor { get; set; }
        public List<FtxtcInjectionState> Injections { get; set; } = new List<FtxtcInjectionState>();
    }

    internal sealed class FtxtcAttributeState
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public bool BoolValue { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? IntValue { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ValueId { get; set; }
        public double DoubleValue { get; set; }
        public string StringValue { get; set; }
        public FtxtcFloatWithError ParameterValue { get; set; }
    }

    internal sealed class FtxtcTandemSegmentState
    {
        public int FirstInjectionId { get; set; }
        public double InitialCellConcentration { get; set; }
        public double InitialTitrantConcentration { get; set; }
    }

    internal sealed class FtxtcProcessorState
    {
        public int SchemaVersion { get; set; } = 1;
        public string Type { get; set; } = "none";
        public bool Locked { get; set; }
        public bool BaselineCompleted { get; set; }
        public bool DiscardIntegratedPoints { get; set; }
        public string IntegrationLengthMode { get; set; }
        public float IntegrationLengthFactor { get; set; }
        public FtxtcSplineState Spline { get; set; }
        public FtxtcPolynomialState Polynomial { get; set; }
        public FtxtcSegmentedState Segmented { get; set; }
        public FtxtcAslState Asl { get; set; }
    }

    internal sealed class FtxtcSplineState
    {
        public string Algorithm { get; set; }
        public string Density { get; set; }
        public string HandleMode { get; set; }
        public bool ShowHandles { get; set; }
        public bool AllowPointTimeDragging { get; set; }
        public int PointsPerInjection { get; set; }
        public List<FtxtcSplinePointState> Points { get; set; } = new List<FtxtcSplinePointState>();
    }

    internal sealed class FtxtcSplinePointState
    {
        public int Id { get; set; }
        public double Time { get; set; }
        public double Power { get; set; }
        public double Slope { get; set; }
        public bool Locked { get; set; }
        public bool SlopeLocked { get; set; }
        public bool Linear { get; set; }
        public bool UserDefined { get; set; }
    }

    internal sealed class FtxtcPolynomialState { public int Degree { get; set; } public double ZLimit { get; set; } }
    internal sealed class FtxtcAslState { public int Iterations { get; set; } public double Lambda { get; set; } public double Asymmetry { get; set; } }
    internal sealed class FtxtcSegmentedState
    {
        public int Degree { get; set; }
        public List<FtxtcBaselineSegmentState> Segments { get; set; } = new List<FtxtcBaselineSegmentState>();
    }
    internal sealed class FtxtcBaselineSegmentState
    {
        public string Kind { get; set; }
        public int InjectionId { get; set; }
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public double CenterTime { get; set; }
        public double[] Coefficients { get; set; }
    }

    internal sealed class FtxtcInjectionState
    {
        public int Id { get; set; }
        public bool Included { get; set; }
        public float Time { get; set; }
        public double Volume { get; set; }
        public float Delay { get; set; }
        public float Duration { get; set; }
        public float Filter { get; set; }
        public double Temperature { get; set; }
        public float IntegrationStartDelay { get; set; }
        public float IntegrationEndOffset { get; set; }
        public double ActualCellConcentration { get; set; }
        public double ActualTitrantConcentration { get; set; }
        public double Ratio { get; set; }
        public bool IsIntegrated { get; set; }
        public string HeatDirection { get; set; }
        public FtxtcFloatWithError RawPeakArea { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FtxtcFloatWithError CorrectedPeakArea { get; set; }
    }

    internal sealed class FtxtcFloatWithError
    {
        public bool IsMissing { get; set; }
        public double Value { get; set; }
        public double StandardDeviation { get; set; }
        public double Lower95 { get; set; }
        public double Upper95 { get; set; }

        public static FtxtcFloatWithError Capture(FloatWithError value) => new FtxtcFloatWithError
        {
            IsMissing = FloatWithError.IsNaN(value),
            Value = value.Value,
            StandardDeviation = value.SD,
            Lower95 = value.Lower,
            Upper95 = value.Upper
        };

        public FloatWithError Restore() => IsMissing ? new FloatWithError(double.NaN) : new FloatWithError(Value, StandardDeviation, Lower95, Upper95);
    }

    internal sealed class FtxtcParameterState
    {
        public string Id { get; set; }
        public double Value { get; set; }
        public bool Locked { get; set; }
    }

    internal sealed class FtxtcReportedParameterState
    {
        public string Id { get; set; }
        public FtxtcFloatWithError Estimate { get; set; }
    }

    internal sealed class FtxtcCloneOptionsState
    {
        public bool IsGlobalClone { get; set; }
        public string ErrorMethod { get; set; }
        public bool IncludeConcentrationErrors { get; set; }
        public bool EnableAutoConcentrationVariance { get; set; }
        public double AutoConcentrationVariance { get; set; }
        public int DiscardedDataPoint { get; set; }
        public bool UnlockParameters { get; set; }
    }

    internal sealed class FtxtcConvergenceState
    {
        public int SchemaVersion { get; set; } = 1;
        public string Algorithm { get; set; }
        public string Termination { get; set; }
        public string ErrorOutcome { get; set; }
        public int Iterations { get; set; }
        public double Loss { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? MolarRmsdJoulesPerMole { get; set; }
        public double TimeSeconds { get; set; }
        public double ErrorEstimationTimeSeconds { get; set; }
        public string FailureReason { get; set; }
        public string ErrorEstimationSummary { get; set; }
        public int ErrorEstimationLimitTerminations { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ErrorEstimationAttemptedRefits { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ErrorEstimationSucceededRefits { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ErrorEstimationFailedRefits { get; set; }
    }

    internal sealed class FtxtcSolutionState
    {
        public int SchemaVersion { get; set; } = 1;
        public string Id { get; set; }
        public string ExperimentId { get; set; }
        public string ModelId { get; set; }
        public int ModelSchemaVersion { get; set; } = 1;
        public bool Weighted { get; set; }
        public string ErrorMethod { get; set; }
        public FtxtcCloneOptionsState CloneOptions { get; set; }
        public List<FtxtcAttributeState> ModelOptions { get; set; } = new List<FtxtcAttributeState>();
        public List<FtxtcParameterState> FittedParameters { get; set; } = new List<FtxtcParameterState>();
        public List<FtxtcReportedParameterState> ReportedParameters { get; set; } = new List<FtxtcReportedParameterState>();
        public FtxtcConvergenceState Convergence { get; set; }
        public bool ParameterBoundaryHit { get; set; }
        public bool IsValid { get; set; } = true;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FtxtcProfileRunState Profile { get; set; }
    }

    internal sealed class FtxtcBootstrapState
    {
        public int SchemaVersion { get; set; } = 1;
        public List<int> ReplicateIndices { get; set; } = new List<int>();
        public List<string> ParameterIds { get; set; } = new List<string>();
        public List<int> InjectionIds { get; set; } = new List<int>();
        public List<FtxtcBootstrapReplicateState> Replicates { get; set; } = new List<FtxtcBootstrapReplicateState>();
        public string ParameterValues { get; set; }
        public string ParameterLocks { get; set; }
        public string Injections { get; set; }
        public string InjectionIncludes { get; set; }
    }

    internal sealed class FtxtcBootstrapReplicateState
    {
        public FtxtcFloatWithError CellConcentration { get; set; }
        public FtxtcFloatWithError SyringeConcentration { get; set; }
        public double CellVolume { get; set; }
        public double MeasuredTemperature { get; set; }
        public bool ParameterBoundaryHit { get; set; }
        public List<FtxtcAttributeState> ModelOptions { get; set; } = new List<FtxtcAttributeState>();
        public List<FtxtcTandemSegmentState> Segments { get; set; } = new List<FtxtcTandemSegmentState>();
    }

    internal sealed class FtxtcResultState
    {
        public string Id { get; set; }
        public string FileName { get; set; }
        public string Name { get; set; }
        public DateTime Date { get; set; }
        public string Comments { get; set; }
        public string GlobalSolutionId { get; set; }
        public string ModelId { get; set; }
        public bool Weighted { get; set; }
        public List<string> MemberSolutionIds { get; set; } = new List<string>();
        public List<FtxtcConstraintState> Constraints { get; set; } = new List<FtxtcConstraintState>();
        public List<FtxtcParameterState> GlobalParameters { get; set; } = new List<FtxtcParameterState>();
        public FtxtcCloneOptionsState CloneOptions { get; set; }
        public FtxtcConvergenceState Convergence { get; set; }
        public bool IsValid { get; set; } = true;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FtxtcProfileRunState Profile { get; set; }
        public JsonElement? Validity { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FtxtcAdvancedAnalysesState AdvancedAnalyses { get; set; }
    }

    internal sealed class FtxtcConstraintState { public string ParameterId { get; set; } public string Constraint { get; set; } }

    internal sealed class FtxtcProfileRunState
    {
        public double ConfidenceLevel { get; set; }
        public string Calibration { get; set; }
        public int N { get; set; }
        public int P { get; set; }
        public int Q { get; set; }
        public int Df { get; set; }
        public double BaselineObjective { get; set; }
        public double TargetIncrement { get; set; }
        public string Algorithm { get; set; }
        public bool Weighted { get; set; }
        public double Tolerance { get; set; }
        public double? OptimizerToleranceSetting { get; set; }
        public int CandidateIterationLimit { get; set; }
        public int ExpansionLimit { get; set; }
        public int RefinementLimit { get; set; }
        public double ElapsedSeconds { get; set; }
        public string Outcome { get; set; }
        public int AttemptedSolverCalls { get; set; }
        public List<FtxtcProfileCoordinateState> Coordinates { get; set; } = new List<FtxtcProfileCoordinateState>();
    }
    internal sealed class FtxtcProfileCoordinateState
    {
        public string ParameterId { get; set; }
        public string Scope { get; set; }
        public string ExperimentId { get; set; }
        public int Index { get; set; }
        public double BestValue { get; set; }
        public double LowerBound { get; set; }
        public double UpperBound { get; set; }
        public FtxtcProfileSideState Lower { get; set; }
        public FtxtcProfileSideState Upper { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }
    internal sealed class FtxtcProfileSideState
    {
        public string Outcome { get; set; }
        public double Endpoint { get; set; }
        public double CrossingG { get; set; }
        public int EvaluationCount { get; set; }
        public int AttemptedSolverCalls { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    internal sealed class FtxtcAdvancedAnalysesState
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FtxtcSpolarRecordState SpolarRecord { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FtxtcElectrostaticsState Electrostatics { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FtxtcProtonationState Protonation { get; set; }
    }

    internal sealed class FtxtcSpolarRecordState
    {
        public int SchemaVersion { get; set; } = 1;
        public string FoldedMode { get; set; }
        public string TemperatureMode { get; set; }
        public FtxtcFloatWithError HydrationEntropy { get; set; }
        public FtxtcFloatWithError ConformationalEntropy { get; set; }
        public FtxtcFloatWithError ResidueEstimate { get; set; }
        public FtxtcFloatWithError ReferenceTemperature { get; set; }
        public int CompletedIterations { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    internal sealed class FtxtcElectrostaticsState
    {
        public int SchemaVersion { get; set; } = 1;
        public FtxtcIonicStrengthFitState IonicStrengthFit { get; set; }
        public FtxtcLinearFitState CounterIonReleaseFit { get; set; }
        public string ErrorMethod { get; set; }
        public int IonicStrengthIterations { get; set; }
        public int CounterIonReleaseIterations { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    internal sealed class FtxtcIonicStrengthFitState
    {
        public FtxtcFloatWithError Kd0 { get; set; }
        public FtxtcFloatWithError Sensitivity { get; set; }
        public FtxtcFloatWithError Curvature { get; set; }
        public bool UsesCurvature { get; set; }
    }

    internal sealed class FtxtcLinearFitState
    {
        public FtxtcFloatWithError Slope { get; set; }
        public FtxtcFloatWithError Intercept { get; set; }
        public double ReferenceX { get; set; }
    }

    internal sealed class FtxtcProtonationState
    {
        public int SchemaVersion { get; set; } = 1;
        public FtxtcFloatWithError BindingEnthalpy { get; set; }
        public FtxtcFloatWithError ProtonationChange { get; set; }
        public string ErrorMethod { get; set; }
        public int CompletedIterations { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    internal sealed class FtxtcValidityState
    {
        public int SchemaVersion { get; set; } = AnalysisResultValiditySnapshot.CurrentSchemaVersion;
        public List<FtxtcValidityExperimentState> Experiments { get; set; } = new List<FtxtcValidityExperimentState>();

        internal static FtxtcValidityState Capture(AnalysisResultValiditySnapshot snapshot)
        {
            if (snapshot == null) return null;
            return new FtxtcValidityState
            {
                SchemaVersion = snapshot.SchemaVersion,
                Experiments = (snapshot.Experiments ?? new List<ExperimentFitInputSnapshot>())
                    .Select(FtxtcValidityExperimentState.Capture).ToList(),
            };
        }

        internal AnalysisResultValiditySnapshot Restore() => new AnalysisResultValiditySnapshot
        {
            SchemaVersion = SchemaVersion,
            Experiments = (Experiments ?? new List<FtxtcValidityExperimentState>())
                .Select(value => value.Restore()).ToList(),
        };
    }

    internal sealed class FtxtcValidityExperimentState
    {
        public string ExperimentId { get; set; }
        public string DisplayName { get; set; }
        public double CellConcentration { get; set; }
        public double CellConcentrationSd { get; set; }
        public double SyringeConcentration { get; set; }
        public double SyringeConcentrationSd { get; set; }
        public double CellVolume { get; set; }
        public FtxtcValidityProcessingState Processing { get; set; }
        public List<FtxtcValidityAttributeState> Attributes { get; set; } = new List<FtxtcValidityAttributeState>();
        public List<FtxtcValidityInjectionState> IncludedInjections { get; set; } = new List<FtxtcValidityInjectionState>();
        public List<FtxtcValiditySegmentState> Segments { get; set; } = new List<FtxtcValiditySegmentState>();

        internal static FtxtcValidityExperimentState Capture(ExperimentFitInputSnapshot value) => new FtxtcValidityExperimentState
        {
            ExperimentId = value.ExperimentID,
            DisplayName = value.DisplayName,
            CellConcentration = value.CellConcentration,
            CellConcentrationSd = value.CellConcentrationSD,
            SyringeConcentration = value.SyringeConcentration,
            SyringeConcentrationSd = value.SyringeConcentrationSD,
            CellVolume = value.CellVolume,
            Processing = FtxtcValidityProcessingState.Capture(value.Processing),
            Attributes = (value.Attributes ?? new List<ExperimentAttributeSnapshot>())
                .Select(FtxtcValidityAttributeState.Capture).ToList(),
            IncludedInjections = (value.IncludedInjections ?? new List<InjectionFitInputSnapshot>())
                .Select(FtxtcValidityInjectionState.Capture).ToList(),
            Segments = (value.Segments ?? new List<TandemSegmentSnapshot>())
                .Select(FtxtcValiditySegmentState.Capture).ToList(),
        };

        internal ExperimentFitInputSnapshot Restore() => new ExperimentFitInputSnapshot
        {
            ExperimentID = ExperimentId,
            DisplayName = DisplayName,
            CellConcentration = CellConcentration,
            CellConcentrationSD = CellConcentrationSd,
            SyringeConcentration = SyringeConcentration,
            SyringeConcentrationSD = SyringeConcentrationSd,
            CellVolume = CellVolume,
            Processing = Processing?.Restore(),
            Attributes = (Attributes ?? new List<FtxtcValidityAttributeState>()).Select(value => value.Restore()).ToList(),
            IncludedInjections = (IncludedInjections ?? new List<FtxtcValidityInjectionState>()).Select(value => value.Restore()).ToList(),
            Segments = (Segments ?? new List<FtxtcValiditySegmentState>()).Select(value => value.Restore()).ToList(),
        };
    }

    internal sealed class FtxtcValidityProcessingState
    {
        public string Type { get; set; }
        public int BaselinePointCount { get; set; }
        public double BaselineFirstValue { get; set; }
        public double BaselineLastValue { get; set; }
        public double BaselineValueSum { get; set; }
        public double BaselineValueSumSquares { get; set; }

        internal static FtxtcValidityProcessingState Capture(ExperimentProcessingSnapshot value) => value == null ? null : new FtxtcValidityProcessingState
        {
            Type = FtxtcWireIds.Processor(value.BaselineType),
            BaselinePointCount = value.BaselinePointCount,
            BaselineFirstValue = value.BaselineFirstValue,
            BaselineLastValue = value.BaselineLastValue,
            BaselineValueSum = value.BaselineValueSum,
            BaselineValueSumSquares = value.BaselineValueSumSquares,
        };

        internal ExperimentProcessingSnapshot Restore() => new ExperimentProcessingSnapshot
        {
            BaselineType = FtxtcWireIds.Processor(Type),
            BaselinePointCount = BaselinePointCount,
            BaselineFirstValue = BaselineFirstValue,
            BaselineLastValue = BaselineLastValue,
            BaselineValueSum = BaselineValueSum,
            BaselineValueSumSquares = BaselineValueSumSquares,
        };
    }

    internal sealed class FtxtcValidityAttributeState
    {
        public string Key { get; set; }
        public bool BoolValue { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? IntValue { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ValueId { get; set; }
        public double DoubleValue { get; set; }
        public string StringValue { get; set; }
        public double ParameterValue { get; set; }
        public double ParameterSd { get; set; }

        internal static FtxtcValidityAttributeState Capture(ExperimentAttributeSnapshot value) => new FtxtcValidityAttributeState
        {
            Key = FtxtcWireIds.Attribute(value.Key),
            BoolValue = value.BoolValue,
            IntValue = FtxtcWireIds.UsesNumericAttributeIntValue(value.Key) ? value.IntValue : null,
            ValueId = FtxtcWireIds.AttributeValueId(value.Key, value.IntValue),
            DoubleValue = value.DoubleValue,
            StringValue = value.StringValue,
            ParameterValue = value.ParameterValue,
            ParameterSd = value.ParameterSD,
        };

        internal ExperimentAttributeSnapshot Restore()
        {
            var key = FtxtcWireIds.Attribute(Key);
            return new ExperimentAttributeSnapshot
            {
                Key = key,
                BoolValue = BoolValue,
                IntValue = FtxtcWireIds.AttributeIntValue(key, ValueId, IntValue),
                DoubleValue = DoubleValue,
                StringValue = StringValue,
                ParameterValue = ParameterValue,
                ParameterSD = ParameterSd,
            };
        }
    }

    internal sealed class FtxtcValidityInjectionState
    {
        public int Id { get; set; }
        public double Volume { get; set; }
        public double? IntegrationStartDelay { get; set; }
        public double? IntegrationEndOffset { get; set; }
        public double? RawPeakArea { get; set; }
        public double? RawPeakAreaSd { get; set; }
        public double PeakArea { get; set; }
        public double PeakAreaSd { get; set; }
        public double ActualCellConcentration { get; set; }
        public double ActualTitrantConcentration { get; set; }
        public double Ratio { get; set; }

        internal static FtxtcValidityInjectionState Capture(InjectionFitInputSnapshot value) => new FtxtcValidityInjectionState
        {
            Id = value.ID,
            Volume = value.Volume,
            IntegrationStartDelay = value.IntegrationStartDelay,
            IntegrationEndOffset = value.IntegrationEndOffset,
            RawPeakArea = value.RawPeakArea,
            RawPeakAreaSd = value.RawPeakAreaSD,
            PeakArea = value.PeakArea,
            PeakAreaSd = value.PeakAreaSD,
            ActualCellConcentration = value.ActualCellConcentration,
            ActualTitrantConcentration = value.ActualTitrantConcentration,
            Ratio = value.Ratio,
        };

        internal InjectionFitInputSnapshot Restore() => new InjectionFitInputSnapshot
        {
            ID = Id,
            Volume = Volume,
            IntegrationStartDelay = IntegrationStartDelay,
            IntegrationEndOffset = IntegrationEndOffset,
            RawPeakArea = RawPeakArea,
            RawPeakAreaSD = RawPeakAreaSd,
            PeakArea = PeakArea,
            PeakAreaSD = PeakAreaSd,
            ActualCellConcentration = ActualCellConcentration,
            ActualTitrantConcentration = ActualTitrantConcentration,
            Ratio = Ratio,
        };
    }

    internal sealed class FtxtcValiditySegmentState
    {
        public int FirstInjectionId { get; set; }
        public double InitialCellConcentration { get; set; }
        public double InitialTitrantConcentration { get; set; }

        internal static FtxtcValiditySegmentState Capture(TandemSegmentSnapshot value) => new FtxtcValiditySegmentState
        {
            FirstInjectionId = value.FirstInjectionID,
            InitialCellConcentration = value.SegmentInitialActiveCellConc,
            InitialTitrantConcentration = value.SegmentInitialActiveTitrantConc,
        };

        internal TandemSegmentSnapshot Restore() => new TandemSegmentSnapshot
        {
            FirstInjectionID = FirstInjectionId,
            SegmentInitialActiveCellConc = InitialCellConcentration,
            SegmentInitialActiveTitrantConc = InitialTitrantConcentration,
        };
    }

    internal enum FtxbScalarType : byte
    {
        Float32 = 1,
        Float64 = 2,
        UInt8 = 3
    }

    internal static class FtxbCodec
    {
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("FTXB");
        const byte Version = 1;
        const int HeaderLength = 16;

        internal static byte[] EncodeFloat32(int rows, int columns, Func<int, int, float> value)
        {
            ValidateShape(rows, columns);
            var bytes = new byte[checked(HeaderLength + rows * columns * 4)];
            WriteHeader(bytes, FtxbScalarType.Float32, rows, columns);
            var offset = HeaderLength;
            for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                WriteInt32(bytes, offset, BitConverter.ToInt32(BitConverter.GetBytes(value(row, column)), 0));
                offset += 4;
            }
            return bytes;
        }

        internal static byte[] EncodeFloat64(int rows, int columns, Func<int, int, double> value)
        {
            ValidateShape(rows, columns);
            var bytes = new byte[checked(HeaderLength + rows * columns * 8)];
            WriteHeader(bytes, FtxbScalarType.Float64, rows, columns);
            var offset = HeaderLength;
            for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                WriteInt64(bytes, offset, BitConverter.DoubleToInt64Bits(value(row, column)));
                offset += 8;
            }
            return bytes;
        }

        internal static byte[] EncodeUInt8(int rows, int columns, Func<int, int, byte> value)
        {
            ValidateShape(rows, columns);
            var bytes = new byte[checked(HeaderLength + rows * columns)];
            WriteHeader(bytes, FtxbScalarType.UInt8, rows, columns);
            var offset = HeaderLength;
            for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
                bytes[offset++] = value(row, column);
            return bytes;
        }

        internal static float[,] DecodeFloat32(byte[] bytes, string path)
        {
            var shape = ReadHeader(bytes, path, FtxbScalarType.Float32, 4);
            var result = new float[shape.rows, shape.columns];
            var offset = HeaderLength;
            for (var row = 0; row < shape.rows; row++)
            for (var column = 0; column < shape.columns; column++)
            {
                result[row, column] = BitConverter.ToSingle(BitConverter.GetBytes(ReadInt32(bytes, offset)), 0);
                offset += 4;
            }
            return result;
        }

        internal static double[,] DecodeFloat64(byte[] bytes, string path)
        {
            var shape = ReadHeader(bytes, path, FtxbScalarType.Float64, 8);
            var result = new double[shape.rows, shape.columns];
            var offset = HeaderLength;
            for (var row = 0; row < shape.rows; row++)
            for (var column = 0; column < shape.columns; column++)
            {
                result[row, column] = BitConverter.Int64BitsToDouble(ReadInt64(bytes, offset));
                offset += 8;
            }
            return result;
        }

        internal static byte[,] DecodeUInt8(byte[] bytes, string path)
        {
            var shape = ReadHeader(bytes, path, FtxbScalarType.UInt8, 1);
            var result = new byte[shape.rows, shape.columns];
            var offset = HeaderLength;
            for (var row = 0; row < shape.rows; row++)
            for (var column = 0; column < shape.columns; column++)
                result[row, column] = bytes[offset++];
            return result;
        }

        static void ValidateShape(int rows, int columns)
        {
            if (rows < 0 || columns <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
            checked { _ = rows * columns; }
        }

        static void WriteHeader(byte[] bytes, FtxbScalarType scalar, int rows, int columns)
        {
            Array.Copy(Magic, bytes, Magic.Length);
            bytes[4] = Version;
            bytes[5] = (byte)scalar;
            bytes[6] = 1; // row-major
            bytes[7] = 0;
            WriteInt32(bytes, 8, rows);
            WriteInt32(bytes, 12, columns);
        }

        static (int rows, int columns) ReadHeader(byte[] bytes, string path, FtxbScalarType expected, int scalarBytes)
        {
            if (bytes == null || bytes.Length < HeaderLength
                || !Magic.SequenceEqual(bytes.Take(4))
                || bytes[4] != Version
                || bytes[5] != (byte)expected
                || bytes[6] != 1)
                throw new InvalidDataException($"FTXTC binary entry '{path}' has an invalid FTXB header.");
            var rows = ReadInt32(bytes, 8);
            var columns = ReadInt32(bytes, 12);
            if (rows < 0 || columns <= 0) throw new InvalidDataException($"FTXTC binary entry '{path}' has an invalid shape.");
            long expectedLength = HeaderLength + checked((long)rows * columns * scalarBytes);
            if (bytes.LongLength != expectedLength) throw new InvalidDataException($"FTXTC binary entry '{path}' has an invalid payload length.");
            return (rows, columns);
        }

        static void WriteInt32(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        static int ReadInt32(byte[] bytes, int offset) =>
            bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24;

        static void WriteInt64(byte[] bytes, int offset, long value)
        {
            for (var index = 0; index < 8; index++) bytes[offset + index] = (byte)(value >> (8 * index));
        }

        static long ReadInt64(byte[] bytes, int offset)
        {
            ulong value = 0;
            for (var index = 0; index < 8; index++) value |= (ulong)bytes[offset + index] << (8 * index);
            return unchecked((long)value);
        }
    }

    public static class FTXTCWriter
    {
        internal static async Task WriteStream(
            Stream destination,
            IEnumerable<ExperimentData> experiments,
            IEnumerable<AnalysisResult> results = null,
            IEnumerable<ITCDataContainer> contentOrder = null,
            IEnumerable<AnalysisReport> reports = null)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            var experimentList = experiments?.ToList() ?? new List<ExperimentData>();
            var resultList = results?.ToList() ?? new List<AnalysisResult>();
            var reportList = reports?.ToList() ?? new List<AnalysisReport>();
            var entries = new Dictionary<string, (string mediaType, byte[] bytes)>(StringComparer.Ordinal);
            var project = new FtxtcProject();

            project.ContentOrder = CaptureContentOrder(experimentList, resultList, contentOrder);

            var solutions = experimentList.Select(experiment => experiment.Solution)
                .Concat(resultList.SelectMany(result => result.Solution.Solutions))
                .Where(solution => solution != null)
                .GroupBy(solution => solution.Guid, StringComparer.Ordinal)
                // Legacy imports can expose equivalent solution instances with the
                // same persisted id. Never let a valid duplicate erase invalidity.
                .Select(group => group.FirstOrDefault(solution => !solution.IsValid) ?? group.First())
                .OrderBy(solution => solution.Guid, StringComparer.Ordinal)
                .ToList();

            for (var index = 0; index < experimentList.Count; index++)
            {
                var experiment = experimentList[index];
                var prefix = $"experiments/{index:D6}";
                var metadataPath = prefix + "/experiment.json";
                var thermogramPath = prefix + "/thermogram.ftxb";
                var baselinePath = prefix + "/baseline.ftxb";

                var metadata = CaptureExperiment(experiment);
                entries.Add(metadataPath, ("application/json", FTXTCFormat.JsonBytes(metadata)));
                entries.Add(thermogramPath, ("application/x-ftxb", EncodeDataPoints(experiment.DataPoints)));
                entries.Add(baselinePath, ("application/x-ftxb", EncodeBaseline(experiment.Processor?.Interpolator?.Baseline)));
                project.Experiments.Add(new FtxtcExperimentReference
                {
                    Id = experiment.UniqueID,
                    Metadata = metadataPath,
                    Thermogram = thermogramPath,
                    Baseline = baselinePath,
                });
            }

            for (var index = 0; index < solutions.Count; index++)
            {
                var solution = solutions[index];
                var prefix = $"solutions/{index:D6}";
                var metadataPath = prefix + "/solution.json";
                string bootstrapPath = null;
                entries.Add(metadataPath, ("application/json", FTXTCFormat.JsonBytes(CaptureSolution(solution))));
                if (solution.BootstrapSolutions.Count > 0)
                {
                    bootstrapPath = prefix + "/bootstrap.json";
                    CaptureBootstrap(solution, prefix, entries, bootstrapPath);
                }
                project.Solutions.Add(new FtxtcSolutionReference
                {
                    Id = solution.Guid,
                    ExperimentId = solution.Data.UniqueID,
                    Metadata = metadataPath,
                    Bootstrap = bootstrapPath,
                });
            }

            for (var index = 0; index < resultList.Count; index++)
            {
                var result = resultList[index];
                var metadataPath = $"results/{index:D6}/result.json";
                var state = CaptureResult(result);
                entries.Add(metadataPath, ("application/json", FTXTCFormat.JsonBytes(state)));
                project.Results.Add(new FtxtcResultReference { Id = result.UniqueID, Metadata = metadataPath });
            }

            for (var index = 0; index < reportList.Count; index++)
            {
                var report = reportList[index];
                var metadataPath = $"reports/{index:D6}/report.json";
                entries.Add(metadataPath, ("application/json", FTXTCFormat.JsonBytes(CaptureReport(report))));
                project.Reports.Add(new FtxtcReportReference { Id = report.UniqueID, Metadata = metadataPath });
            }

            entries.Add(FTXTCFormat.ProjectPath, ("application/json", FTXTCFormat.JsonBytes(project)));
            var manifest = new FtxtcManifest { WriterVersion = AppVersion.FullVersionString };
            manifest.Entries = entries.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => new FtxtcManifestEntry
            {
                Path = item.Key,
                MediaType = item.Value.mediaType,
                Length = item.Value.bytes.LongLength,
                Sha256 = FTXTCFormat.Sha256(item.Value.bytes)
            }).ToList();

            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            await WriteEntryAsync(archive, FTXTCFormat.ManifestPath, FTXTCFormat.JsonBytes(manifest));
            foreach (var item in entries.OrderBy(item => item.Key, StringComparer.Ordinal))
                await WriteEntryAsync(archive, item.Key, item.Value.bytes);
        }

        public static async Task WriteFileAsync(
            string path,
            IEnumerable<ExperimentData> experiments,
            IEnumerable<AnalysisResult> results = null,
            IEnumerable<ITCDataContainer> contentOrder = null,
            IEnumerable<AnalysisReport> reports = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A project path is required.", nameof(path));
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                    await WriteStream(stream, experiments, results, contentOrder, reports);
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        static List<FtxtcContentReference> CaptureContentOrder(
            IReadOnlyList<ExperimentData> experiments,
            IReadOnlyList<AnalysisResult> results,
            IEnumerable<ITCDataContainer> requestedOrder)
        {
            var expected = experiments.Cast<ITCDataContainer>().Concat(results).ToList();
            var ordered = requestedOrder?.ToList() ?? expected;
            if (ordered.Count != expected.Count
                || ordered.Any(item => item == null)
                || ordered.Distinct().Count() != ordered.Count
                || expected.Any(item => !ordered.Contains(item)))
                throw new InvalidDataException("FTXTC content order must contain every saved experiment and result exactly once.");

            return ordered.Select(item => new FtxtcContentReference
            {
                Type = item is ExperimentData ? "experiment" : "result",
                Id = item.UniqueID,
            }).ToList();
        }

        static FtxtcExperimentState CaptureExperiment(ExperimentData experiment) => new FtxtcExperimentState
        {
            Id = experiment.UniqueID,
            FileName = experiment.FileName,
            Name = experiment.Name,
            Date = experiment.Date,
            DateSource = DateSourceId(experiment.DateSource),
            Comments = experiment.Comments,
            Included = experiment.Include,
            SourceFormat = DataFormatId(experiment.DataSourceFormat),
            Instrument = InstrumentId(experiment.Instrument),
            CellConcentration = FtxtcFloatWithError.Capture(experiment.CellConcentration),
            SyringeConcentration = FtxtcFloatWithError.Capture(experiment.SyringeConcentration),
            CellVolume = experiment.CellVolume,
            StirringSpeed = experiment.StirringSpeed,
            FeedbackMode = FeedbackId(experiment.FeedBackMode),
            TargetTemperature = experiment.TargetTemperature,
            MeasuredTemperature = experiment.MeasuredTemperature,
            InitialDelay = experiment.InitialDelay,
            TargetPowerDifference = experiment.TargetPowerDiff,
            AverageHeatDirection = HeatDirectionId(experiment.AverageHeatDirection),
            AttachedSolutionId = experiment.Solution?.Guid,
            Attributes = experiment.Attributes.Select(CaptureAttribute).OrderBy(item => item.Key, StringComparer.Ordinal).ToList(),
            Segments = (experiment.Segments ?? new List<TandemExperimentSegment>()).Select(segment => new FtxtcTandemSegmentState
            {
                FirstInjectionId = segment.FirstInjectionID,
                InitialCellConcentration = segment.SegmentInitialActiveCellConc,
                InitialTitrantConcentration = segment.SegmentInitialActiveTitrantConc,
            }).ToList(),
            Processor = CaptureProcessor(experiment.Processor),
            Injections = experiment.Injections.Select(injection => new FtxtcInjectionState
            {
                Id = injection.ID,
                Included = injection.Include,
                Time = injection.Time,
                Volume = injection.Volume,
                Delay = injection.Delay,
                Duration = injection.Duration,
                Filter = injection.Filter,
                Temperature = injection.Temperature,
                IntegrationStartDelay = injection.IntegrationStartDelay,
                IntegrationEndOffset = injection.IntegrationEndOffset,
                ActualCellConcentration = injection.ActualCellConcentration,
                ActualTitrantConcentration = injection.ActualTitrantConcentration,
                Ratio = injection.Ratio,
                IsIntegrated = injection.IsIntegrated,
                HeatDirection = HeatDirectionId(injection.HeatDirection),
                RawPeakArea = FtxtcFloatWithError.Capture(injection.RawPeakArea),
                CorrectedPeakArea = FtxtcFloatWithError.Capture(injection.PeakArea),
            }).ToList()
        };

        static FtxtcAttributeState CaptureAttribute(ExperimentAttribute attribute)
        {
            var usesValueId = FtxtcWireIds.UsesAttributeValueId(attribute.Key);
            return new FtxtcAttributeState
            {
                Key = FtxtcWireIds.Attribute(attribute.Key),
                Name = attribute.OptionName,
                BoolValue = attribute.BoolValue,
                IntValue = FtxtcWireIds.UsesNumericAttributeIntValue(attribute.Key) ? attribute.IntValue : null,
                ValueId = usesValueId ? FtxtcWireIds.AttributeValueId(attribute.Key, attribute.IntValue) : null,
                DoubleValue = attribute.DoubleValue,
                StringValue = attribute.StringValue,
                ParameterValue = FtxtcFloatWithError.Capture(attribute.ParameterValue),
            };
        }

        static FtxtcProcessorState CaptureProcessor(DataProcessor processor)
        {
            if (processor == null) return null;
            var state = new FtxtcProcessorState
            {
                Type = FtxtcWireIds.Processor(processor.BaselineType),
                Locked = processor.IsLocked,
                BaselineCompleted = processor.BaselineCompleted,
                DiscardIntegratedPoints = processor.DiscardIntegratedPoints,
                IntegrationLengthMode = processor.IntegrationLengthMode == InjectionData.IntegrationLengthMode.Factor ? "factor" : "time",
                IntegrationLengthFactor = processor.IntegrationLengthFactor,
            };
            if (processor.Interpolator is SplineInterpolator spline)
            {
                state.Spline = new FtxtcSplineState
                {
                    Algorithm = SplineAlgorithmId(spline.Algorithm),
                    Density = SplineDensityId(spline.PointDensity),
                    HandleMode = SplineHandleId(spline.HandleMode),
                    ShowHandles = spline.ShowHandles,
                    AllowPointTimeDragging = spline.AllowPointTimeDragging,
                    PointsPerInjection = spline.PointsPerInjection,
                    Points = spline.SplinePoints.Select(point => new FtxtcSplinePointState
                    {
                        Id = point.ID, Time = point.Time, Power = point.Power, Slope = point.Slope,
                        Locked = point.Locked, SlopeLocked = point.SlopeLocked,
                        Linear = point.Linear, UserDefined = point.UserDefined,
                    }).ToList(),
                };
            }
            else if (processor.Interpolator is PolynomialLeastSquaresInterpolator polynomial)
                state.Polynomial = new FtxtcPolynomialState { Degree = polynomial.Degree, ZLimit = polynomial.ZLimit };
            else if (processor.Interpolator is SegmentedBaselineInterpolator segmented)
            {
                state.Segmented = new FtxtcSegmentedState
                {
                    Degree = segmented.Degree,
                    Segments = segmented.Segments.Select(segment => new FtxtcBaselineSegmentState
                    {
                        Kind = segment.Kind == SegmentedBaselineInterpolator.BaselineSegmentKind.InitialDelay ? "initial-delay" : "injection-scope",
                        InjectionId = segment.InjectionID, StartTime = segment.StartTime, EndTime = segment.EndTime,
                        CenterTime = segment.CenterTime, Coefficients = segment.Coefficients.ToArray(),
                    }).ToList(),
                };
            }
            else if (processor.Interpolator is AssymetricLeastSquaresInterpolator asl)
                state.Asl = new FtxtcAslState { Iterations = asl.Iterations, Lambda = asl.Lambda, Asymmetry = asl.Asymmetry };
            return state;
        }

        static FtxtcSolutionState CaptureSolution(SolutionInterface solution)
        {
            var modelSchemaVersion = solution.ModelType == AnalysisModel.SequentialBindingSites ? 2 : 1;
            if (solution.ModelType == AnalysisModel.SequentialBindingSites)
            {
                var count = SequentialPersistenceShape.RequireExplicitSiteCount(
                    solution.ModelOptions.Values, "Sequential FTXTC solution");
                SequentialPersistenceShape.ValidateFittedParameters(
                    solution.Model.Parameters.Table.Values, count, "Sequential FTXTC solution");
                SequentialPersistenceShape.ValidateReportedParameterKeys(
                    solution.Parameters.Keys, count, "Sequential FTXTC solution");
            }

            return new FtxtcSolutionState
            {
                Id = solution.Guid,
                ExperimentId = solution.Data.UniqueID,
                ModelId = FtxtcWireIds.Model(solution.ModelType),
                ModelSchemaVersion = modelSchemaVersion,
                Weighted = solution.UseWeightedFitting,
                ErrorMethod = ErrorMethodId(solution.ErrorMethod),
                CloneOptions = CaptureCloneOptions(solution.Model.ModelCloneOptions),
                ModelOptions = solution.ModelOptions.Values.Select(CaptureAttribute).OrderBy(item => item.Key, StringComparer.Ordinal).ToList(),
                FittedParameters = solution.Model.Parameters.Table.Values.Select(parameter => new FtxtcParameterState
                {
                    Id = FtxtcWireIds.Parameter(parameter.Key), Value = parameter.Value, Locked = parameter.IsLocked,
                }).OrderBy(item => item.Id, StringComparer.Ordinal).ToList(),
                ReportedParameters = solution.Parameters.Select(parameter => new FtxtcReportedParameterState
                {
                    Id = FtxtcWireIds.Parameter(parameter.Key), Estimate = FtxtcFloatWithError.Capture(parameter.Value),
                }).OrderBy(item => item.Id, StringComparer.Ordinal).ToList(),
                Convergence = CaptureConvergence(solution.Convergence),
                ParameterBoundaryHit = solution.ParameterBoundaryHit,
                IsValid = solution.IsValid,
                Profile = CaptureProfile(solution.ProfileLikelihoodRun),
            };
        }

        static void CaptureBootstrap(SolutionInterface solution, string prefix,
            IDictionary<string, (string mediaType, byte[] bytes)> entries, string descriptorPath)
        {
            var snapshots = solution.BootstrapSolutions.Select((item, ordinal) =>
                BootstrapModelSnapshot.Capture(item, item.BootstrapReplicateIndex ?? ordinal)).ToList();
            var parameterIds = snapshots[0].Parameters.Select(parameter => FtxtcWireIds.Parameter(parameter.Key))
                .OrderBy(value => value, StringComparer.Ordinal).ToList();
            var injectionIds = solution.Data.Injections.Select(injection => injection.ID).ToList();
            foreach (var snapshot in snapshots)
            {
                var replicateParameters = snapshot.Parameters.Select(parameter => FtxtcWireIds.Parameter(parameter.Key))
                    .OrderBy(value => value, StringComparer.Ordinal).ToList();
                if (!parameterIds.SequenceEqual(replicateParameters))
                    throw new InvalidDataException("Every bootstrap replicate must contain exactly the declared parameter columns.");
                if (!injectionIds.SequenceEqual(snapshot.Injections.Select(injection => injection.ID)))
                    throw new InvalidDataException("Every bootstrap replicate must contain the declared injection columns in order.");
            }
            var parameterPath = prefix + "/bootstrap-parameters.ftxb";
            var lockPath = prefix + "/bootstrap-parameter-locks.ftxb";
            var injectionPath = prefix + "/bootstrap-injections.ftxb";
            var includePath = prefix + "/bootstrap-injection-includes.ftxb";
            var state = new FtxtcBootstrapState
            {
                ReplicateIndices = snapshots.Select(snapshot => snapshot.ReplicateIndex).ToList(),
                ParameterIds = parameterIds, InjectionIds = injectionIds,
                ParameterValues = parameterPath, ParameterLocks = lockPath,
                Injections = injectionPath, InjectionIncludes = includePath,
                Replicates = snapshots.Select(snapshot => new FtxtcBootstrapReplicateState
                {
                    CellConcentration = FtxtcFloatWithError.Capture(snapshot.CellConcentration),
                    SyringeConcentration = FtxtcFloatWithError.Capture(snapshot.SyringeConcentration),
                    CellVolume = snapshot.CellVolume, MeasuredTemperature = snapshot.MeasuredTemperature,
                    ParameterBoundaryHit = snapshot.ParameterBoundaryHit,
                    ModelOptions = snapshot.ModelOptions.Select(CaptureAttribute).OrderBy(item => item.Key, StringComparer.Ordinal).ToList(),
                    Segments = snapshot.Segments.Select(segment => new FtxtcTandemSegmentState
                    {
                        FirstInjectionId = segment.FirstInjectionID,
                        InitialCellConcentration = segment.InitialCellConcentration,
                        InitialTitrantConcentration = segment.InitialTitrantConcentration,
                    }).ToList(),
                }).ToList(),
            };
            entries.Add(parameterPath, ("application/x-ftxb", FtxbCodec.EncodeFloat64(snapshots.Count, parameterIds.Count,
                (row, column) => snapshots[row].Parameters.Single(parameter => FtxtcWireIds.Parameter(parameter.Key) == parameterIds[column]).Value)));
            entries.Add(lockPath, ("application/x-ftxb", FtxbCodec.EncodeUInt8(snapshots.Count, parameterIds.Count,
                (row, column) => snapshots[row].Parameters.Single(parameter => FtxtcWireIds.Parameter(parameter.Key) == parameterIds[column]).IsLocked ? (byte)1 : (byte)0)));
            entries.Add(injectionPath, ("application/x-ftxb", FtxbCodec.EncodeFloat64(snapshots.Count, injectionIds.Count * 4,
                (row, column) =>
                {
                    var injection = snapshots[row].Injections[column / 4];
                    switch (column % 4) { case 0: return injection.Volume; case 1: return injection.ActualCellConcentration; case 2: return injection.ActualTitrantConcentration; default: return injection.Ratio; }
                })));
            entries.Add(includePath, ("application/x-ftxb", FtxbCodec.EncodeUInt8(snapshots.Count, injectionIds.Count,
                (row, column) => snapshots[row].Injections[column].Include ? (byte)1 : (byte)0)));
            entries.Add(descriptorPath, ("application/json", FTXTCFormat.JsonBytes(state)));
        }

        static FtxtcReportState CaptureReport(AnalysisReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            return new FtxtcReportState
            {
                Id = report.UniqueID,
                Name = report.Name,
                Date = report.Date,
                Comments = report.Comments,
                ResultIds = report.ResultIds.ToList(),
                StudyContext = report.StudyContext.Copy(),
                InterpretationSettings = report.InterpretationSettings.Copy(),
                ApprovedInterpretation = report.ApprovedInterpretation?.Copy(),
            };
        }

        static FtxtcResultState CaptureResult(AnalysisResult result)
        {
            if (result.Model.ModelType == AnalysisModel.SequentialBindingSites)
            {
                var counts = result.Model.Models.Select(model =>
                    SequentialPersistenceShape.RequireExplicitSiteCount(
                        model.ModelOptions.Values, "Sequential FTXTC global member")).Distinct().ToList();
                if (counts.Count != 1)
                    throw new InvalidDataException(
                        "Sequential FTXTC global members must declare the same site count.");
                SequentialPersistenceShape.ValidateGlobalShape(
                    counts[0], result.Model.Parameters.Constraints,
                    result.Model.Parameters.GlobalTable.Keys, "Sequential FTXTC global result");
            }

            return new FtxtcResultState
            {
                Id = result.UniqueID, FileName = result.FileName, Name = result.Name, Date = result.Date, Comments = result.Comments,
                GlobalSolutionId = result.Solution.UniqueID, ModelId = FtxtcWireIds.Model(result.Model.ModelType),
                Weighted = result.Solution.UseWeightedFitting,
                MemberSolutionIds = result.Solution.Solutions.Select(solution => solution.Guid).ToList(),
                Constraints = result.Model.Parameters.Constraints.Select(item => new FtxtcConstraintState
                {
                    ParameterId = FtxtcWireIds.Parameter(item.Key), Constraint = ConstraintId(item.Value),
                }).OrderBy(item => item.ParameterId, StringComparer.Ordinal).ToList(),
                GlobalParameters = result.Model.Parameters.GlobalTable.Values.Select(parameter => new FtxtcParameterState
                {
                    Id = FtxtcWireIds.Parameter(parameter.Key), Value = parameter.Value, Locked = parameter.IsLocked,
                }).OrderBy(item => item.Id, StringComparer.Ordinal).ToList(),
                CloneOptions = CaptureCloneOptions(result.Model.ModelCloneOptions),
                Convergence = CaptureConvergence(result.Solution.Convergence),
                IsValid = result.Solution.IsValid,
                Validity = result.ValiditySnapshot == null
                    ? null
                    : JsonSerializer.SerializeToElement(FtxtcValidityState.Capture(result.ValiditySnapshot), FTXTCFormat.JsonOptions),
                AdvancedAnalyses = CaptureAdvancedAnalyses(result),
                Profile = CaptureProfile(result.Solution.ProfileLikelihoodRun),
            };
        }

        static FtxtcAdvancedAnalysesState CaptureAdvancedAnalyses(AnalysisResult result)
        {
            var state = new FtxtcAdvancedAnalysesState();
            var spolar = result.SpolarRecordAnalysis;
            if (spolar?.Result != null)
            {
                state.SpolarRecord = new FtxtcSpolarRecordState
                {
                    FoldedMode = SpolarFoldedModeId(spolar.CompletedFoldedMode ?? spolar.FoldedMode),
                    TemperatureMode = SpolarTemperatureModeId(spolar.CompletedTempMode ?? spolar.TempMode),
                    HydrationEntropy = FtxtcFloatWithError.Capture(spolar.Result.HydrationEntropy),
                    ConformationalEntropy = FtxtcFloatWithError.Capture(spolar.Result.ConformationalEntropy),
                    ResidueEstimate = FtxtcFloatWithError.Capture(spolar.Result.Rvalue),
                    ReferenceTemperature = FtxtcFloatWithError.Capture(spolar.Result.ReferenceTemperature),
                    CompletedIterations = spolar.CompletedIterations,
                    CompletedAtUtc = spolar.CompletedAtUtc,
                };
            }

            var electrostatics = result.ElectrostaticsAnalysis;
            if (electrostatics?.Calculated == true)
            {
                state.Electrostatics = new FtxtcElectrostaticsState
                {
                    IonicStrengthFit = CaptureIonicStrengthFit(electrostatics.IonicStrengthDependenceFit),
                    CounterIonReleaseFit = CaptureLinearFit(electrostatics.CounterIonReleaseFit),
                    ErrorMethod = ErrorMethodId(electrostatics.CompletedErrorEstimationMethod ?? ErrorEstimationMethod.None),
                    IonicStrengthIterations = electrostatics.CompletedIterations,
                    CounterIonReleaseIterations = electrostatics.CounterIonReleaseIterations,
                    CompletedAtUtc = electrostatics.CompletedAtUtc,
                };
            }

            var protonation = result.ProtonationAnalysis;
            if (protonation?.Fit is LinearFitWithError)
            {
                state.Protonation = new FtxtcProtonationState
                {
                    BindingEnthalpy = FtxtcFloatWithError.Capture(protonation.BindingEnthalpy.FloatWithError),
                    ProtonationChange = FtxtcFloatWithError.Capture(protonation.ProtonationChange),
                    ErrorMethod = ErrorMethodId(protonation.CompletedErrorEstimationMethod ?? ErrorEstimationMethod.None),
                    CompletedIterations = protonation.CompletedIterations,
                    CompletedAtUtc = protonation.CompletedAtUtc,
                };
            }

            return state.SpolarRecord == null && state.Electrostatics == null && state.Protonation == null ? null : state;
        }

        static FtxtcIonicStrengthFitState CaptureIonicStrengthFit(IonicStrengthDependenceFit fit) => fit == null ? null : new FtxtcIonicStrengthFitState
        {
            Kd0 = FtxtcFloatWithError.Capture(fit.Kd0),
            Sensitivity = FtxtcFloatWithError.Capture(fit.SaltSensitivity),
            Curvature = FtxtcFloatWithError.Capture(fit.Curvature),
            UsesCurvature = fit.UsesCurvature,
        };

        static FtxtcLinearFitState CaptureLinearFit(LinearFitWithError fit) => fit == null ? null : new FtxtcLinearFitState
        {
            Slope = FtxtcFloatWithError.Capture(fit.Slope),
            Intercept = FtxtcFloatWithError.Capture(fit.Intercept),
            ReferenceX = fit.ReferenceT,
        };

        static FtxtcCloneOptionsState CaptureCloneOptions(ModelCloneOptions options)
        {
            if (options == null) return null;
            return new FtxtcCloneOptionsState
            {
                IsGlobalClone = options.IsGlobalClone, ErrorMethod = ErrorMethodId(options.ErrorEstimationMethod),
                IncludeConcentrationErrors = options.IncludeConcentrationErrorsInBootstrap,
                EnableAutoConcentrationVariance = options.EnableAutoConcentrationVariance,
                AutoConcentrationVariance = options.AutoConcentrationVariance, DiscardedDataPoint = options.DiscardedDataPoint,
                UnlockParameters = options.UnlockBootstrapParameters,
            };
        }

        static FtxtcConvergenceState CaptureConvergence(SolverConvergence convergence)
        {
            if (convergence == null) return null;
            var value = convergence.ToSnapshot();
            return new FtxtcConvergenceState
            {
                Algorithm = value.Algorithm == SolverAlgorithm.NelderMead ? "nelder-mead" : "levenberg-marquardt",
                Termination = TerminationId(value.Termination), ErrorOutcome = ErrorOutcomeId(value.ErrorEstimationOutcome),
                Iterations = value.Iterations, Loss = value.Loss, TimeSeconds = value.TimeSeconds,
                MolarRmsdJoulesPerMole = value.MolarRmsdJoulesPerMole,
                ErrorEstimationTimeSeconds = value.ErrorEstimationTimeSeconds,
                FailureReason = value.FailureReason, ErrorEstimationSummary = value.ErrorEstimationSummary,
                ErrorEstimationLimitTerminations = value.ErrorEstimationLimitTerminations,
                ErrorEstimationAttemptedRefits = value.ErrorEstimationAttemptedRefits,
                ErrorEstimationSucceededRefits = value.ErrorEstimationSucceededRefits,
                ErrorEstimationFailedRefits = value.ErrorEstimationFailedRefits,
            };
        }

        static byte[] EncodeDataPoints(IReadOnlyList<DataPoint> points)
        {
            points = points ?? Array.Empty<DataPoint>();
            return FtxbCodec.EncodeFloat32(points.Count, 3, (row, column) =>
            {
                var point = points[row];
                switch (column)
                {
                    case 0: return point.Time;
                    case 1: return point.Power;
                    default: return point.Temperature;
                }
            });
        }

        static byte[] EncodeBaseline(IReadOnlyList<Energy> baseline)
        {
            baseline = baseline ?? Array.Empty<Energy>();
            return FtxbCodec.EncodeFloat64(baseline.Count, 4, (row, column) =>
            {
                var value = baseline[row].FloatWithError;
                switch (column)
                {
                    case 0: return value.Value;
                    case 1: return value.SD;
                    case 2: return value.Lower;
                    default: return value.Upper;
                }
            });
        }

        static async Task WriteEntryAsync(ZipArchive archive, string path, byte[] bytes)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var stream = entry.Open();
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }

        static string DataFormatId(ITCDataFormat value) => value switch
        {
            ITCDataFormat.ITC200 => "microcal-itc200",
            ITCDataFormat.FTITC => "ftitc", ITCDataFormat.FTXTC => "ftxtc", ITCDataFormat.TAITC => "ta-itc",
            ITCDataFormat.IntegratedHeats => "integrated-heats", ITCDataFormat.PEAQITCProject => "peaq-itc-project", ITCDataFormat.OriginProject => "origin-opj", ITCDataFormat.NanoITC => "nano-itc",
            ITCDataFormat.Unknown => "unknown", _ => throw new NotSupportedException("Unsupported data source format."),
        };
        static string InstrumentId(ITCInstrument value) => value switch
        {
            ITCInstrument.Unknown => "unknown", ITCInstrument.MicroCalITC200 => "microcal-itc200",
            ITCInstrument.MalvernITC200 => "microcal-peaq-itc",
            ITCInstrument.MicroCalVPITC => "microcal-vp-itc",
            ITCInstrument.TAInstrumentsITCStandard => "ta-itc-standard", ITCInstrument.TAInstrumentsITCLowVolume => "ta-itc-low-volume",
            _ => throw new NotSupportedException("Unsupported instrument value."),
        };
        static string FeedbackId(FeedbackMode value) => value switch
        { FeedbackMode.Null => "unknown", FeedbackMode.None => "none", FeedbackMode.Low => "low", FeedbackMode.High => "high", _ => throw new NotSupportedException() };

        static string DateSourceId(ExperimentDateSource value) => value switch
        { ExperimentDateSource.Unknown => null, ExperimentDateSource.DataFile => "data-file", ExperimentDateSource.FileSystem => "file-system", _ => throw new NotSupportedException() };
        static string HeatDirectionId(PeakHeatDirection value) => value switch
        { PeakHeatDirection.Unknown => "unknown", PeakHeatDirection.Exothermal => "exothermal", PeakHeatDirection.Endothermal => "endothermal", PeakHeatDirection.Both => "both", _ => throw new NotSupportedException() };
        static string SplineAlgorithmId(SplineInterpolator.SplineInterpolatorAlgorithm value) => value switch
        { SplineInterpolator.SplineInterpolatorAlgorithm.Smooth => "smooth", SplineInterpolator.SplineInterpolatorAlgorithm.Handles => "handles", SplineInterpolator.SplineInterpolatorAlgorithm.Rigid => "rigid", SplineInterpolator.SplineInterpolatorAlgorithm.Linear => "linear", _ => throw new NotSupportedException() };
        static string SplineDensityId(SplineInterpolator.SplinePointDensity value) => value switch
        { SplineInterpolator.SplinePointDensity.Sparse => "sparse", SplineInterpolator.SplinePointDensity.Balanced => "balanced", SplineInterpolator.SplinePointDensity.Dense => "dense", _ => throw new NotSupportedException() };
        static string SplineHandleId(SplineInterpolator.SplineHandleMode value) => value switch
        { SplineInterpolator.SplineHandleMode.Mean => "mean", SplineInterpolator.SplineHandleMode.Median => "median", SplineInterpolator.SplineHandleMode.MinVolatility => "minimum-volatility", _ => throw new NotSupportedException() };
        static FtxtcProfileRunState CaptureProfile(ProfileLikelihoodRunResult run)
        {
            if (run == null) return null;
            return new FtxtcProfileRunState
            {
                ConfidenceLevel = run.ConfidenceLevel, Calibration = ProfileCalibrationId(run.Calibration), N = run.N, P = run.P, Q = run.Q, Df = run.Df,
                BaselineObjective = run.BaselineObjective, TargetIncrement = run.TargetIncrement, Algorithm = ProfileAlgorithmId(run.Algorithm),
                Weighted = run.UseWeightedFitting, Tolerance = run.Tolerance, OptimizerToleranceSetting = run.OptimizerToleranceSetting,
                CandidateIterationLimit = run.CandidateIterationLimit,
                ExpansionLimit = run.ExpansionLimit, RefinementLimit = run.RefinementLimit, ElapsedSeconds = run.Elapsed.TotalSeconds,
                Outcome = ErrorOutcomeId(run.Outcome), AttemptedSolverCalls = run.AttemptedSolverCalls,
                Coordinates = run.Coordinates.Select(c => new FtxtcProfileCoordinateState
                {
                    ParameterId = FtxtcWireIds.Parameter(c.Id.Parameter), Scope = ProfileScopeId(c.Id.Scope), ExperimentId = c.Id.ExperimentIdentity,
                    Index = c.Id.PrimaryOptimizerIndex, BestValue = c.BestValue, LowerBound = c.LowerBound, UpperBound = c.UpperBound,
                    Lower = CaptureProfileSide(c.Lower), Upper = CaptureProfileSide(c.Upper), Warnings = c.ShapeWarnings.ToList(),
                }).ToList(),
            };
        }
        static FtxtcProfileSideState CaptureProfileSide(ProfileSideResult side) => new FtxtcProfileSideState
        {
            Outcome = ProfileSideOutcomeId(side.Outcome), Endpoint = side.Endpoint, CrossingG = side.CrossingG,
            EvaluationCount = side.EvaluationCount, AttemptedSolverCalls = side.AttemptedSolverCalls, Warnings = side.Warnings.ToList(),
        };

        static string ProfileCalibrationId(ProfileLikelihoodCalibration value) => value switch
        {
            ProfileLikelihoodCalibration.UnweightedFCalibratedRss => "unweighted-f-calibrated-rss",
            ProfileLikelihoodCalibration.WeightedChiSquared => "weighted-chi-squared",
            _ => throw new NotSupportedException(),
        };
        static string ProfileAlgorithmId(SolverAlgorithm value) => value switch
        {
            SolverAlgorithm.NelderMead => "nelder-mead",
            SolverAlgorithm.LevenbergMarquardt => "levenberg-marquardt",
            _ => throw new NotSupportedException(),
        };
        static string ProfileScopeId(ParameterBoundaryScope value) => value switch
        {
            ParameterBoundaryScope.Local => "local",
            ParameterBoundaryScope.Shared => "shared",
            _ => throw new NotSupportedException(),
        };
        static string ProfileSideOutcomeId(ProfileSideOutcome value) => value switch
        {
            ProfileSideOutcome.EndpointFound => "endpoint-found",
            ProfileSideOutcome.BoundReachedBeforeCrossing => "bound-reached-before-crossing",
            ProfileSideOutcome.SearchExhausted => "search-exhausted",
            ProfileSideOutcome.OptimizerFailure => "optimizer-failure",
            ProfileSideOutcome.NonFiniteCandidate => "non-finite-candidate",
            ProfileSideOutcome.Cancelled => "cancelled",
            ProfileSideOutcome.PrimaryMinimumImproved => "primary-minimum-improved",
            _ => throw new NotSupportedException(),
        };

        static string ErrorMethodId(ErrorEstimationMethod value) => value switch
        { ErrorEstimationMethod.None => "none", ErrorEstimationMethod.BootstrapResiduals => "bootstrap-residuals", ErrorEstimationMethod.LeaveOneOut => "leave-one-out", ErrorEstimationMethod.ProfileLikelihood => "profile-likelihood", _ => throw new NotSupportedException() };
        static string ConstraintId(VariableConstraint value) => value switch
        { VariableConstraint.None => "none", VariableConstraint.TemperatureDependent => "temperature-dependent", VariableConstraint.SameForAll => "same-for-all", _ => throw new NotSupportedException() };
        static string TerminationId(SolverTermination value) => value switch
        {
            SolverTermination.Unknown => "unknown", SolverTermination.Converged => "converged", SolverTermination.SmallStep => "small-step",
            SolverTermination.SmallGradient => "small-gradient", SolverTermination.ReachedTarget => "reached-target",
            SolverTermination.IterationLimit => "iteration-limit", SolverTermination.EvaluationLimit => "evaluation-limit",
            SolverTermination.TimeLimit => "time-limit", SolverTermination.Cancelled => "cancelled",
            SolverTermination.InvalidValues => "invalid-values", SolverTermination.Failed => "failed", _ => throw new NotSupportedException(),
        };

        static string SpolarFoldedModeId(FTSRMethod.SRFoldedMode value) => value switch
        {
            FTSRMethod.SRFoldedMode.Glob => "globular",
            FTSRMethod.SRFoldedMode.Intermediate => "intermediate",
            FTSRMethod.SRFoldedMode.ID => "intrinsically-disordered",
            _ => throw new NotSupportedException($"Unknown Spolar folded mode '{value}'."),
        };

        static string SpolarTemperatureModeId(FTSRMethod.SRTempMode value) => value switch
        {
            FTSRMethod.SRTempMode.IsoEntropicPoint => "isoentropic-point",
            FTSRMethod.SRTempMode.MeanTemperature => "mean-temperature",
            FTSRMethod.SRTempMode.ReferenceTemperature => "reference-temperature",
            _ => throw new NotSupportedException($"Unknown Spolar temperature mode '{value}'."),
        };
        static string ErrorOutcomeId(ErrorEstimationOutcome value) => value switch
        {
            ErrorEstimationOutcome.None => "none", ErrorEstimationOutcome.NotRun => "not-run", ErrorEstimationOutcome.Completed => "completed",
            ErrorEstimationOutcome.PartialFailure => "partial-failure", ErrorEstimationOutcome.CompleteFailure => "complete-failure",
            ErrorEstimationOutcome.Cancelled => "cancelled", _ => throw new NotSupportedException(),
        };
    }
}
