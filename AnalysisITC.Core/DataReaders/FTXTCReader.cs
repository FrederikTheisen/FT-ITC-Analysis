using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.DataReaders
{
    public static class FTXTCReader
    {
        public static async Task<ITCDataContainer[]> ReadPath(string path)
        {
            using var stream = File.OpenRead(path);
            var result = await ReadStream(stream, interactive: true);
            FTITCFormat.CurrentAccessedAppDocumentPath = path;
            return result;
        }

        internal static async Task<ITCDataContainer[]> ReadStream(Stream stream, bool interactive = false)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            // Phase one is deliberately side-effect free: read, constrain and authenticate
            // the complete package before the semantic graph is reconstructed.
            var entries = ReadAndValidateEntries(stream);
            var project = ReadProject(entries);
            ValidateProjectReferences(project, entries);

            ITCDataContainer[] containers;
            using (var semanticStream = new MemoryStream(DecodeSemanticGraph(project), writable: false))
                containers = await FTITCReader.ReadStream(semanticStream, interactive, processProcessorData: false);

            // Phase two applies authoritative exact-state payloads only to the detached
            // objects returned above. DataManager is updated by DataReader after success.
            RestoreExactExperimentState(project, entries, containers);
            return containers;
        }

        static Dictionary<string, byte[]> ReadAndValidateEntries(Stream stream)
        {
            var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                if (archive.Entries.Count > FTXTCFormat.MaxEntries)
                    throw new InvalidDataException("FTXTC package contains too many entries.");

                long totalLength = 0;
                foreach (var entry in archive.Entries)
                {
                    var path = FTXTCFormat.NormalizeEntryPath(entry.FullName);
                    if (entry.Length > FTXTCFormat.MaxEntryBytes)
                        throw new InvalidDataException($"FTXTC entry '{path}' exceeds the size limit.");
                    if (entry.Length > 1024 * 1024 && entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > 200)
                        throw new InvalidDataException($"FTXTC entry '{path}' exceeds the compression-ratio limit.");
                    if (entries.ContainsKey(path))
                        throw new InvalidDataException($"FTXTC package contains duplicate entry '{path}'.");

                    using var source = entry.Open();
                    using var destination = new MemoryStream(entry.Length > int.MaxValue ? 0 : (int)entry.Length);
                    var buffer = new byte[81920];
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (destination.Length + read > FTXTCFormat.MaxEntryBytes)
                            throw new InvalidDataException($"FTXTC entry '{path}' exceeds the size limit while decompressing.");
                        destination.Write(buffer, 0, read);
                    }
                    totalLength = checked(totalLength + destination.Length);
                    if (totalLength > FTXTCFormat.MaxPackageBytes)
                        throw new InvalidDataException("FTXTC package exceeds the uncompressed size limit.");
                    entries.Add(path, destination.ToArray());
                }
            }

            if (!entries.TryGetValue(FTXTCFormat.ManifestPath, out var manifestBytes))
                throw new InvalidDataException("FTXTC package is missing manifest.json.");
            var manifest = FTXTCFormat.ReadJson<FtxtcManifest>(manifestBytes, FTXTCFormat.ManifestPath);
            if (!string.Equals(manifest.Format, FTXTCFormat.FormatName, StringComparison.Ordinal))
                throw new InvalidDataException("The ZIP package is not an FTXTC project.");
            if (!string.Equals(manifest.Root, FTXTCFormat.ProjectPath, StringComparison.Ordinal))
                throw new InvalidDataException("FTXTC manifest has an unsupported root document.");

            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in manifest.Entries ?? new List<FtxtcManifestEntry>())
            {
                var path = FTXTCFormat.NormalizeEntryPath(item.Path);
                if (path == FTXTCFormat.ManifestPath || !declared.Add(path))
                    throw new InvalidDataException($"FTXTC manifest declares duplicate entry '{path}'.");
                if (!entries.TryGetValue(path, out var bytes))
                    throw new InvalidDataException($"FTXTC package is missing declared entry '{path}'.");
                if (bytes.LongLength != item.Length)
                    throw new InvalidDataException($"FTXTC entry '{path}' has the wrong length.");
                if (!string.Equals(FTXTCFormat.Sha256(bytes), item.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"FTXTC entry '{path}' failed its SHA-256 checksum.");
            }

            var actual = new HashSet<string>(entries.Keys.Where(path => path != FTXTCFormat.ManifestPath), StringComparer.Ordinal);
            if (!actual.SetEquals(declared))
            {
                var extra = actual.Except(declared).FirstOrDefault();
                throw new InvalidDataException($"FTXTC package contains undeclared entry '{extra}'.");
            }
            FtxtcMigrationPipeline.MigrateToCurrent(manifest, entries);
            return entries;
        }

        static FtxtcProject ReadProject(Dictionary<string, byte[]> entries)
        {
            if (!entries.TryGetValue(FTXTCFormat.ProjectPath, out var bytes))
                throw new InvalidDataException("FTXTC package is missing project.json.");
            return FTXTCFormat.ReadJson<FtxtcProject>(bytes, FTXTCFormat.ProjectPath);
        }

        static void ValidateProjectReferences(FtxtcProject project, Dictionary<string, byte[]> entries)
        {
            if (project == null) throw new InvalidDataException("FTXTC root project is missing.");
            var referencedEntries = new HashSet<string>(StringComparer.Ordinal) { FTXTCFormat.ProjectPath };
            _ = DecodeSemanticGraph(project);
            var experimentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var experiment in project.Experiments ?? new List<FtxtcExperimentReference>())
            {
                if (string.IsNullOrWhiteSpace(experiment.Id) || !experimentIds.Add(experiment.Id))
                    throw new InvalidDataException("FTXTC project contains an empty or duplicate experiment id.");
                Require(experiment.Metadata, entries, "experiment metadata");
                Require(experiment.Thermogram, entries, "thermogram");
                Require(experiment.Baseline, entries, "baseline");
                Require(experiment.CorrectedPower, entries, "corrected trace");
                referencedEntries.Add(FTXTCFormat.NormalizeEntryPath(experiment.Metadata));
                referencedEntries.Add(FTXTCFormat.NormalizeEntryPath(experiment.Thermogram));
                referencedEntries.Add(FTXTCFormat.NormalizeEntryPath(experiment.Baseline));
                referencedEntries.Add(FTXTCFormat.NormalizeEntryPath(experiment.CorrectedPower));
            }
            var resultIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var result in project.Results ?? new List<FtxtcResultReference>())
            {
                if (string.IsNullOrWhiteSpace(result.Id) || !resultIds.Add(result.Id))
                    throw new InvalidDataException("FTXTC project contains an empty or duplicate result id.");
                if (experimentIds.Contains(result.Id))
                    throw new InvalidDataException($"FTXTC project reuses id '{result.Id}' for an experiment and a result.");
                Require(result.Metadata, entries, "result metadata");
                referencedEntries.Add(FTXTCFormat.NormalizeEntryPath(result.Metadata));
                var resultState = FTXTCFormat.ReadJson<FtxtcResultState>(entries[result.Metadata], result.Metadata);
                if (resultState.Id != result.Id || resultState.ExperimentIds.Any(id => !experimentIds.Contains(id)))
                    throw new InvalidDataException($"FTXTC result '{result.Id}' has an invalid reference.");
            }
            var actualPayloadEntries = new HashSet<string>(entries.Keys.Where(path => path != FTXTCFormat.ManifestPath), StringComparer.Ordinal);
            if (!actualPayloadEntries.SetEquals(referencedEntries))
                throw new InvalidDataException("FTXTC project contains payload entries that are not referenced by project.json.");
        }

        static void Require(string path, IReadOnlyDictionary<string, byte[]> entries, string purpose)
        {
            var normalized = FTXTCFormat.NormalizeEntryPath(path);
            if (!entries.ContainsKey(normalized)) throw new InvalidDataException($"FTXTC project references missing {purpose} entry '{normalized}'.");
        }

        static byte[] DecodeSemanticGraph(FtxtcProject project)
        {
            if (project?.SemanticGraph == null
                || !string.Equals(project.SemanticGraph.Encoding, "ftitc-semantic-v1", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(project.SemanticGraph.PayloadBase64))
                throw new InvalidDataException("FTXTC project is missing a supported semantic graph.");
            try
            {
                return Convert.FromBase64String(project.SemanticGraph.PayloadBase64);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("FTXTC semantic graph is not valid base64.", ex);
            }
        }

        static void RestoreExactExperimentState(
            FtxtcProject project,
            IReadOnlyDictionary<string, byte[]> entries,
            IReadOnlyCollection<ITCDataContainer> containers)
        {
            var experiments = containers.OfType<ExperimentData>().ToDictionary(item => item.UniqueID, StringComparer.Ordinal);
            if (experiments.Count != project.Experiments.Count)
                throw new InvalidDataException("FTXTC semantic state does not match its experiment index.");

            foreach (var reference in project.Experiments)
            {
                if (!experiments.TryGetValue(reference.Id, out var experiment))
                    throw new InvalidDataException($"FTXTC semantic state is missing experiment '{reference.Id}'.");
                var metadata = FTXTCFormat.ReadJson<FtxtcExperimentState>(entries[reference.Metadata], reference.Metadata);
                if (metadata.Id != reference.Id)
                    throw new InvalidDataException($"FTXTC experiment metadata id does not match '{reference.Id}'.");

                experiment.DataPoints = RestoreDataPoints(entries[reference.Thermogram], reference.Thermogram);
                experiment.BaseLineCorrectedDataPoints = RestoreDataPoints(entries[reference.CorrectedPower], reference.CorrectedPower);
                var baseline = FtxbCodec.DecodeFloat64(entries[reference.Baseline], reference.Baseline);
                if (experiment.Processor?.Interpolator == null && baseline.GetLength(0) != 0)
                    throw new InvalidDataException($"FTXTC experiment '{reference.Id}' has baseline data but no interpolator.");
                if (experiment.Processor?.Interpolator != null)
                {
                    experiment.Processor.Interpolator.Baseline = Enumerable.Range(0, baseline.GetLength(0))
                        .Select(row => new Energy(new FloatWithError(baseline[row, 0], baseline[row, 1], baseline[row, 2], baseline[row, 3])))
                        .ToList();
                    experiment.Processor.BaselineCompleted = metadata.BaselineCompleted;
                    if (metadata.ProcessorLocked && !experiment.Processor.IsLocked) experiment.Processor.Lock();
                }
                if (experiment.Processor != null)
                {
                    experiment.Processor.DiscardIntegratedPoints = metadata.DiscardIntegratedPoints;
                    experiment.Processor.IntegrationLengthMode = metadata.IntegrationLengthMode;
                    experiment.Processor.IntegrationLengthFactor = metadata.IntegrationLengthFactor;
                    experiment.Processor.BaselineCompleted = metadata.BaselineCompleted;
                    if (metadata.ProcessorLocked && !experiment.Processor.IsLocked) experiment.Processor.Lock();
                }

                if (experiment.Injections.Count != metadata.Injections.Count)
                    throw new InvalidDataException($"FTXTC injection count does not match for experiment '{reference.Id}'.");
                for (var index = 0; index < experiment.Injections.Count; index++)
                {
                    var injection = experiment.Injections[index];
                    var state = metadata.Injections[index];
                    if (injection.ID != state.Id)
                        throw new InvalidDataException($"FTXTC injection ids do not match for experiment '{reference.Id}'.");
                    injection.RestoreState(
                        state.Included, state.Time, state.Volume, state.Delay, state.Duration, state.Filter,
                        state.Temperature, state.IntegrationStartDelay, state.IntegrationEndOffset,
                        state.ActualCellConcentration, state.ActualTitrantConcentration, state.Ratio,
                        state.IsIntegrated, state.HeatDirection,
                        state.RawPeakArea.Restore(), state.CorrectedPeakArea.Restore());
                }
            }
        }

        static List<DataPoint> RestoreDataPoints(byte[] bytes, string path)
        {
            var values = FtxbCodec.DecodeFloat32(bytes, path);
            if (values.GetLength(1) != 7)
                throw new InvalidDataException($"FTXTC data-point entry '{path}' must have seven columns.");
            return Enumerable.Range(0, values.GetLength(0)).Select(row => new DataPoint(
                values[row, 0], values[row, 1], values[row, 2], values[row, 3],
                values[row, 4], values[row, 5], values[row, 6])).ToList();
        }
    }

    internal static class ProjectDocumentState
    {
        static string path = "";

        internal static event EventHandler PathChanged;
        internal static ITCDataFormat Format { get; private set; } = ITCDataFormat.FTXTC;
        internal static string Path
        {
            get => path;
            set
            {
                var next = value ?? "";
                if (path == next) return;
                path = next;
                var extension = System.IO.Path.GetExtension(next);
                Format = string.Equals(extension, ".ftitc", StringComparison.OrdinalIgnoreCase)
                    ? ITCDataFormat.FTITC
                    : ITCDataFormat.FTXTC;
                PathChanged?.Invoke(null, EventArgs.Empty);
            }
        }
    }
}
