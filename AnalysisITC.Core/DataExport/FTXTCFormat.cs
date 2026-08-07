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
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
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
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
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

    internal interface IFtxtcMigrator
    {
        int SourceMajor { get; }
        int SourceMinor { get; }
        int TargetMajor { get; }
        int TargetMinor { get; }
        void Migrate(FtxtcManifest manifest, IDictionary<string, byte[]> entries);
    }

    internal static class FtxtcMigrationPipeline
    {
        // Add one narrowly scoped, deterministic migrator for each schema step.
        // Schema 1.0 is the first public version, so the registry is initially empty.
        static readonly IReadOnlyList<IFtxtcMigrator> Migrators = Array.Empty<IFtxtcMigrator>();

        internal static void MigrateToCurrent(FtxtcManifest manifest, IDictionary<string, byte[]> entries)
        {
            if (manifest.SchemaMajor < 0 || manifest.SchemaMinor < 0)
                throw new InvalidDataException("FTXTC manifest has an invalid schema version.");
            if (manifest.SchemaMajor > FTXTCFormat.SchemaMajor
                || manifest.SchemaMajor == FTXTCFormat.SchemaMajor && manifest.SchemaMinor > FTXTCFormat.SchemaMinor)
                throw new NotSupportedException($"FTXTC schema {manifest.SchemaMajor}.{manifest.SchemaMinor} is newer than this application supports.");

            while (manifest.SchemaMajor != FTXTCFormat.SchemaMajor || manifest.SchemaMinor != FTXTCFormat.SchemaMinor)
            {
                var migrator = Migrators.FirstOrDefault(item =>
                    item.SourceMajor == manifest.SchemaMajor && item.SourceMinor == manifest.SchemaMinor);
                if (migrator == null)
                    throw new NotSupportedException($"No FTXTC migrator is available for schema {manifest.SchemaMajor}.{manifest.SchemaMinor}.");
                migrator.Migrate(manifest, entries);
                manifest.SchemaMajor = migrator.TargetMajor;
                manifest.SchemaMinor = migrator.TargetMinor;
            }
        }
    }

    internal sealed class FtxtcProject
    {
        public FtxtcSemanticGraph SemanticGraph { get; set; }
        public List<FtxtcExperimentReference> Experiments { get; set; } = new List<FtxtcExperimentReference>();
        public List<FtxtcResultReference> Results { get; set; } = new List<FtxtcResultReference>();
    }

    internal sealed class FtxtcSemanticGraph
    {
        public string Encoding { get; set; } = "ftitc-semantic-v1";
        public string PayloadBase64 { get; set; }
    }

    internal sealed class FtxtcExperimentReference
    {
        public string Id { get; set; }
        public string Metadata { get; set; }
        public string Thermogram { get; set; }
        public string Baseline { get; set; }
        public string CorrectedPower { get; set; }
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
        public bool ProcessorLocked { get; set; }
        public bool BaselineCompleted { get; set; }
        public bool DiscardIntegratedPoints { get; set; }
        public InjectionData.IntegrationLengthMode IntegrationLengthMode { get; set; }
        public float IntegrationLengthFactor { get; set; }
        public List<FtxtcInjectionState> Injections { get; set; } = new List<FtxtcInjectionState>();
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
        public PeakHeatDirection HeatDirection { get; set; }
        public FtxtcFloatWithError RawPeakArea { get; set; }
        public FtxtcFloatWithError CorrectedPeakArea { get; set; }
    }

    internal sealed class FtxtcFloatWithError
    {
        public double Value { get; set; }
        public double StandardDeviation { get; set; }
        public double Lower95 { get; set; }
        public double Upper95 { get; set; }

        public static FtxtcFloatWithError Capture(FloatWithError value) => new FtxtcFloatWithError
        {
            Value = value.Value,
            StandardDeviation = value.SD,
            Lower95 = value.Lower,
            Upper95 = value.Upper
        };

        public FloatWithError Restore() => new FloatWithError(Value, StandardDeviation, Lower95, Upper95);
    }

    internal sealed class FtxtcResultState
    {
        public string Id { get; set; }
        public string FileName { get; set; }
        public string Name { get; set; }
        public DateTime Date { get; set; }
        public string Comments { get; set; }
        public List<string> ExperimentIds { get; set; } = new List<string>();
    }

    internal enum FtxbScalarType : byte
    {
        Float32 = 1,
        Float64 = 2
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

            using (var semantic = new MemoryStream())
            {
                await FTITCWriter.WriteStream(semantic, experimentList, resultList);
                project.SemanticGraph = new FtxtcSemanticGraph
                {
                    PayloadBase64 = Convert.ToBase64String(StripThermogramLists(semantic.ToArray()))
                };
            }

            for (var index = 0; index < experimentList.Count; index++)
            {
                var experiment = experimentList[index];
                var prefix = $"experiments/{index:D6}";
                var metadataPath = prefix + ".json";
                var thermogramPath = prefix + "-thermogram.ftxb";
                var baselinePath = prefix + "-baseline.ftxb";
                var correctedPath = prefix + "-corrected-power.ftxb";

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
                    CorrectedPower = correctedPath
                });
            }

            for (var index = 0; index < resultList.Count; index++)
            {
                var result = resultList[index];
                var metadataPath = $"results/{index:D6}.json";
                var state = new FtxtcResultState
                {
                    Id = result.UniqueID,
                    FileName = result.FileName,
                    Name = result.Name,
                    Date = result.Date,
                    Comments = result.Comments,
                    ExperimentIds = result.Solution.Solutions.Select(solution => solution.Data.UniqueID).ToList()
                };
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
            ProcessorLocked = experiment.Processor?.IsLocked == true,
            BaselineCompleted = experiment.Processor?.BaselineCompleted == true,
            DiscardIntegratedPoints = experiment.Processor?.DiscardIntegratedPoints == true,
            IntegrationLengthMode = experiment.Processor?.IntegrationLengthMode ?? InjectionData.IntegrationLengthMode.Time,
            IntegrationLengthFactor = experiment.Processor?.IntegrationLengthFactor ?? 2,
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
                HeatDirection = injection.HeatDirection,
                RawPeakArea = FtxtcFloatWithError.Capture(injection.RawPeakArea),
                CorrectedPeakArea = FtxtcFloatWithError.Capture(injection.PeakArea)
            }).ToList()
        };

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

        static byte[] StripThermogramLists(byte[] semanticState)
        {
            var lines = Encoding.UTF8.GetString(semanticState).Replace("\r\n", "\n").Split('\n');
            var output = new StringBuilder(semanticState.Length / 4);
            var skippingDataPoints = false;
            foreach (var line in lines)
            {
                if (!skippingDataPoints && line == "LIST:DataPointList")
                {
                    output.AppendLine(line);
                    skippingDataPoints = true;
                    continue;
                }
                if (skippingDataPoints)
                {
                    if (line == "ENDLIST")
                    {
                        output.AppendLine(line);
                        skippingDataPoints = false;
                    }
                    continue;
                }
                output.AppendLine(line);
            }
            if (skippingDataPoints) throw new InvalidDataException("Could not create the FTXTC semantic graph because a data-point list was not closed.");
            return Encoding.UTF8.GetBytes(output.ToString());
        }
    }
}
