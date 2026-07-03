using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using System.Text.RegularExpressions;
using System.Globalization;
using System.IO;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using Buffer = AnalysisITC.Core.Data.Buffer;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.DataReaders
{
    public static partial class DataReader
    {
        static ITCDataContainer GetValidData(ITCDataContainer data)
        {
            if (data == null) return null;
            bool valid = true;
            if (data is ExperimentData) valid = ImportValidator.ValidateData(data as ExperimentData);

            return valid ? data : null;
        }

        static bool AddData(ITCDataContainer[] data)
        {
            var validData = data?
                .Select(GetValidData)
                .Where(dat => dat != null)
                .ToArray() ?? Array.Empty<ITCDataContainer>();

            if (validData.Length == 0) return false;

            DataManager.AddData(validData);
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

        public static async void Read(string path) => await ReadPathsAsync(new[] { path });

        public static async void Read(IEnumerable<string> paths) => await ReadPathsAsync(paths);

        public static async Task ReadPathsAsync(IEnumerable<string> paths, Action<string> didReadPath = null)
        {
            var pathList = paths?.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray() ?? Array.Empty<string>();

            StatusBarManager.SetStatus("Reading data...", 0);
            StatusBarManager.StartInderminateProgress();
            IntegratedHeatReader.BeginImportQueue();

            var allFtitc = pathList.Length > 0 && pathList.All(path => GetFormat(path) == ITCDataFormat.FTITC);
            var wasEmptyDocument = DataManager.SourceItems == null || DataManager.SourceItems.Count == 0;
            var initialItemCount = DataManager.SourceItems?.Count ?? 0;

            try
            {
                using (allFtitc ? DocumentDirtyTracker.RestoreDocument() : DocumentDirtyTracker.Suspend())
                {
                    await Task.Delay(1);

                    foreach (var path in pathList)
                    {
                        var format = GetFormat(path);
                        var isFtitc = format == ITCDataFormat.FTITC;
                        var fileName = Path.GetFileName(path);

                        AppEventHandler.PrintAndLog($"Loading File: {fileName}");
                        StatusBarManager.SetStatus(isFtitc
                            ? $"Loading: {Path.GetFileNameWithoutExtension(fileName)}"
                            : $"Reading file: {fileName}", 0);
                        StatusBarManager.SetSecondaryStatus("", 0);
                        await Task.Delay(1); //Necessary to update UI. Unclear why whole method has to be on UI thread.
                        var dat = await ReadFile(path);

                        if (IntegratedHeatReader.CancelRemainingQueueItems)
                        {
                            break;
                        }

                        if (dat != null && AddData(dat))
                        {
                            didReadPath?.Invoke(path);
                            AppSettings.LastDocumentPath = path;
                        }
                    }

                    AppSettings.LastDocumentPaths = pathList;
                    DataManager.ApplyOptions();
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

            var addedData = (DataManager.SourceItems?.Count ?? 0) > initialItemCount;
            var openedCleanProject = wasEmptyDocument && allFtitc && pathList.Length == 1 && addedData;

            if (openedCleanProject)
            {
                DocumentDirtyTracker.MarkClean();
                await Task.Delay(1);
                DocumentDirtyTracker.MarkClean();
            }
            else if (addedData)
            {
                DocumentDirtyTracker.MarkDirty();
            }

            StatusBarManager.SetStatus("Rendering data...", 0);
            await Task.Delay(1);
            StatusBarManager.ClearAppStatus();
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
                    case ITCDataFormat.VPITC: // TODO No idea what vpitc files might look like if they exist
                    case ITCDataFormat.ITC200:
                        return new ExperimentData[] { MicroCalITC200Reader.ReadPath(path) };
                    case ITCDataFormat.TAITC:
                        return new ExperimentData[] { TAFileReader.ReadPath(path) };
                    case ITCDataFormat.IntegratedHeats:
                        return new ExperimentData[] { IntegratedHeatReader.ReadFile(path) };
                    case ITCDataFormat.PEAQITCProject:
                        return new ExperimentData[] { PEAQReader.ReadFile(path) };
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

    public class RawDataReader
    {
        public static void ProcessInjections(ExperimentData experiment)
        {
            // We cannot reprocess injections for tandem experiments
            if (experiment.IsTandemExperiment) return;

            AppEventHandler.PrintAndLog("Processing injections for: " + experiment.FileName + " / " + experiment.Name);

            switch (AppSettings.DilutionCalculationMethod)
            {
                default:
                case DilutionMethod.MicroCal:
                    ProcessInjectionsMicroCal(experiment);
                    break;
                case DilutionMethod.Exponential:
                    ProcessInjectionsExponential(experiment);
                    break;
            }
        }

        internal static void ProcessInjectionsMicroCal(ExperimentData experiment)
        {
            var x2vol0 = 2 * experiment.CellVolume;
            var deltaVolume = 0.0;

            foreach (var inj in experiment.Injections)
            {
                deltaVolume += inj.Volume;
                inj.ActualCellConcentration = experiment.CellConcentration * ((1 - deltaVolume / x2vol0) / (1 + deltaVolume / x2vol0));
                inj.ActualTitrantConcentration = experiment.SyringeConcentration * (deltaVolume / experiment.CellVolume) * (1 - deltaVolume / x2vol0);

                inj.Ratio = experiment.AxisType switch
                {
                    AnalysisXAxisType.ID => (inj.ID + 1),
                    AnalysisXAxisType.TitrantConcentration => inj.ActualTitrantConcentration,
                    _ => inj.ActualTitrantConcentration / inj.ActualCellConcentration,
                };
            }
        }

        static void ProcessInjectionsExponential(ExperimentData experiment)
        {
            var deltaVolume = 0.0;
            var cellVolume = experiment.CellVolume;

            foreach (var inj in experiment.Injections)
            {
                deltaVolume += inj.Volume;

                var dilutionFactor = Math.Exp(-deltaVolume / cellVolume);

                inj.ActualCellConcentration = experiment.CellConcentration * dilutionFactor;
                inj.ActualTitrantConcentration = experiment.SyringeConcentration * (1.0 - dilutionFactor);

                inj.Ratio = experiment.AxisType switch
                {
                    AnalysisXAxisType.ID => (inj.ID + 1),
                    AnalysisXAxisType.TitrantConcentration => inj.ActualTitrantConcentration,
                    _ => inj.ActualTitrantConcentration / inj.ActualCellConcentration,
                };
            }
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
