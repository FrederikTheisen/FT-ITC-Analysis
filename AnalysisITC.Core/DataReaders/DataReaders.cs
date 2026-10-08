using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using System.Text.RegularExpressions;
using System.Globalization;
using System.IO;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using Buffer = AnalysisITC.Core.Data.Buffer;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Core.Interpretation;

namespace AnalysisITC.Core.DataReaders
{
    public static partial class DataReader
    {
        static ITCDataContainer GetValidData(
            ITCDataContainer data,
            bool allowAutomaticActions,
            ICollection<AutomaticImportActionReport> automaticActionReports)
        {
            if (data == null) return null;
            bool valid = true;
            if (data is ExperimentData experiment)
            {
                valid = ImportValidator.ValidateData(
                    experiment,
                    allowAutomaticActions && !experiment.IsTandemExperiment,
                    automaticActionReports);
            }

            return valid ? data : null;
        }

        static bool AddData(
            ITCDataContainer[] data,
            bool allowAutomaticActions,
            ICollection<AutomaticImportActionReport> automaticActionReports,
            ICollection<ExperimentData> acceptedExperiments = null)
        {
            var validData = data?
                .Select(item => GetValidData(item, allowAutomaticActions, automaticActionReports))
                .Where(dat => dat != null)
                .ToArray() ?? Array.Empty<ITCDataContainer>();

            if (validData.Length == 0) return false;

            DataManager.AddData(validData);
            if (acceptedExperiments != null)
                foreach (var experiment in validData.OfType<ExperimentData>())
                    acceptedExperiments.Add(experiment);
            return true;
        }

        public static ITCDataFormat GetFormat(string path)
        {
            try
            {
                var ext = System.IO.Path.GetExtension(path).ToLower();

                foreach (var format in ITCFormatAttribute.GetAllFormats())
                {
                    var extensions = format.GetProperties().Extensions;

                    if (extensions.Contains(ext)) return format;
                }
            }
            catch
            {
                AppEventHandler.PrintAndLog("GetFormat Error: " + path);
            }

            return ITCDataFormat.Unknown;
        }

        public static bool IsProjectFile(string path)
        {
            var format = GetFormat(path);
            return format == ITCDataFormat.FTITC || format == ITCDataFormat.FTXTC;
        }

        public static async void Read(string path) => await ReadPathsAsync(new[] { path });

        public static async void Read(IEnumerable<string> paths) => await ReadPathsAsync(paths);

        public static async Task<DataReadResult> ReadPathsAsync(IEnumerable<string> paths, Action<string> didReadPath = null)
        {
            var pathList = paths?.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray() ?? Array.Empty<string>();
            var loadedPaths = new List<string>();
            var importedExperiments = new List<ExperimentData>();
            var automaticActionReports = new List<AutomaticImportActionReport>();
            var recoveryIssues = new List<FtxtcRecoveryIssue>();

            StatusBarManager.SetStatus("Reading data...", 0);
            StatusBarManager.StartInderminateProgress();
            IntegratedHeatReader.BeginImportQueue();

            var allProjectFiles = pathList.Length > 0 && pathList.All(IsProjectFile);
            var wasEmptyDocument = (DataManager.SourceItems == null || DataManager.SourceItems.Count == 0)
                && DataManager.Reports.Count == 0;
            var initialItemCount = DataManager.SourceItems?.Count ?? 0;
            var initialReportCount = DataManager.Reports.Count;
            var addedExperiments = new List<ExperimentData>();
            var addedReports = new List<AnalysisReport>();

            try
            {
                using (allProjectFiles ? DocumentDirtyTracker.RestoreDocument() : DocumentDirtyTracker.Suspend())
                {
                    await Task.Delay(1);

                    foreach (var path in pathList)
                    {
                        var format = GetFormat(path);
                        var isProjectFile = IsProjectFile(path);
                        var fileName = Path.GetFileName(path);

                        AppEventHandler.PrintAndLog($"Loading File: {fileName}");
                        StatusBarManager.SetStatus(isProjectFile
                            ? $"Loading: {Path.GetFileNameWithoutExtension(fileName)}"
                            : $"Reading file: {fileName}", 0);
                        StatusBarManager.SetSecondaryStatus("", 0);
                        await Task.Delay(1); //Necessary to update UI. Unclear why whole method has to be on UI thread.
                        var previousDocumentPath = ProjectDocumentState.Path;
                        var dat = await ReadFile(path);

                        if (IntegratedHeatReader.CancelRemainingQueueItems)
                        {
                            break;
                        }

                        var reports = Array.Empty<AnalysisReport>();
                        if (format == ITCDataFormat.FTXTC && dat != null)
                        {
                            recoveryIssues.AddRange(FTXTCReader.LastRecoveryIssues);
                            var resolved = FtxtcImportResolver.Resolve(path, dat, FTXTCReader.LastReports);
                            dat = resolved.Containers;
                            reports = resolved.Reports;
                        }

                        var beforeAdd = DataManager.SourceItems.Count;
                        var acceptedThisFile = isProjectFile ? null : new List<ExperimentData>();
                        var addedContainers = dat != null && AddData(
                            dat,
                            allowAutomaticActions: !isProjectFile,
                            automaticActionReports: automaticActionReports,
                            acceptedExperiments: acceptedThisFile);
                        if (format == ITCDataFormat.FTXTC && dat != null)
                            DataManager.AddReports(reports);
                        var acceptedContent = addedContainers || reports.Length > 0;
                        if (acceptedContent)
                        {
                            addedExperiments.AddRange(DataManager.SourceItems.Skip(beforeAdd).OfType<ExperimentData>());
                            addedReports.AddRange(reports);
                            if (acceptedThisFile != null) importedExperiments.AddRange(acceptedThisFile);
                            AppSettings.LastDocumentPath = path;
                        }
                        else if (format == ITCDataFormat.FTXTC)
                        {
                            // Reading a package sets its save path. A skipped import
                            // must leave the current document destination untouched.
                            ProjectDocumentState.Path = previousDocumentPath;
                        }
                        if (acceptedContent || (format == ITCDataFormat.FTXTC && dat != null))
                        {
                            loadedPaths.Add(path);
                            didReadPath?.Invoke(path);
                        }
                    }

                    AppSettings.LastDocumentPaths = pathList;
                    DataManager.ApplyOptions(addedExperiments);
                    foreach (var report in addedReports)
                        report.SetInterpretationFreshness(AnalysisInterpretationService.EvaluateFreshness(report,
                            id => DataManager.Results.Find(result => result.UniqueID == id),
                            id => DataManager.Data.Find(experiment => experiment.UniqueID == id)));
                }
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
            finally
            {
                IntegratedHeatReader.EndImportQueue();
            }

            var addedData = (DataManager.SourceItems?.Count ?? 0) > initialItemCount
                || DataManager.Reports.Count > initialReportCount;
            var openedCleanProject = wasEmptyDocument && allProjectFiles && pathList.Length == 1 && addedData
                && GetFormat(pathList[0]) == ITCDataFormat.FTXTC
                && !string.IsNullOrWhiteSpace(FTITCFormat.CurrentAccessedAppDocumentPath);

            if (openedCleanProject)
            {
                using (DocumentDirtyTracker.RestoreDocument())
                {
                    DocumentDirtyTracker.BeginDocument();
                    DocumentDirtyTracker.MarkClean();
                    await Task.Delay(1);
                    DocumentDirtyTracker.MarkClean();
                }
            }
            else if (addedData)
            {
                DocumentDirtyTracker.MarkDirty();
            }

            StatusBarManager.SetStatus("Rendering data...", 0);
            await Task.Delay(1);
            StatusBarManager.ClearAppStatus();

            var automaticActionStatus = BuildAutomaticImportActionStatus(automaticActionReports);
            if (!string.IsNullOrEmpty(automaticActionStatus))
                StatusBarManager.SetStatus(automaticActionStatus, 8000);

            FTXTCReader.DisplayRecoveryNotice(recoveryIssues);

            return new DataReadResult(
                requestedPathCount: pathList.Length,
                loadedPaths: loadedPaths,
                initialItemCount: initialItemCount,
                finalItemCount: DataManager.SourceItems?.Count ?? 0,
                openedCleanProject: openedCleanProject,
                importedExperiments: importedExperiments);
        }

        public static async Task<bool> ReadRecoveryFileAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            if ((DataManager.SourceItems != null && DataManager.SourceItems.Count > 0) || DataManager.Reports.Count > 0) return false;

            try
            {
                ITCDataContainer[] recovered;
                IReadOnlyList<AnalysisReport> recoveredReports = Array.Empty<AnalysisReport>();
                IReadOnlyList<FtxtcRecoveryIssue> recoveryIssues = Array.Empty<FtxtcRecoveryIssue>();
                using (var stream = File.OpenRead(path))
                {
                    if (GetFormat(path) == ITCDataFormat.FTITC)
                        recovered = await FTITCReader.ReadStream(stream, interactive: true);
                    else
                    {
                        var recovery = await FTXTCReader.ReadWithRecovery(stream, FtxtcReadPolicy.RecoverUsableContent, interactive: true);
                        recovered = recovery.Containers;
                        recoveredReports = recovery.Reports;
                        recoveryIssues = recovery.Issues;
                        foreach (var issue in recovery.Issues)
                            AppEventHandler.PrintAndLog($"FTXTC recovery [{issue.Code}] {issue.Message}");
                    }
                }

                using (DocumentDirtyTracker.RestoreDocument())
                {
                    if (!AddData(recovered, allowAutomaticActions: false, new List<AutomaticImportActionReport>())) return false;
                    DataManager.AddReports(recoveredReports);
                    DataManager.ApplyOptions();
                    FTITCFormat.CurrentAccessedAppDocumentPath = "";
                    DocumentDirtyTracker.BeginDocument();
                }

                DocumentDirtyTracker.MarkDirty();
                await Task.Delay(1);
                FTXTCReader.DisplayRecoveryNotice(recoveryIssues);
                return true;
            }
            catch (Exception ex)
            {
                AppEventHandler.PrintAndLog("Recovery file could not be opened: " + ex.Message);
                return false;
            }
        }

        internal static string BuildAutomaticImportActionStatus(
            IEnumerable<AutomaticImportActionReport> reports)
        {
            var reportList = reports?
                .Where(report => report != null && report.DiscardedOrphanInjectionCount > 0)
                .ToList() ?? new List<AutomaticImportActionReport>();

            if (reportList.Count == 0) return "";

            var discardedCount = reportList.Sum(report => report.DiscardedOrphanInjectionCount);
            var injectionWord = discardedCount == 1 ? "injection" : "injections";

            if (reportList.Count == 1)
            {
                return $"Automatically discarded {discardedCount} orphan {injectionWord} " +
                    $"while loading {reportList[0].ExperimentName}.";
            }

            return $"Automatically discarded {discardedCount} orphan {injectionWord} " +
                $"across {reportList.Count} experiments.";
        }

        static async Task<ITCDataContainer[]> ReadFile(string path)
        {
            try
            {
                var format = GetFormat(path);

                switch (format)
                {
                    case ITCDataFormat.FTITC:
                        return await FTITCReader.ReadPath(path);
                    case ITCDataFormat.FTXTC:
                        return await FTXTCReader.ReadPath(path);
                    case ITCDataFormat.ITC200:
                        return new ExperimentData[] { MicroCalITC200Reader.ReadPath(path) };
                    case ITCDataFormat.TAITC:
                        return new ExperimentData[] { TAFileReader.ReadPath(path) };
                    case ITCDataFormat.IntegratedHeats:
                        return new ExperimentData[] { IntegratedHeatReader.ReadFile(path) };
                    case ITCDataFormat.PEAQITCProject:
                        return new ExperimentData[] { PEAQReader.ReadFile(path) };
                    case ITCDataFormat.OriginProject:
                        return new ExperimentData[] { OriginProjectReader.ReadFile(path) };
                    case ITCDataFormat.NanoITC:
                        return new ExperimentData[] { NanoItcReader.ReadPath(path) };
                    case ITCDataFormat.Unknown:
                        AppEventHandler.PrintAndLog($"Unknown File Format: {path}");
                        break;
                }
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }

            return null;
        }
    }

    public sealed class DataReadResult
    {
        public DataReadResult(int requestedPathCount, IEnumerable<string> loadedPaths, int initialItemCount, int finalItemCount, bool openedCleanProject,
            IEnumerable<ExperimentData> importedExperiments = null)
        {
            RequestedPathCount = Math.Max(0, requestedPathCount);
            LoadedPaths = loadedPaths?.ToArray() ?? Array.Empty<string>();
            InitialItemCount = Math.Max(0, initialItemCount);
            FinalItemCount = Math.Max(0, finalItemCount);
            OpenedCleanProject = openedCleanProject;
            ImportedExperiments = Array.AsReadOnly(importedExperiments?.ToArray() ?? Array.Empty<ExperimentData>());
        }

        public int RequestedPathCount { get; }
        public IReadOnlyList<string> LoadedPaths { get; }
        public int LoadedPathCount => LoadedPaths.Count;
        public int FailedOrSkippedPathCount => Math.Max(0, RequestedPathCount - LoadedPathCount);
        public int InitialItemCount { get; }
        public int FinalItemCount { get; }
        public int AddedItemCount => Math.Max(0, FinalItemCount - InitialItemCount);
        public bool OpenedCleanProject { get; }
        public IReadOnlyList<ExperimentData> ImportedExperiments { get; }
        public bool LoadedAny => LoadedPathCount > 0 || AddedItemCount > 0;
        public bool LoadedAllRequested => RequestedPathCount > 0 && FailedOrSkippedPathCount == 0;
    }

    public class RawDataReader
    {
        /// <summary>Rejects NaN and infinity in instrument values that the importer uses.</summary>
        internal static float RequireFinite(float value, string line)
        {
            if (!FWEMath.IsFinite(value))
                throw new FormatException($"The file contains a value that is not a finite number: '{line}'.");
            return value;
        }

        /// <summary>Validate a proposed bookkeeping change before editing experiment metadata.</summary>
        public static void ValidateInjectionProtocol(ExperimentData experiment, DilutionMethod method, double cellVolume)
        {
            _ = InjectionBookkeeping.HeatMethodFor(method);
            if (method != DilutionMethod.DiscreteDisplacement) return;
            _ = InjectionDisplacementCalculator.DiscreteDisplacementRetention(cellVolume, 0);
            foreach (var injection in experiment.Injections)
                _ = InjectionDisplacementCalculator.DiscreteDisplacementRetention(cellVolume, injection.Volume);
        }

        public static void ProcessInjections(ExperimentData experiment)
        {
            // We cannot reprocess injections for tandem experiments
            if (experiment.IsTandemExperiment) return;

            AppEventHandler.PrintAndLog("Processing injections for: " + experiment.FileName + " / " + experiment.Name);

            if (experiment.AppliedDilutionMethod.HasValue)
                RecalculateInjections(experiment);
            else
                ProcessInjections(experiment, experiment.PendingImportBookkeepingMethod ?? AppSettings.DilutionCalculationMethod);
        }

        /// <summary>Processes new data with an explicit paired concentration/heat method.</summary>
        public static void ProcessInjections(ExperimentData experiment, DilutionMethod method)
        {
            if (experiment.IsTandemExperiment)
                throw new InvalidOperationException("Rebuild tandem experiments through the tandem tool to change bookkeeping.");
            var heatMethod = InjectionBookkeeping.HeatMethodFor(method);
            ProcessInjectionsUsingMethod(experiment, method);
            experiment.HeatMethod = heatMethod;
            experiment.PendingImportBookkeepingMethod = null;
        }

        /// <summary>Explicit user selection. Does not alter measured heats or their processing.</summary>
        public static void ReprocessInjections(ExperimentData experiment, DilutionMethod method)
        {
            ProcessInjections(experiment, method);
            experiment.UpdateProcessing();
        }

        /// <summary>
        /// Applies an explicitly selected bookkeeping method to ordinary experiments.
        /// Tandem experiments are intentionally left untouched because their concentration
        /// trajectory must be rebuilt by the tandem tool. Every eligible protocol is
        /// validated before any experiment is changed.
        /// </summary>
        public static int ReprocessInjections(IEnumerable<ExperimentData> experiments, DilutionMethod method)
        {
            var targets = experiments?
                .Where(experiment => experiment != null && !experiment.IsTandemExperiment)
                .ToList() ?? new List<ExperimentData>();

            foreach (var experiment in targets)
                ValidateInjectionProtocol(experiment, method, experiment.CellVolume);

            foreach (var experiment in targets)
                ReprocessInjections(experiment, method);

            return targets.Count;
        }

        /// <summary>Recomputes a known concentration law without upgrading historical heat behavior.</summary>
        public static void RecalculateInjections(ExperimentData experiment)
        {
            if (experiment.IsTandemExperiment) return;
            if (!experiment.AppliedDilutionMethod.HasValue)
                throw new InvalidOperationException("Select MicroCal, Ideal continuous mixing, or Discrete displacement in Experiment Details before recalculating saved concentrations.");
            ProcessInjectionsUsingMethod(experiment, experiment.AppliedDilutionMethod.Value);
        }

        internal static void ProcessInjectionsMicroCal(ExperimentData experiment)
        {
            ProcessInjectionsUsingMethod(experiment, DilutionMethod.MicroCal);
        }

        internal static void ProcessInjectionsExponential(ExperimentData experiment)
        {
            ProcessInjectionsUsingMethod(experiment, DilutionMethod.Exponential);
        }

        /// <summary>Concentration-only operation: retains historical heat bookkeeping.
        /// New imports and explicit mode selection must use ProcessInjections/ReprocessInjections.</summary>
        internal static void ProcessInjectionsUsingMethod(ExperimentData experiment, DilutionMethod method)
        {
            _ = InjectionBookkeeping.HeatMethodFor(method); // Reject unknown enum values.
            if (method == DilutionMethod.DiscreteDisplacement)
            {
                // Validate the entire protocol before changing any stored concentrations.
                _ = InjectionDisplacementCalculator.DiscreteDisplacementRetention(experiment.CellVolume, 0);
                var retentions = experiment.Injections.Select(injection =>
                    InjectionDisplacementCalculator.DiscreteDisplacementRetention(experiment.CellVolume, injection.Volume)).ToArray();
                var initial = new InjectionConcentrationState(experiment.CellConcentration.Value, 0.0);
                var retention = 1.0;
                for (var i = 0; i < experiment.Injections.Count; i++)
                {
                    retention *= retentions[i];
                    InjectionDisplacementCalculator.ApplyToInjection(experiment, experiment.Injections[i],
                        InjectionDisplacementCalculator.DiscreteDisplacementState(initial, experiment.SyringeConcentration.Value, retention));
                }
                experiment.AppliedDilutionMethod = method;
                return;
            }
            var deltaVolume = 0.0;

            foreach (var inj in experiment.Injections)
            {
                deltaVolume += inj.Volume;
                var state = InjectionDisplacementCalculator.Calculate(
                    method,
                    experiment.CellVolume,
                    experiment.SyringeConcentration.Value,
                    experiment.CellConcentration.Value,
                    deltaVolume);
                InjectionDisplacementCalculator.ApplyToInjection(experiment, inj, state);
            }
            experiment.AppliedDilutionMethod = method;
        }

        /// <summary>
        /// Determine derived properties and try parse the comment for attributes
        /// </summary>
        /// <param name="experiment"></param>
        public static void ProcessExperiment(ExperimentData experiment)
        {
            experiment.MeasuredTemperature = experiment.DataPoints.Average(dp => dp.Temperature);

            ITCInstrumentAttribute.ResolveInstrument(experiment);

            // Try to extract attributes from comments
            if (!string.IsNullOrEmpty(experiment.Comments))
            {
                var comment = experiment.Comments;

                // Global pH fallback (if comment says "pH 7.4" once, apply to buffers that don’t have a local pH match)
                double? pH = null;
                {
                    var m = Regex.Match(comment, @"\bpH\s*[:=]?\s*([+-]?\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
                    if (m.Success && TryParseNumber(m.Groups[1].Value, out var ph)) pH = ph;
                }

                // Special buffers (expand into explicit components)
                if (Regex.IsMatch(comment, @"\b(1x)?PBS\b", RegexOptions.IgnoreCase))
                    BufferAttribute.SetupSpecialBuffer(experiment.Attributes, global::AnalysisITC.Core.Data.Buffer.PBS);
                if (Regex.IsMatch(comment, @"\b(1x)?TBS\b", RegexOptions.IgnoreCase))
                    BufferAttribute.SetupSpecialBuffer(experiment.Attributes, global::AnalysisITC.Core.Data.Buffer.TBS);

                // ---------- Salt ----------
                foreach (var salt in SaltAttribute.GetSalts())
                {
                    var sname = salt.GetProperties().Name;

                    if (!Regex.IsMatch(comment, $@"(?<![A-Za-z0-9]){Regex.Escape(sname)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase))
                        continue;

                    if (HasAttribute(experiment.Attributes, AttributeKey.Salt, (int)salt))
                        continue;

                    if (!ConcentrationUnitAttribute.TryExtractConcentrationM(comment, sname, out var concM))
                        continue;

                    var att = ExperimentAttribute.FromKey(AttributeKey.Salt);
                    att.IntValue = (int)salt;
                    att.ParameterValue = new FloatWithError(concM, 0);
                    experiment.Attributes.Add(att);
                }

                // ---------- Buffer ----------
                foreach (var buffer in BufferAttribute.GetBuffers())
                {
                    var bnames = buffer.GetProperties().Aliases;

                    bool matched = false;
                    string matchedName = bnames[0];

                    foreach (var bn in bnames)
                    {
                        if (Regex.IsMatch(comment, $@"(?<![A-Za-z0-9]){Regex.Escape(bn)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase))
                        {
                            matched = true;
                            matchedName = bn;
                            break;
                        }
                    }

                    if (!matched) continue;
                    if (HasAttribute(experiment.Attributes, AttributeKey.Buffer, (int)buffer)) continue;

                    ConcentrationUnitAttribute.TryExtractConcentrationM(comment, matchedName, out var concM);

                    var att = ExperimentAttribute.FromKey(AttributeKey.Buffer);
                    att.IntValue = (int)buffer;
                    att.ParameterValue = new FloatWithError(concM, 0);

                    if (pH.HasValue) att.DoubleValue = pH.Value;
                    else
                    {
                        // Fallback to pKa of buffer with one decimal (it is a guess to avoid 0)
                        pH = Math.Round(BufferAttribute.GetDefaultpHValue(buffer), 1);
                        att.DoubleValue = pH.Value;
                    }

                    experiment.Attributes.Add(att);
                }
            }

            //experiment.CalculatePeakHeatDirection();
        }

        static bool HasAttribute(List<ExperimentAttribute> atts, AttributeKey key, int intValue) => atts.Any(a => a.Key == key && a.IntValue == intValue);

        static bool TryParseNumber(string s, out double v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;

            // allow both "7.4" and "7,4"
            var t = s.Trim();
            if (t.Count(c => c == ',') == 1 && !t.Contains('.')) t = t.Replace(',', '.');

            return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
