using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.DataReaders
{
    public enum FtxtcReadPolicy { Strict, RecoverUsableContent }
    public enum FtxtcIssueSeverity { Warning, Error }

    public sealed class FtxtcRecoveryIssue
    {
        public string Code { get; set; }
        public FtxtcIssueSeverity Severity { get; set; }
        public string ComponentId { get; set; }
        public string EntryPath { get; set; }
        public string Message { get; set; }
    }

    public sealed class FtxtcReadResult
    {
        public ITCDataContainer[] Containers { get; internal set; } = Array.Empty<ITCDataContainer>();
        public IReadOnlyList<FtxtcRecoveryIssue> Issues { get; internal set; } = Array.Empty<FtxtcRecoveryIssue>();
        public int SchemaMajor { get; internal set; }
        public int SchemaMinor { get; internal set; }
        public bool IsPartial => Issues.Count != 0;
    }

    internal sealed class FtxtcEntryStore : IReadOnlyDictionary<string, byte[]>, IDisposable
    {
        readonly string directory;
        readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal);

        internal int SchemaMajor { get; set; }
        internal int SchemaMinor { get; set; }

        internal FtxtcEntryStore()
        {
            directory = Path.Combine(Path.GetTempPath(), "ftxtc-read-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        internal Stream Create(string path)
        {
            if (files.ContainsKey(path)) throw new InvalidDataException($"FTXTC package contains duplicate entry '{path}'.");
            var file = Path.Combine(directory, files.Count.ToString("D8") + ".entry");
            files.Add(path, file);
            return new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }

        internal bool Remove(string path)
        {
            if (!files.TryGetValue(path, out var file)) return false;
            files.Remove(path);
            if (File.Exists(file)) File.Delete(file);
            return true;
        }

        internal long Length(string path) => new FileInfo(files[path]).Length;
        internal string Sha256(string path)
        {
            using var input = File.OpenRead(files[path]);
            using var sha = System.Security.Cryptography.SHA256.Create();
            return string.Concat(sha.ComputeHash(input).Select(value => value.ToString("x2")));
        }

        public byte[] this[string key] => File.ReadAllBytes(files[key]);
        public IEnumerable<string> Keys => files.Keys;
        public IEnumerable<byte[]> Values => files.Keys.Select(key => this[key]);
        public int Count => files.Count;
        public bool ContainsKey(string key) => files.ContainsKey(key);
        public bool TryGetValue(string key, out byte[] value)
        {
            if (!files.ContainsKey(key)) { value = null; return false; }
            value = this[key]; return true;
        }
        public IEnumerator<KeyValuePair<string, byte[]>> GetEnumerator() =>
            files.Keys.Select(key => new KeyValuePair<string, byte[]>(key, this[key])).GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch { /* Best-effort cleanup of a private temporary directory. */ }
        }
    }

    public static class FTXTCReader
    {
        public static IReadOnlyList<FtxtcRecoveryIssue> LastRecoveryIssues { get; private set; } = Array.Empty<FtxtcRecoveryIssue>();

        public static async Task<ITCDataContainer[]> ReadPath(string path)
        {
            using var stream = File.OpenRead(path);
            var result = await ReadWithRecovery(stream, FtxtcReadPolicy.RecoverUsableContent, interactive: true);
            LastRecoveryIssues = result.Issues;
            foreach (var issue in result.Issues)
                AppEventHandler.PrintAndLog($"FTXTC recovery [{issue.Severity}/{issue.Code}] {issue.ComponentId}: {issue.Message}");
            FTITCFormat.CurrentAccessedAppDocumentPath = result.IsPartial ? "" : path;
            if (result.IsPartial) DocumentDirtyTracker.MarkDirty();
            return result.Containers;
        }

        internal static async Task<ITCDataContainer[]> ReadStream(Stream stream, bool interactive = false) =>
            (await ReadWithRecovery(stream, FtxtcReadPolicy.Strict, interactive)).Containers;

        public static Task<FtxtcReadResult> ReadWithRecovery(Stream stream, FtxtcReadPolicy policy, bool interactive = false)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            using var restoreScope = DocumentDirtyTracker.RestoreDocument();
            var issues = new List<FtxtcRecoveryIssue>();
            using var entries = ReadAndValidateEntries(stream, policy, issues);
            var project = ReadProject(entries);
            ValidateRootReferences(project);

            var experiments = RestoreExperiments(project, entries, entries.SchemaMinor, policy, issues);
            RestoreBufferReferences(experiments, policy, issues);
            var solutions = RestoreSolutions(project, entries, experiments, entries.SchemaMinor, policy, issues);
            foreach (var reference in project.Experiments)
            {
                if (string.IsNullOrWhiteSpace(reference.Id) || !experiments.TryGetValue(reference.Id, out var experiment)) continue;
                var state = TryRead<FtxtcExperimentState>(entries, reference.Metadata, reference.Id, policy, issues, "experiment-metadata");
                if (state != null && !string.IsNullOrWhiteSpace(state.AttachedSolutionId)
                    && solutions.TryGetValue(state.AttachedSolutionId, out var solution))
                    experiment.UpdateSolution(solution.Model);
            }
            var results = RestoreResults(project, entries, solutions, entries.SchemaMinor, policy, issues);

            var containers = experiments.Values.Cast<ITCDataContainer>().Concat(results).ToArray();
            foreach (var container in containers) container.MarkClean();
            return Task.FromResult(new FtxtcReadResult
            {
                Containers = containers,
                Issues = issues,
                SchemaMajor = entries.SchemaMajor,
                SchemaMinor = entries.SchemaMinor,
            });
        }

        static FtxtcEntryStore ReadAndValidateEntries(Stream stream, FtxtcReadPolicy policy, List<FtxtcRecoveryIssue> issues)
        {
            // Pass one constrains paths and sizes and authenticates all declared
            // entries. Pass two (the restore methods below) parses entries on demand.
            // Validated expanded entries are spooled to a private temporary store,
            // so validation never retains the complete package in memory.
            var entries = new FtxtcEntryStore();
            try
            {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > FTXTCFormat.MaxEntries) throw new InvalidDataException("FTXTC package contains too many entries.");
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                var path = FTXTCFormat.NormalizeEntryPath(entry.FullName);
                if (entries.ContainsKey(path)) throw new InvalidDataException($"FTXTC package contains duplicate entry '{path}'.");
                if (entry.Length > FTXTCFormat.MaxEntryBytes) throw new InvalidDataException($"FTXTC entry '{path}' exceeds the size limit.");
                if (entry.Length > 1024 * 1024 && entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > 200)
                    throw new InvalidDataException($"FTXTC entry '{path}' exceeds the compression-ratio limit.");
                using var input = entry.Open();
                using var output = entries.Create(path);
                var buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Position + read > FTXTCFormat.MaxEntryBytes) throw new InvalidDataException($"FTXTC entry '{path}' exceeds the size limit while decompressing.");
                    output.Write(buffer, 0, read);
                }
                total = checked(total + output.Position);
                if (total > FTXTCFormat.MaxPackageBytes) throw new InvalidDataException("FTXTC package exceeds the uncompressed size limit.");
            }
            if (!entries.TryGetValue(FTXTCFormat.ManifestPath, out var manifestBytes)) throw new InvalidDataException("FTXTC package is missing manifest.json.");
            var manifest = FTXTCFormat.ReadJson<FtxtcManifest>(manifestBytes, FTXTCFormat.ManifestPath);
            if (manifest.Format != FTXTCFormat.FormatName || manifest.Root != FTXTCFormat.ProjectPath) throw new InvalidDataException("The ZIP package is not a supported FTXTC project.");
            if (manifest.SchemaMajor > FTXTCFormat.SchemaMajor || manifest.SchemaMajor == FTXTCFormat.SchemaMajor && manifest.SchemaMinor > FTXTCFormat.SchemaMinor)
                throw new NotSupportedException($"FTXTC schema {manifest.SchemaMajor}.{manifest.SchemaMinor} is newer than this application supports.");
            if (manifest.SchemaMajor != FTXTCFormat.SchemaMajor || manifest.SchemaMinor < 0)
                throw new NotSupportedException($"No FTXTC migrator is available for schema {manifest.SchemaMajor}.{manifest.SchemaMinor}.");
            entries.SchemaMajor = manifest.SchemaMajor;
            entries.SchemaMinor = manifest.SchemaMinor;

            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in manifest.Entries ?? new List<FtxtcManifestEntry>())
            {
                var path = FTXTCFormat.NormalizeEntryPath(item.Path);
                if (!declared.Add(path) || path == FTXTCFormat.ManifestPath) throw new InvalidDataException($"FTXTC manifest declares duplicate entry '{path}'.");
                if (!entries.ContainsKey(path))
                {
                    if (path == FTXTCFormat.ProjectPath || policy == FtxtcReadPolicy.Strict) throw new InvalidDataException($"FTXTC package is missing declared entry '{path}'.");
                    issues.Add(Issue("missing-entry", null, path, $"Declared entry '{path}' is missing."));
                    continue;
                }
                var valid = entries.Length(path) == item.Length
                    && string.Equals(entries.Sha256(path), item.Sha256, StringComparison.OrdinalIgnoreCase);
                if (!valid)
                {
                    if (path == FTXTCFormat.ProjectPath || policy == FtxtcReadPolicy.Strict) throw new InvalidDataException($"FTXTC entry '{path}' failed its length or SHA-256 check.");
                    entries.Remove(path);
                    issues.Add(Issue("checksum-failure", null, path, $"Entry '{path}' failed authentication and was omitted."));
                }
            }
            foreach (var path in entries.Keys.Where(path => path != FTXTCFormat.ManifestPath && !declared.Contains(path)).ToList())
            {
                if (policy == FtxtcReadPolicy.Strict) throw new InvalidDataException($"FTXTC package contains undeclared entry '{path}'.");
                entries.Remove(path);
                issues.Add(Issue("undeclared-entry", null, path, $"Undeclared safe entry '{path}' was ignored.", FtxtcIssueSeverity.Warning));
            }
            return entries;
            }
            catch
            {
                entries.Dispose();
                throw;
            }
        }

        static FtxtcProject ReadProject(IReadOnlyDictionary<string, byte[]> entries)
        {
            if (!entries.TryGetValue(FTXTCFormat.ProjectPath, out var bytes)) throw new InvalidDataException("FTXTC package is missing project.json.");
            var store = entries as FtxtcEntryStore
                ?? throw new InvalidDataException("FTXTC entry store does not expose its package schema.");
            return FtxtcStorageMigrationPipeline.MigrateToCurrent(
                FTXTCFormat.ReadJson<FtxtcProject>(bytes, FTXTCFormat.ProjectPath), store.SchemaMinor);
        }

        static void ValidateRootReferences(FtxtcProject project)
        {
            if (project == null) throw new InvalidDataException("FTXTC root project is missing.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in project.Experiments.Select(value => value.Id).Concat(project.Solutions.Select(value => value.Id)).Concat(project.Results.Select(value => value.Id)))
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new InvalidDataException($"FTXTC project contains an empty or ambiguous root id '{id}'.");
            var experimentIds = new HashSet<string>(project.Experiments.Select(value => value.Id), StringComparer.Ordinal);
            if (project.Solutions.Any(value => !experimentIds.Contains(value.ExperimentId))) throw new InvalidDataException("FTXTC project contains an ambiguous solution-to-experiment reference.");
        }

        static Dictionary<string, ExperimentData> RestoreExperiments(FtxtcProject project, IReadOnlyDictionary<string, byte[]> entries,
            int packageSchemaMinor, FtxtcReadPolicy policy, List<FtxtcRecoveryIssue> issues)
        {
            var result = new Dictionary<string, ExperimentData>(StringComparer.Ordinal);
            foreach (var reference in project.Experiments)
            {
                var state = TryRead<FtxtcExperimentState>(entries, reference.Metadata, reference.Id, policy, issues, "experiment-metadata");
                if (state == null) continue;
                try
                {
                    if (state.Id != reference.Id) throw new InvalidDataException("Experiment metadata id does not match project.json.");
                    var experiment = new ExperimentData(state.FileName ?? string.Empty);
                    experiment.SetID(state.Id); experiment.Name = state.Name; experiment.SetDate(state.Date); experiment.Comments = state.Comments;
                    experiment.DateSource = ParseDateSource(state.DateSource);
                    experiment.DataSourceFormat = ParseDataFormat(state.SourceFormat); experiment.Instrument = ParseInstrument(state.Instrument);
                    experiment.CellConcentration = state.CellConcentration?.Restore() ?? new FloatWithError(double.NaN);
                    experiment.SyringeConcentration = state.SyringeConcentration?.Restore() ?? new FloatWithError(double.NaN);
                    experiment.CellVolume = state.CellVolume; experiment.StirringSpeed = state.StirringSpeed;
                    experiment.FeedBackMode = ParseFeedback(state.FeedbackMode); experiment.TargetTemperature = state.TargetTemperature;
                    experiment.MeasuredTemperature = state.MeasuredTemperature; experiment.InitialDelay = state.InitialDelay;
                    experiment.TargetPowerDiff = state.TargetPowerDifference; experiment.AverageHeatDirection = ParseHeatDirection(state.AverageHeatDirection);
                    experiment.Attributes.AddRange(state.Attributes.Select(value => RestoreAttribute(value, packageSchemaMinor)));
                    experiment.ReplaceSegments(state.Segments.Select(segment => new TandemExperimentSegment(segment.FirstInjectionId, segment.InitialCellConcentration, segment.InitialTitrantConcentration)));
                    experiment.Injections = state.Injections.Select(injection => RestoreInjection(experiment, injection)).ToList();
                    experiment.Include = state.Included;

                    if (entries.TryGetValue(reference.Thermogram, out var thermogram))
                    {
                        try { experiment.DataPoints = RestoreDataPoints(thermogram, reference.Thermogram, packageSchemaMinor); }
                        catch (Exception ex)
                        {
                            if (policy == FtxtcReadPolicy.Strict) throw;
                            experiment.DataPoints = new List<DataPoint>();
                            issues.Add(Issue("thermogram-unavailable", reference.Id, reference.Thermogram,
                                "Raw thermogram is corrupt; integrated injection data was retained. " + ex.Message, FtxtcIssueSeverity.Warning));
                        }
                    }
                    else issues.Add(Issue("thermogram-unavailable", reference.Id, reference.Thermogram, "Raw thermogram is unavailable; integrated injection data was retained.", FtxtcIssueSeverity.Warning));
                    RestoreProcessor(experiment, state.Processor);
                    byte[] baselineBytes = null;
                    var hasBaseline = !string.IsNullOrWhiteSpace(reference.Baseline)
                        && entries.TryGetValue(reference.Baseline, out baselineBytes);
                    if (hasBaseline)
                    {
                        try
                        {
                            RestoreBaseline(experiment, baselineBytes, reference.Baseline);
                            RestoreCorrectedTrace(experiment);
                        }
                        catch (Exception ex)
                        {
                            if (policy == FtxtcReadPolicy.Strict) throw;
                            ClearProcessedOutput(experiment);
                            issues.Add(Issue("processed-output-unavailable", reference.Id, reference.Baseline,
                                "The corrected trace could not be reconstructed from the saved baseline. " + ex.Message,
                                FtxtcIssueSeverity.Warning));
                        }
                    }
                    else if (state.Processor != null)
                    {
                        if (policy == FtxtcReadPolicy.Strict)
                            throw new InvalidDataException("The saved baseline is unavailable.");
                        ClearProcessedOutput(experiment);
                        issues.Add(Issue("processed-output-unavailable", reference.Id, reference.Baseline,
                            "The corrected trace could not be reconstructed because the saved baseline is unavailable.", FtxtcIssueSeverity.Warning));
                    }
                    result.Add(reference.Id, experiment);
                }
                catch (Exception ex)
                {
                    if (policy == FtxtcReadPolicy.Strict) throw new InvalidDataException($"Could not restore experiment '{reference.Id}'.", ex);
                    issues.Add(Issue("experiment-skipped", reference.Id, reference.Metadata, ex.Message));
                }
            }
            return result;
        }

        static Dictionary<string, SolutionInterface> RestoreSolutions(FtxtcProject project, IReadOnlyDictionary<string, byte[]> entries,
            IReadOnlyDictionary<string, ExperimentData> experiments, int packageSchemaMinor,
            FtxtcReadPolicy policy, List<FtxtcRecoveryIssue> issues)
        {
            var result = new Dictionary<string, SolutionInterface>(StringComparer.Ordinal);
            foreach (var reference in project.Solutions)
            {
                if (!experiments.TryGetValue(reference.ExperimentId, out var experiment)) continue;
                var state = TryRead<FtxtcSolutionState>(entries, reference.Metadata, reference.Id, policy, issues, "solution-metadata");
                if (state == null) continue;
                try
                {
                    var modelType = FtxtcWireIds.Model(state.ModelId);
                    var expectedModelSchema = modelType == AnalysisModel.SequentialBindingSites ? 2 : 1;
                    if (state.Id != reference.Id || state.ExperimentId != reference.ExperimentId
                        || state.SchemaVersion != 1 || state.ModelSchemaVersion != expectedModelSchema)
                        throw new InvalidDataException("Solution identity or schema is invalid.");
                    var model = FtxtcModelRegistry.Create(state.ModelId, experiment);
                    model.InitializeParameters(experiment);
                    model.ModelCloneOptions = RestoreCloneOptions(state.CloneOptions);

                    var restoredOptions = state.ModelOptions
                        .Select(option => RestoreAttribute(option, packageSchemaMinor)).ToList();
                    if (restoredOptions.GroupBy(option => option.Key).Any(group => group.Count() != 1))
                        throw new InvalidDataException("Solution contains duplicate model options.");
                    foreach (var option in restoredOptions)
                        model.ModelOptions[option.Key] = option;

                    int? sequentialCount = null;
                    if (modelType == AnalysisModel.SequentialBindingSites)
                        sequentialCount = SequentialPersistenceShape.RequireExplicitSiteCount(
                            restoredOptions, "Sequential FTXTC solution");

                    // Apply structural options before installing fitted values so a
                    // dynamic model has its final parameter table first.
                    model.ApplyModelOptions();

                    var fittedParameters = state.FittedParameters.Select(parameter =>
                        new Parameter(FtxtcWireIds.Parameter(parameter.Id), parameter.Value, parameter.Locked)).ToList();
                    var reportedParameters = state.ReportedParameters.Select(parameter =>
                        new KeyValuePair<ParameterType, FloatWithError>(
                            FtxtcWireIds.Parameter(parameter.Id), parameter.Estimate.Restore())).ToList();
                    if (sequentialCount.HasValue)
                    {
                        SequentialPersistenceShape.ValidateFittedParameters(
                            fittedParameters, sequentialCount.Value, "Sequential FTXTC solution");
                        SequentialPersistenceShape.ValidateReportedParameterKeys(
                            reportedParameters.Select(parameter => parameter.Key),
                            sequentialCount.Value, "Sequential FTXTC solution");
                    }
                    foreach (var parameter in fittedParameters)
                        model.Parameters.AddOrUpdateParameter(parameter);

                    var solution = SolutionInterface.FromModel(model, RestoreConvergence(state.Convergence));
                    solution.SetID(state.Id); solution.UseWeightedFitting = state.Weighted; solution.ErrorMethod = ParseErrorMethod(state.ErrorMethod);
                    solution.RestoreParameterBoundaryHit(state.ParameterBoundaryHit);
                    model.Solution = solution;
                    if (!string.IsNullOrWhiteSpace(reference.Bootstrap))
                    {
                        try { solution.RestoreBootstrapSolutions(RestoreBootstrap(reference, solution, entries, packageSchemaMinor)); }
                        catch (Exception ex)
                        {
                            if (policy == FtxtcReadPolicy.Strict) throw;
                            var code = modelType == AnalysisModel.SequentialBindingSites
                                ? "sequential-bootstrap-omitted"
                                : "bootstrap-omitted";
                            issues.Add(Issue(code, reference.Id, reference.Bootstrap, ex.Message, FtxtcIssueSeverity.Warning));
                        }
                    }
                    solution.Parameters.Clear();
                    foreach (var parameter in reportedParameters)
                        solution.Parameters.Add(parameter.Key, parameter.Value);
                    solution.ProfileLikelihoodRun = RestoreProfile(state.Profile);
                    solution.RestoreValidity(state.IsValid);
                    result.Add(reference.Id, solution);
                }
                catch (Exception ex)
                {
                    if (policy == FtxtcReadPolicy.Strict) throw new InvalidDataException($"Could not restore solution '{reference.Id}'.", ex);
                    var sequential = string.Equals(state.ModelId,
                        FtxtcWireIds.Model(AnalysisModel.SequentialBindingSites), StringComparison.Ordinal);
                    issues.Add(Issue(sequential ? "sequential-solution-skipped" : "solution-skipped",
                        reference.Id, reference.Metadata, ex.Message,
                        sequential ? FtxtcIssueSeverity.Warning : FtxtcIssueSeverity.Error));
                }
            }
            return result;
        }

        static List<SolutionInterface> RestoreBootstrap(FtxtcSolutionReference reference, SolutionInterface primary,
            IReadOnlyDictionary<string, byte[]> entries, int packageSchemaMinor)
        {
            if (!entries.TryGetValue(reference.Bootstrap, out var descriptorBytes)) throw new InvalidDataException("Bootstrap descriptor is missing.");
            var state = FTXTCFormat.ReadJson<FtxtcBootstrapState>(descriptorBytes, reference.Bootstrap);
            if (state.SchemaVersion != 1 || state.ReplicateIndices.Count != state.Replicates.Count) throw new InvalidDataException("Bootstrap descriptor has an invalid schema or shape.");
            if (state.ReplicateIndices.Distinct().Count() != state.ReplicateIndices.Count) throw new InvalidDataException("Bootstrap replicate indices must be unique.");
            if (state.ParameterIds.Distinct(StringComparer.Ordinal).Count() != state.ParameterIds.Count)
                throw new InvalidDataException("Bootstrap parameter identifiers must be unique.");
            var parameterKeys = state.ParameterIds.Select(FtxtcWireIds.Parameter).ToList();
            int? sequentialCount = null;
            if (primary.ModelType == AnalysisModel.SequentialBindingSites)
            {
                sequentialCount = SequentialPersistenceShape.RequireExplicitSiteCount(
                    primary.ModelOptions.Values, "Primary sequential FTXTC solution");
                SequentialPersistenceShape.ValidateFittedParameterKeys(
                    parameterKeys, sequentialCount.Value, "Sequential FTXTC bootstrap descriptor");
            }
            var values = FtxbCodec.DecodeFloat64(Require(entries, state.ParameterValues), state.ParameterValues);
            var locks = FtxbCodec.DecodeUInt8(Require(entries, state.ParameterLocks), state.ParameterLocks);
            var injections = FtxbCodec.DecodeFloat64(Require(entries, state.Injections), state.Injections);
            var includes = FtxbCodec.DecodeUInt8(Require(entries, state.InjectionIncludes), state.InjectionIncludes);
            var rows = state.ReplicateIndices.Count;
            if (values.GetLength(0) != rows || values.GetLength(1) != state.ParameterIds.Count
                || locks.GetLength(0) != rows || locks.GetLength(1) != state.ParameterIds.Count
                || injections.GetLength(0) != rows || injections.GetLength(1) != state.InjectionIds.Count * 4
                || includes.GetLength(0) != rows || includes.GetLength(1) != state.InjectionIds.Count)
                throw new InvalidDataException("Bootstrap matrices do not match their declared columns.");
            var restored = new List<SolutionInterface>();
            for (var row = 0; row < rows; row++)
            {
                var descriptor = state.Replicates[row];
                var snapshot = new BootstrapModelSnapshot
                {
                    ReplicateIndex = state.ReplicateIndices[row], CellConcentration = descriptor.CellConcentration.Restore(),
                    SyringeConcentration = descriptor.SyringeConcentration.Restore(), CellVolume = descriptor.CellVolume,
                    MeasuredTemperature = descriptor.MeasuredTemperature,
                    ParameterBoundaryHit = descriptor.ParameterBoundaryHit,
                };
                for (var column = 0; column < state.ParameterIds.Count; column++)
                    snapshot.Parameters.Add(new Parameter(parameterKeys[column], values[row, column], locks[row, column] != 0));
                var restoredOptions = descriptor.ModelOptions
                    .Select(value => RestoreAttribute(value, packageSchemaMinor)).ToList();
                snapshot.ModelOptions.AddRange(restoredOptions);
                if (sequentialCount.HasValue)
                {
                    var snapshotCount = SequentialPersistenceShape.RequireExplicitSiteCount(
                        restoredOptions, $"Sequential FTXTC bootstrap replicate {snapshot.ReplicateIndex}");
                    if (snapshotCount != sequentialCount.Value)
                        throw new InvalidDataException(
                            $"Sequential FTXTC bootstrap replicate {snapshot.ReplicateIndex} declares {snapshotCount} steps; expected {sequentialCount.Value}.");
                }
                for (var column = 0; column < state.InjectionIds.Count; column++) snapshot.Injections.Add(new BootstrapInjectionSnapshot
                {
                    ID = state.InjectionIds[column], Include = includes[row, column] != 0, Volume = injections[row, column * 4],
                    ActualCellConcentration = injections[row, column * 4 + 1], ActualTitrantConcentration = injections[row, column * 4 + 2],
                    Ratio = injections[row, column * 4 + 3],
                });
                snapshot.Segments.AddRange(descriptor.Segments.Select(segment => new BootstrapSegmentSnapshot
                {
                    FirstInjectionID = segment.FirstInjectionId, InitialCellConcentration = segment.InitialCellConcentration,
                    InitialTitrantConcentration = segment.InitialTitrantConcentration,
                }));
                restored.Add(snapshot.Restore(primary.Model));
            }
            return restored.OrderBy(item => item.BootstrapReplicateIndex).ToList();
        }

        static List<AnalysisResult> RestoreResults(FtxtcProject project, IReadOnlyDictionary<string, byte[]> entries,
            IReadOnlyDictionary<string, SolutionInterface> solutions, int packageSchemaMinor,
            FtxtcReadPolicy policy, List<FtxtcRecoveryIssue> issues)
        {
            var result = new List<AnalysisResult>();
            foreach (var reference in project.Results)
            {
                var state = TryRead<FtxtcResultState>(entries, reference.Metadata, reference.Id, policy, issues, "result-metadata");
                if (state == null) continue;
                try
                {
                    if (state.Id != reference.Id || state.MemberSolutionIds.Count == 0
                        || state.MemberSolutionIds.Any(id => !solutions.ContainsKey(id))) throw new InvalidDataException("Result member reference is unavailable.");
                    var members = state.MemberSolutionIds.Select(id => solutions[id]).ToList();
                    var resultModelType = FtxtcWireIds.Model(state.ModelId);
                    if (members.Any(member => member.ModelType != resultModelType))
                        throw new InvalidDataException("Result model id does not match its member solutions.");
                    var constraints = state.Constraints.Select(constraint =>
                        new KeyValuePair<ParameterType, VariableConstraint>(
                            FtxtcWireIds.Parameter(constraint.ParameterId),
                            ParseConstraint(constraint.Constraint))).ToList();
                    var globalParameters = state.GlobalParameters.Select(parameter =>
                        new Parameter(FtxtcWireIds.Parameter(parameter.Id), parameter.Value, parameter.Locked)).ToList();
                    if (resultModelType == AnalysisModel.SequentialBindingSites)
                    {
                        var counts = members.Select(member =>
                            SequentialPersistenceShape.RequireExplicitSiteCount(
                                member.ModelOptions.Values, "Sequential FTXTC global member")).Distinct().ToList();
                        if (counts.Count != 1)
                            throw new InvalidDataException(
                                "Sequential FTXTC global members must declare the same site count.");
                        SequentialPersistenceShape.ValidateGlobalShape(
                            counts[0], constraints, globalParameters.Select(parameter => parameter.Key),
                            "Sequential FTXTC global result");
                    }
                    var model = new GlobalModel(members.Select(member => member.Model).ToList())
                    {
                        ModelCloneOptions = RestoreCloneOptions(state.CloneOptions), Parameters = new GlobalModelParameters(),
                    };
                    foreach (var member in members) model.Parameters.AddIndivdualParameter(member.Model.Parameters);
                    foreach (var constraint in constraints)
                        model.Parameters.SetConstraintForParameter(constraint.Key, constraint.Value);
                    foreach (var parameter in globalParameters)
                        model.Parameters.AddorUpdateGlobalParameter(parameter.Key, parameter.Value, parameter.IsLocked);
                    model.Parameters.SetIndividualFromGlobal();
                    var solver = new GlobalSolver { Model = model, ErrorEstimationMethod = state.CloneOptions == null ? ErrorEstimationMethod.None : ParseErrorMethod(state.CloneOptions.ErrorMethod), UseErrorWeightedFitting = state.Weighted };
                    var global = new GlobalSolution(solver, members, RestoreConvergence(state.Convergence));
                    global.ProfileLikelihoodRun = RestoreProfile(state.Profile);
                    global.ApplyProfileTemperatureCoordinates(global.ProfileLikelihoodRun);
                    global.SetID(state.GlobalSolutionId); global.RestoreValidity(state.IsValid); model.Solution = global;
                    var restored = new AnalysisResult(global, captureValiditySnapshot: false);
                    restored.SetID(state.Id); restored.SetFileName(state.FileName); restored.Name = state.Name; restored.SetDate(state.Date);
                    restored.Comments = state.Comments;
                    restored.SetValiditySnapshot(RestoreValiditySnapshot(state.Validity, packageSchemaMinor));
                    RestoreAdvancedAnalyses(restored, state.AdvancedAnalyses, reference, policy, issues);
                    restored.MarkClean();
                    result.Add(restored);
                }
                catch (Exception ex)
                {
                    if (policy == FtxtcReadPolicy.Strict) throw new InvalidDataException($"Could not restore result '{reference.Id}'.", ex);
                    var sequential = string.Equals(state.ModelId,
                        FtxtcWireIds.Model(AnalysisModel.SequentialBindingSites), StringComparison.Ordinal);
                    issues.Add(Issue(sequential ? "sequential-result-skipped" : "result-skipped",
                        reference.Id, reference.Metadata, ex.Message,
                        sequential ? FtxtcIssueSeverity.Warning : FtxtcIssueSeverity.Error));
                }
            }
            return result;
        }

        static void RestoreAdvancedAnalyses(
            AnalysisResult result,
            FtxtcAdvancedAnalysesState state,
            FtxtcResultReference reference,
            FtxtcReadPolicy policy,
            List<FtxtcRecoveryIssue> issues)
        {
            if (state == null) return;

            RestoreOne("spolar-record", state.SpolarRecord != null, () =>
            {
                var value = state.SpolarRecord;
                if (value.SchemaVersion != 1 || value.CompletedIterations < 0
                    || value.HydrationEntropy == null || value.ConformationalEntropy == null
                    || value.ResidueEstimate == null || value.ReferenceTemperature == null
                    || result.SpolarRecordAnalysis == null)
                    throw new InvalidDataException("The saved Spolar Record analysis has an invalid schema or is unavailable for this result.");
                result.SpolarRecordAnalysis.RestoreResult(
                    ParseSpolarFoldedMode(value.FoldedMode),
                    ParseSpolarTemperatureMode(value.TemperatureMode),
                    new FTSRMethod.SROutput(
                        value.HydrationEntropy.Restore(),
                        value.ConformationalEntropy.Restore(),
                        value.ResidueEstimate.Restore(),
                        value.ReferenceTemperature.Restore()),
                    value.CompletedIterations,
                    value.CompletedAtUtc);
            });

            RestoreOne("electrostatics", state.Electrostatics != null, () =>
            {
                var value = state.Electrostatics;
                if (value.SchemaVersion != 1 || value.IonicStrengthIterations < 0 || value.CounterIonReleaseIterations < 0
                    || result.ElectrostaticsAnalysis == null)
                    throw new InvalidDataException("The saved electrostatics analysis has an invalid schema or is unavailable for this result.");
                result.ElectrostaticsAnalysis.RestoreResult(
                    RestoreIonicStrengthFit(value.IonicStrengthFit),
                    RestoreLinearFit(value.CounterIonReleaseFit),
                    value.IonicStrengthIterations,
                    value.CounterIonReleaseIterations,
                    value.CompletedAtUtc,
                    ParseErrorMethod(value.ErrorMethod));
            });

            RestoreOne("protonation", state.Protonation != null, () =>
            {
                var value = state.Protonation;
                if (value.SchemaVersion != 1 || value.CompletedIterations < 0
                    || value.BindingEnthalpy == null || value.ProtonationChange == null
                    || result.ProtonationAnalysis == null)
                    throw new InvalidDataException("The saved protonation analysis has an invalid schema or is unavailable for this result.");
                result.ProtonationAnalysis.RestoreResult(
                    value.BindingEnthalpy.Restore(),
                    value.ProtonationChange.Restore(),
                    value.CompletedIterations,
                    value.CompletedAtUtc,
                    ParseErrorMethod(value.ErrorMethod));
            });

            void RestoreOne(string kind, bool present, Action restore)
            {
                if (!present) return;
                try
                {
                    restore();
                }
                catch (Exception ex)
                {
                    if (policy == FtxtcReadPolicy.Strict) throw;
                    issues.Add(Issue("advanced-analysis-unavailable", reference.Id + ":" + kind, reference.Metadata,
                        $"The saved {kind} advanced analysis is unavailable: {ex.Message}"));
                }
            }
        }

        static IonicStrengthDependenceFit RestoreIonicStrengthFit(FtxtcIonicStrengthFitState state)
        {
            if (state == null) return null;
            if (state.Kd0 == null || state.Sensitivity == null || state.Curvature == null)
                throw new InvalidDataException("The saved ionic-strength fit is incomplete.");
            return new IonicStrengthDependenceFit(
                state.Kd0.Restore(), state.Sensitivity.Restore(), state.Curvature.Restore(), state.UsesCurvature);
        }

        static LinearFitWithError RestoreLinearFit(FtxtcLinearFitState state)
        {
            if (state == null) return null;
            if (state.Slope == null || state.Intercept == null)
                throw new InvalidDataException("The saved linear fit is incomplete.");
            return new LinearFitWithError(state.Slope.Restore(), state.Intercept.Restore(), state.ReferenceX);
        }

        static InjectionData RestoreInjection(ExperimentData experiment, FtxtcInjectionState state)
        {
            var injection = new InjectionData(experiment, state.Id, state.Volume, experiment.SyringeConcentration * state.Volume, state.Included);
            var rawPeakArea = state.RawPeakArea?.Restore() ?? new FloatWithError(double.NaN);
            injection.RestoreState(state.Included, state.Time, state.Volume, state.Delay, state.Duration, state.Filter, state.Temperature,
                state.IntegrationStartDelay, state.IntegrationEndOffset, state.ActualCellConcentration, state.ActualTitrantConcentration,
                state.Ratio, state.IsIntegrated, ParseHeatDirection(state.HeatDirection), rawPeakArea, rawPeakArea);
            return injection;
        }

        static ExperimentAttribute RestoreAttribute(FtxtcAttributeState state, int packageSchemaMinor)
        {
            var key = FtxtcWireIds.Attribute(state.Key);
            var value = ExperimentAttribute.FromKey(key);
            value.OptionName = state.Name; value.BoolValue = state.BoolValue;
            value.IntValue = packageSchemaMinor == 0
                ? state.IntValue ?? 0
                : FtxtcWireIds.AttributeIntValue(key, state.ValueId, state.IntValue);
            value.DoubleValue = state.DoubleValue; value.StringValue = state.StringValue;
            value.ParameterValue = state.ParameterValue?.Restore() ?? new FloatWithError(double.NaN);
            return value;
        }

        static void RestoreProcessor(ExperimentData experiment, FtxtcProcessorState state)
        {
            if (state == null) return;
            var processor = new DataProcessor(experiment) { DiscardIntegratedPoints = state.DiscardIntegratedPoints,
                IntegrationLengthMode = state.IntegrationLengthMode == "factor" ? InjectionData.IntegrationLengthMode.Factor : InjectionData.IntegrationLengthMode.Time,
                IntegrationLengthFactor = state.IntegrationLengthFactor, BaselineCompleted = state.BaselineCompleted };
            processor.InitializeBaseline(ParseProcessorType(state.Type));
            if (processor.Interpolator is SplineInterpolator spline && state.Spline != null)
            {
                spline.Algorithm = ParseSplineAlgorithm(state.Spline.Algorithm); spline.PointDensity = ParseSplineDensity(state.Spline.Density);
                spline.HandleMode = ParseSplineHandle(state.Spline.HandleMode); spline.ShowHandles = state.Spline.ShowHandles;
                spline.AllowPointTimeDragging = state.Spline.AllowPointTimeDragging; spline.PointsPerInjection = state.Spline.PointsPerInjection;
                spline.SetSplinePoints(state.Spline.Points.Select(point => new SplineInterpolator.SplinePoint(point.Time, point.Power, point.Id, point.Slope)
                { Locked = point.Locked, SlopeLocked = point.SlopeLocked, Linear = point.Linear, UserDefined = point.UserDefined }).ToList());
            }
            else if (processor.Interpolator is PolynomialLeastSquaresInterpolator polynomial && state.Polynomial != null)
            { polynomial.Degree = state.Polynomial.Degree; polynomial.ZLimit = state.Polynomial.ZLimit; }
            else if (processor.Interpolator is SegmentedBaselineInterpolator segmented && state.Segmented != null)
            {
                segmented.Degree = state.Segmented.Degree;
                segmented.RestoreSegments(state.Segmented.Segments.Select(segment => new SegmentedBaselineInterpolator.BaselineSegment(
                    segment.Kind == "initial-delay" ? SegmentedBaselineInterpolator.BaselineSegmentKind.InitialDelay : SegmentedBaselineInterpolator.BaselineSegmentKind.InjectionScope,
                    segment.InjectionId, segment.StartTime, segment.EndTime, segment.CenterTime, segment.Coefficients)));
            }
            else if (processor.Interpolator is AssymetricLeastSquaresInterpolator asl && state.Asl != null)
            { asl.Iterations = state.Asl.Iterations; asl.Lambda = state.Asl.Lambda; asl.Asymmetry = state.Asl.Asymmetry; }
            if (state.Locked) processor.Lock();
            experiment.SetProcessor(processor);
        }

        static void RestoreBaseline(ExperimentData experiment, byte[] bytes, string path)
        {
            var values = FtxbCodec.DecodeFloat64(bytes, path);
            if (values.GetLength(1) != 4) throw new InvalidDataException($"FTXTC baseline '{path}' must have four columns.");
            if (experiment.Processor?.Interpolator == null && values.GetLength(0) != 0) throw new InvalidDataException("Baseline data has no processor interpolator.");
            if (experiment.Processor?.Interpolator != null)
                experiment.Processor.Interpolator.Baseline = Enumerable.Range(0, values.GetLength(0))
                    .Select(row => new Energy(new FloatWithError(values[row, 0], values[row, 1], values[row, 2], values[row, 3]))).ToList();
        }

        static void RestoreCorrectedTrace(ExperimentData experiment)
        {
            if (experiment.Processor == null || !experiment.Processor.BaselineCompleted)
            {
                experiment.BaseLineCorrectedDataPoints = null;
                return;
            }

            var baseline = experiment.Processor.Interpolator?.Baseline;
            var raw = experiment.DataPoints ?? new List<DataPoint>();
            if (baseline == null)
                throw new InvalidDataException("Baseline processing is marked complete but no baseline interpolator is available.");
            if (raw.Count != baseline.Count)
                throw new InvalidDataException($"The raw thermogram has {raw.Count} points but the baseline has {baseline.Count} points.");

            experiment.Processor.RestoreBaselineCorrectedData();
        }

        static void ClearProcessedOutput(ExperimentData experiment)
        {
            experiment.BaseLineCorrectedDataPoints = null;
            if (experiment.Processor != null)
                experiment.Processor.BaselineCompleted = false;
        }

        static List<DataPoint> RestoreDataPoints(byte[] bytes, string path, int packageSchemaMinor)
        {
            var values = FtxbCodec.DecodeFloat32(bytes, path);
            var columns = values.GetLength(1);
            var validColumns = packageSchemaMinor switch
            {
                0 => columns == 7,
                1 => columns == 3 || columns == 7,
                _ => columns == 3,
            };
            if (!validColumns)
            {
                var expectedColumns = packageSchemaMinor == 0
                    ? "7 columns"
                    : packageSchemaMinor == 1 ? "3 or legacy 7 columns" : "3 columns";
                throw new InvalidDataException(
                    $"FTXTC {FTXTCFormat.SchemaMajor}.{packageSchemaMinor} trace '{path}' must have {expectedColumns}.");
            }
            return Enumerable.Range(0, values.GetLength(0))
                .Select(row => new DataPoint(values[row, 0], values[row, 1], values[row, 2]))
                .ToList();
        }

        static void RestoreBufferReferences(IReadOnlyDictionary<string, ExperimentData> experiments,
            FtxtcReadPolicy policy, List<FtxtcRecoveryIssue> issues)
        {
            foreach (var experiment in experiments.Values)
            {
                foreach (var injection in experiment.Injections)
                    injection.UpdateCorrectedPeakArea(BufferSubtractionModel.Empty(BufferSubtractionMethod.MatchedInjection));

                var settings = experiment.BufferSubtractionSettings;
                if (settings == null) continue;

                if (string.IsNullOrWhiteSpace(settings.ReferenceExperimentId)
                    || !experiments.TryGetValue(settings.ReferenceExperimentId, out var referenceExperiment))
                {
                    var message = $"Buffer-subtraction reference '{settings.ReferenceExperimentId}' is unavailable.";
                    if (policy == FtxtcReadPolicy.Strict) throw new InvalidDataException(message);
                    issues.Add(Issue("buffer-reference-unavailable", experiment.UniqueID, null,
                        message + " Raw peak areas were retained.", FtxtcIssueSeverity.Warning));
                    continue;
                }

                var model = BufferSubtractionCalculator.BuildModel(referenceExperiment, settings);
                foreach (var injection in experiment.Injections)
                    injection.UpdateCorrectedPeakArea(model);
            }
        }

        static AnalysisResultValiditySnapshot RestoreValiditySnapshot(System.Text.Json.JsonElement? value, int packageSchemaMinor)
        {
            if (!value.HasValue || value.Value.ValueKind == System.Text.Json.JsonValueKind.Null) return null;
            if (packageSchemaMinor == 0)
                return value.Value.Deserialize<AnalysisResultValiditySnapshot>(FTXTCFormat.JsonOptions);
            return value.Value.Deserialize<FtxtcValidityState>(FTXTCFormat.JsonOptions)?.Restore();
        }

        static FtxtcRecoveryIssue Issue(string code, string id, string path, string message, FtxtcIssueSeverity severity = FtxtcIssueSeverity.Error) =>
            new FtxtcRecoveryIssue { Code = code, Severity = severity, ComponentId = id, EntryPath = path, Message = message };
        static T TryRead<T>(IReadOnlyDictionary<string, byte[]> entries, string path, string id, FtxtcReadPolicy policy,
            List<FtxtcRecoveryIssue> issues, string code) where T : class
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !entries.TryGetValue(path, out var bytes)) throw new InvalidDataException($"Entry '{path}' is unavailable.");
                return FTXTCFormat.ReadJson<T>(bytes, path);
            }
            catch (Exception ex)
            {
                if (policy == FtxtcReadPolicy.Strict) throw;
                issues.Add(Issue(code, id, path, ex.Message)); return null;
            }
        }
        static byte[] Require(IReadOnlyDictionary<string, byte[]> entries, string path) =>
            entries.TryGetValue(path, out var bytes) ? bytes : throw new InvalidDataException($"Required bootstrap entry '{path}' is missing.");

        static ModelCloneOptions RestoreCloneOptions(FtxtcCloneOptionsState value) => value == null ? null : new ModelCloneOptions
        {
            IsGlobalClone = value.IsGlobalClone, ErrorEstimationMethod = ParseErrorMethod(value.ErrorMethod),
            IncludeConcentrationErrorsInBootstrap = value.IncludeConcentrationErrors,
            EnableAutoConcentrationVariance = value.EnableAutoConcentrationVariance,
            AutoConcentrationVariance = value.AutoConcentrationVariance, DiscardedDataPoint = value.DiscardedDataPoint,
            UnlockBootstrapParameters = value.UnlockParameters,
        };
        static SolverConvergence RestoreConvergence(FtxtcConvergenceState value) => value == null ? null : SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
        {
            Algorithm = value.Algorithm == "nelder-mead" ? SolverAlgorithm.NelderMead : SolverAlgorithm.LevenbergMarquardt,
            Termination = ParseTermination(value.Termination), ErrorEstimationOutcome = ParseErrorOutcome(value.ErrorOutcome),
            Iterations = value.Iterations, Loss = value.Loss, TimeSeconds = value.TimeSeconds,
            MolarRmsdJoulesPerMole = value.MolarRmsdJoulesPerMole,
            ErrorEstimationTimeSeconds = value.ErrorEstimationTimeSeconds, FailureReason = value.FailureReason,
            ErrorEstimationSummary = value.ErrorEstimationSummary,
            ErrorEstimationLimitTerminations = value.ErrorEstimationLimitTerminations,
            ErrorEstimationAttemptedRefits = value.ErrorEstimationAttemptedRefits,
            ErrorEstimationSucceededRefits = value.ErrorEstimationSucceededRefits,
            ErrorEstimationFailedRefits = value.ErrorEstimationFailedRefits,
        });

        static ITCDataFormat ParseDataFormat(string value) => value switch { "microcal-itc200" => ITCDataFormat.ITC200, "ftitc" => ITCDataFormat.FTITC, "ftxtc" => ITCDataFormat.FTXTC, "ta-itc" => ITCDataFormat.TAITC, "integrated-heats" => ITCDataFormat.IntegratedHeats, "peaq-itc-project" => ITCDataFormat.PEAQITCProject, "origin-opj" => ITCDataFormat.OriginProject, "nano-itc" => ITCDataFormat.NanoITC, "unknown" => ITCDataFormat.Unknown, _ => throw new NotSupportedException($"Unknown data format '{value}'.") };
        static ITCInstrument ParseInstrument(string value) => value switch { "unknown" => ITCInstrument.Unknown, "microcal-itc200" => ITCInstrument.MicroCalITC200, "microcal-peaq-itc" => ITCInstrument.MalvernITC200, "microcal-vp-itc" => ITCInstrument.MicroCalVPITC, "ta-itc-standard" => ITCInstrument.TAInstrumentsITCStandard, "ta-itc-low-volume" => ITCInstrument.TAInstrumentsITCLowVolume, _ => throw new NotSupportedException($"Unknown instrument '{value}'.") };
        static FeedbackMode ParseFeedback(string value) => value switch { "unknown" => FeedbackMode.Null, "none" => FeedbackMode.None, "low" => FeedbackMode.Low, "high" => FeedbackMode.High, _ => throw new NotSupportedException() };
        static ExperimentDateSource ParseDateSource(string value) => value switch { null => ExperimentDateSource.Unknown, "data-file" => ExperimentDateSource.DataFile, "file-system" => ExperimentDateSource.FileSystem, _ => throw new NotSupportedException() };
        static PeakHeatDirection ParseHeatDirection(string value) => value switch { "unknown" => PeakHeatDirection.Unknown, "exothermal" => PeakHeatDirection.Exothermal, "endothermal" => PeakHeatDirection.Endothermal, "both" => PeakHeatDirection.Both, _ => throw new NotSupportedException() };
        static BaselineInterpolatorTypes ParseProcessorType(string value) => FtxtcWireIds.Processor(value);
        static SplineInterpolator.SplineInterpolatorAlgorithm ParseSplineAlgorithm(string value) => value switch { "smooth" => SplineInterpolator.SplineInterpolatorAlgorithm.Smooth, "handles" => SplineInterpolator.SplineInterpolatorAlgorithm.Handles, "rigid" => SplineInterpolator.SplineInterpolatorAlgorithm.Rigid, "linear" => SplineInterpolator.SplineInterpolatorAlgorithm.Linear, _ => throw new NotSupportedException() };
        static SplineInterpolator.SplinePointDensity ParseSplineDensity(string value) => value switch { "sparse" => SplineInterpolator.SplinePointDensity.Sparse, "balanced" => SplineInterpolator.SplinePointDensity.Balanced, "dense" => SplineInterpolator.SplinePointDensity.Dense, _ => throw new NotSupportedException() };
        static SplineInterpolator.SplineHandleMode ParseSplineHandle(string value) => value switch { "mean" => SplineInterpolator.SplineHandleMode.Mean, "median" => SplineInterpolator.SplineHandleMode.Median, "minimum-volatility" => SplineInterpolator.SplineHandleMode.MinVolatility, _ => throw new NotSupportedException() };
        static ProfileLikelihoodRunResult RestoreProfile(FtxtcProfileRunState state)
        {
            if (state == null) return null;
            var calibration = ProfileCalibration(state.Calibration);
            var algorithm = ProfileAlgorithm(state.Algorithm);
            var outcome = ProfileOutcome(state.Outcome);
            var coordinates = (state.Coordinates ?? new List<FtxtcProfileCoordinateState>()).Select(value => new ProfileCoordinateResult(
                new ProfileCoordinateId(FtxtcWireIds.Parameter(value.ParameterId),
                    ProfileScope(value.Scope),
                    value.ExperimentId, value.Index), value.BestValue, value.LowerBound, value.UpperBound,
                RestoreProfileSide(value.Lower), RestoreProfileSide(value.Upper), value.Warnings)).ToList();
            return new ProfileLikelihoodRunResult(state.ConfidenceLevel, calibration, state.N, state.P, state.Q, state.Df,
                state.BaselineObjective, state.TargetIncrement, algorithm, state.Weighted, state.Tolerance, state.CandidateIterationLimit,
                state.ExpansionLimit, state.RefinementLimit, TimeSpan.FromSeconds(state.ElapsedSeconds), outcome, coordinates,
                state.AttemptedSolverCalls, state.OptimizerToleranceSetting ?? double.NaN);
        }
        static ProfileSideResult RestoreProfileSide(FtxtcProfileSideState state)
        {
            if (state == null) return new ProfileSideResult(ProfileSideOutcome.SearchExhausted);
            var outcome = ProfileSideOutcomeValue(state.Outcome);
            return new ProfileSideResult(outcome, state.Endpoint, state.CrossingG, state.EvaluationCount, state.AttemptedSolverCalls, state.Warnings);
        }
        static ProfileLikelihoodCalibration ProfileCalibration(string value) => value switch
        {
            "unweighted-f-calibrated-rss" => ProfileLikelihoodCalibration.UnweightedFCalibratedRss,
            "weighted-chi-squared" => ProfileLikelihoodCalibration.WeightedChiSquared,
            _ => throw new NotSupportedException($"Unknown profile calibration '{value}'."),
        };
        static SolverAlgorithm ProfileAlgorithm(string value) => value switch
        {
            "nelder-mead" => SolverAlgorithm.NelderMead,
            "levenberg-marquardt" => SolverAlgorithm.LevenbergMarquardt,
            _ => throw new NotSupportedException($"Unknown profile algorithm '{value}'."),
        };
        static ParameterBoundaryScope ProfileScope(string value) => value switch
        {
            "local" => ParameterBoundaryScope.Local,
            "shared" => ParameterBoundaryScope.Shared,
            _ => throw new NotSupportedException($"Unknown profile coordinate scope '{value}'."),
        };
        static ErrorEstimationOutcome ProfileOutcome(string value) => value switch
        {
            "none" => ErrorEstimationOutcome.None,
            "not-run" => ErrorEstimationOutcome.NotRun,
            "completed" => ErrorEstimationOutcome.Completed,
            "partial-failure" => ErrorEstimationOutcome.PartialFailure,
            "complete-failure" => ErrorEstimationOutcome.CompleteFailure,
            "cancelled" => ErrorEstimationOutcome.Cancelled,
            _ => throw new NotSupportedException($"Unknown profile run outcome '{value}'."),
        };
        static ProfileSideOutcome ProfileSideOutcomeValue(string value) => value switch
        {
            "endpoint-found" => ProfileSideOutcome.EndpointFound,
            "bound-reached-before-crossing" => ProfileSideOutcome.BoundReachedBeforeCrossing,
            "search-exhausted" => ProfileSideOutcome.SearchExhausted,
            "optimizer-failure" => ProfileSideOutcome.OptimizerFailure,
            "non-finite-candidate" => ProfileSideOutcome.NonFiniteCandidate,
            "cancelled" => ProfileSideOutcome.Cancelled,
            "primary-minimum-improved" => ProfileSideOutcome.PrimaryMinimumImproved,
            _ => throw new NotSupportedException($"Unknown profile side outcome '{value}'."),
        };
        static ErrorEstimationMethod ParseErrorMethod(string value) => value switch { "none" => ErrorEstimationMethod.None, "bootstrap-residuals" => ErrorEstimationMethod.BootstrapResiduals, "leave-one-out" => ErrorEstimationMethod.LeaveOneOut, "profile-likelihood" => ErrorEstimationMethod.ProfileLikelihood, _ => throw new NotSupportedException($"Unknown error method '{value}'.") };
        static FTSRMethod.SRFoldedMode ParseSpolarFoldedMode(string value) => value switch { "globular" => FTSRMethod.SRFoldedMode.Glob, "intermediate" => FTSRMethod.SRFoldedMode.Intermediate, "intrinsically-disordered" => FTSRMethod.SRFoldedMode.ID, _ => throw new NotSupportedException($"Unknown Spolar folded mode '{value}'.") };
        static FTSRMethod.SRTempMode ParseSpolarTemperatureMode(string value) => value switch { "isoentropic-point" => FTSRMethod.SRTempMode.IsoEntropicPoint, "mean-temperature" => FTSRMethod.SRTempMode.MeanTemperature, "reference-temperature" => FTSRMethod.SRTempMode.ReferenceTemperature, _ => throw new NotSupportedException($"Unknown Spolar temperature mode '{value}'.") };
        static VariableConstraint ParseConstraint(string value) => value switch { "none" => VariableConstraint.None, "temperature-dependent" => VariableConstraint.TemperatureDependent, "same-for-all" => VariableConstraint.SameForAll, _ => throw new NotSupportedException() };
        static SolverTermination ParseTermination(string value) => value switch { "unknown" => SolverTermination.Unknown, "converged" => SolverTermination.Converged, "small-step" => SolverTermination.SmallStep, "small-gradient" => SolverTermination.SmallGradient, "reached-target" => SolverTermination.ReachedTarget, "iteration-limit" => SolverTermination.IterationLimit, "evaluation-limit" => SolverTermination.EvaluationLimit, "time-limit" => SolverTermination.TimeLimit, "cancelled" => SolverTermination.Cancelled, "invalid-values" => SolverTermination.InvalidValues, "failed" => SolverTermination.Failed, _ => throw new NotSupportedException() };
        static ErrorEstimationOutcome ParseErrorOutcome(string value) => value switch { "none" => ErrorEstimationOutcome.None, "not-run" => ErrorEstimationOutcome.NotRun, "completed" => ErrorEstimationOutcome.Completed, "partial-failure" => ErrorEstimationOutcome.PartialFailure, "complete-failure" => ErrorEstimationOutcome.CompleteFailure, "cancelled" => ErrorEstimationOutcome.Cancelled, _ => throw new NotSupportedException() };
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
                var next = value ?? ""; if (path == next) return; path = next;
                Format = string.Equals(System.IO.Path.GetExtension(next), ".ftitc", StringComparison.OrdinalIgnoreCase) ? ITCDataFormat.FTITC : ITCDataFormat.FTXTC;
                PathChanged?.Invoke(null, EventArgs.Empty);
            }
        }
    }
}
