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
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.Export
{
    /// <summary>
    /// Version 1 of the FT-ITC project container. The package is a checksummed ZIP
    /// whose numeric traces are stored in typed, little-endian FTXB payloads.
    /// </summary>
    internal static class FTXTCFormat
    {
        internal const string Extension = ".ftxtc";
        internal const string FormatName = "ftxtc";
        internal const int SchemaMajor = 1;
        internal const int SchemaMinor = 0;
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
        public int ProjectSchemaVersion { get; set; } = 1;
        public List<FtxtcExperimentReference> Experiments { get; set; } = new List<FtxtcExperimentReference>();
        public List<FtxtcSolutionReference> Solutions { get; set; } = new List<FtxtcSolutionReference>();
        public List<FtxtcResultReference> Results { get; set; } = new List<FtxtcResultReference>();
    }

    /// <summary>
    /// Future schema steps migrate parsed storage DTOs here, before any domain
    /// object is constructed. Writers always emit only the current DTO schema.
    /// </summary>
    internal static class FtxtcStorageMigrationPipeline
    {
        internal static FtxtcProject MigrateToCurrent(FtxtcProject project)
        {
            if (project == null) throw new InvalidDataException("FTXTC root project is missing.");
            if (project.ProjectSchemaVersion != 1)
                throw new NotSupportedException($"No FTXTC storage migration is available for project schema {project.ProjectSchemaVersion}.");
            return project;
        }
    }

    internal sealed class FtxtcExperimentReference
    {
        public string Id { get; set; }
        public string Metadata { get; set; }
        public string Thermogram { get; set; }
        public string Baseline { get; set; }
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

    internal sealed class FtxtcExperimentState
    {
        public string Id { get; set; }
        public string FileName { get; set; }
        public string Name { get; set; }
        public DateTime Date { get; set; }
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
        public int IntValue { get; set; }
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
        public double TimeSeconds { get; set; }
        public double ErrorEstimationTimeSeconds { get; set; }
        public string FailureReason { get; set; }
        public string ErrorEstimationSummary { get; set; }
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
        public AnalysisResultValiditySnapshot Validity { get; set; }
    }

    internal sealed class FtxtcConstraintState { public string ParameterId { get; set; } public string Constraint { get; set; } }

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
            IEnumerable<AnalysisResult> results = null)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            var experimentList = experiments?.ToList() ?? new List<ExperimentData>();
            var resultList = results?.ToList() ?? new List<AnalysisResult>();
            var entries = new Dictionary<string, (string mediaType, byte[] bytes)>(StringComparer.Ordinal);
            var project = new FtxtcProject();

            var solutions = experimentList.Select(experiment => experiment.Solution)
                .Concat(resultList.SelectMany(result => result.Solution.Solutions))
                .Where(solution => solution != null)
                .GroupBy(solution => solution.Guid, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(solution => solution.Guid, StringComparer.Ordinal)
                .ToList();

            for (var index = 0; index < experimentList.Count; index++)
            {
                var experiment = experimentList[index];
                var prefix = $"experiments/{index:D6}";
                var metadataPath = prefix + "/experiment.json";
                var thermogramPath = prefix + "/thermogram.ftxb";
                var baselinePath = prefix + "/baseline.ftxb";
                var correctedPath = prefix + "/corrected-trace.ftxb";

                var metadata = CaptureExperiment(experiment);
                entries.Add(metadataPath, ("application/json", FTXTCFormat.JsonBytes(metadata)));
                entries.Add(thermogramPath, ("application/x-ftxb", EncodeDataPoints(experiment.DataPoints)));
                entries.Add(baselinePath, ("application/x-ftxb", EncodeBaseline(experiment.Processor?.Interpolator?.Baseline)));
                entries.Add(correctedPath, ("application/x-ftxb", EncodeDataPoints(experiment.BaseLineCorrectedDataPoints)));
                project.Experiments.Add(new FtxtcExperimentReference
                {
                    Id = experiment.UniqueID,
                    Metadata = metadataPath,
                    Thermogram = thermogramPath,
                    Baseline = baselinePath,
                    CorrectedTrace = correctedPath
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
            WriteEntry(archive, FTXTCFormat.ManifestPath, FTXTCFormat.JsonBytes(manifest));
            foreach (var item in entries.OrderBy(item => item.Key, StringComparer.Ordinal))
                WriteEntry(archive, item.Key, item.Value.bytes);
        }

        public static async Task WriteFileAsync(string path, IEnumerable<ExperimentData> experiments, IEnumerable<AnalysisResult> results = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A project path is required.", nameof(path));
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await WriteStream(stream, experiments, results);
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        static FtxtcExperimentState CaptureExperiment(ExperimentData experiment) => new FtxtcExperimentState
        {
            Id = experiment.UniqueID,
            FileName = experiment.FileName,
            Name = experiment.Name,
            Date = experiment.Date,
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
                CorrectedPeakArea = FtxtcFloatWithError.Capture(injection.PeakArea)
            }).ToList()
        };

        static FtxtcAttributeState CaptureAttribute(ExperimentAttribute attribute) => new FtxtcAttributeState
        {
            Key = FtxtcWireIds.Attribute(attribute.Key),
            Name = attribute.OptionName,
            BoolValue = attribute.BoolValue,
            IntValue = attribute.IntValue,
            DoubleValue = attribute.DoubleValue,
            StringValue = attribute.StringValue,
            ParameterValue = FtxtcFloatWithError.Capture(attribute.ParameterValue),
        };

        static FtxtcProcessorState CaptureProcessor(DataProcessor processor)
        {
            if (processor == null) return null;
            var state = new FtxtcProcessorState
            {
                Type = ProcessorTypeId(processor.BaselineType),
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

        static FtxtcSolutionState CaptureSolution(SolutionInterface solution) => new FtxtcSolutionState
        {
            Id = solution.Guid,
            ExperimentId = solution.Data.UniqueID,
            ModelId = FtxtcWireIds.Model(solution.ModelType),
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
        };

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

        static FtxtcResultState CaptureResult(AnalysisResult result) => new FtxtcResultState
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
            Convergence = CaptureConvergence(result.Solution.Convergence), Validity = result.ValiditySnapshot,
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
                ErrorEstimationTimeSeconds = value.ErrorEstimationTimeSeconds,
                FailureReason = value.FailureReason, ErrorEstimationSummary = value.ErrorEstimationSummary,
            };
        }

        static byte[] EncodeDataPoints(IReadOnlyList<DataPoint> points)
        {
            points = points ?? Array.Empty<DataPoint>();
            return FtxbCodec.EncodeFloat32(points.Count, 7, (row, column) =>
            {
                var point = points[row];
                switch (column)
                {
                    case 0: return point.Time;
                    case 1: return point.Power;
                    case 2: return point.Temperature;
                    case 3: return point.DT;
                    case 4: return point.ShieldT;
                    case 5: return point.ATP;
                    default: return point.JFBI;
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

        static void WriteEntry(ZipArchive archive, string path, byte[] bytes)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var stream = entry.Open();
            stream.Write(bytes, 0, bytes.Length);
        }

        static string DataFormatId(ITCDataFormat value) => value switch
        {
            ITCDataFormat.ITC200 => "microcal-itc200", ITCDataFormat.VPITC => "microcal-vpitc",
            ITCDataFormat.FTITC => "ftitc", ITCDataFormat.FTXTC => "ftxtc", ITCDataFormat.TAITC => "ta-itc",
            ITCDataFormat.IntegratedHeats => "integrated-heats", ITCDataFormat.PEAQITCProject => "peaq-itc-project",
            ITCDataFormat.Unknown => "unknown", _ => throw new NotSupportedException("Unsupported data source format."),
        };
        static string InstrumentId(ITCInstrument value) => value switch
        {
            ITCInstrument.Unknown => "unknown", ITCInstrument.MicroCalITC200 => "microcal-itc200",
            ITCInstrument.MalvernITC200 => "microcal-peaq-itc", ITCInstrument.MicroCalVPITC => "microcal-vp-itc",
            ITCInstrument.TAInstrumentsITCStandard => "ta-itc-standard", ITCInstrument.TAInstrumentsITCLowVolume => "ta-itc-low-volume",
            _ => throw new NotSupportedException("Unsupported instrument value."),
        };
        static string FeedbackId(FeedbackMode value) => value switch
        { FeedbackMode.Null => "unknown", FeedbackMode.None => "none", FeedbackMode.Low => "low", FeedbackMode.High => "high", _ => throw new NotSupportedException() };
        static string HeatDirectionId(PeakHeatDirection value) => value switch
        { PeakHeatDirection.Unknown => "unknown", PeakHeatDirection.Exothermal => "exothermal", PeakHeatDirection.Endothermal => "endothermal", PeakHeatDirection.Both => "both", _ => throw new NotSupportedException() };
        static string ProcessorTypeId(BaselineInterpolatorTypes value) => value switch
        { BaselineInterpolatorTypes.None => "none", BaselineInterpolatorTypes.Spline => "spline", BaselineInterpolatorTypes.ASL => "asl", BaselineInterpolatorTypes.Polynomial => "polynomial", BaselineInterpolatorTypes.Segmented => "segmented", _ => throw new NotSupportedException() };
        static string SplineAlgorithmId(SplineInterpolator.SplineInterpolatorAlgorithm value) => value switch
        { SplineInterpolator.SplineInterpolatorAlgorithm.Smooth => "smooth", SplineInterpolator.SplineInterpolatorAlgorithm.Handles => "handles", SplineInterpolator.SplineInterpolatorAlgorithm.Rigid => "rigid", SplineInterpolator.SplineInterpolatorAlgorithm.Linear => "linear", _ => throw new NotSupportedException() };
        static string SplineDensityId(SplineInterpolator.SplinePointDensity value) => value switch
        { SplineInterpolator.SplinePointDensity.Sparse => "sparse", SplineInterpolator.SplinePointDensity.Balanced => "balanced", SplineInterpolator.SplinePointDensity.Dense => "dense", _ => throw new NotSupportedException() };
        static string SplineHandleId(SplineInterpolator.SplineHandleMode value) => value switch
        { SplineInterpolator.SplineHandleMode.Mean => "mean", SplineInterpolator.SplineHandleMode.Median => "median", SplineInterpolator.SplineHandleMode.MinVolatility => "minimum-volatility", _ => throw new NotSupportedException() };
        static string ErrorMethodId(ErrorEstimationMethod value) => value switch
        { ErrorEstimationMethod.None => "none", ErrorEstimationMethod.BootstrapResiduals => "bootstrap-residuals", ErrorEstimationMethod.LeaveOneOut => "leave-one-out", _ => throw new NotSupportedException() };
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
        static string ErrorOutcomeId(ErrorEstimationOutcome value) => value switch
        {
            ErrorEstimationOutcome.None => "none", ErrorEstimationOutcome.NotRun => "not-run", ErrorEstimationOutcome.Completed => "completed",
            ErrorEstimationOutcome.PartialFailure => "partial-failure", ErrorEstimationOutcome.CompleteFailure => "complete-failure",
            ErrorEstimationOutcome.Cancelled => "cancelled", _ => throw new NotSupportedException(),
        };
    }
}
