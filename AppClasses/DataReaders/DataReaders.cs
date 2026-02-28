using System;
using AnalysisITC;
using System.Collections.Generic;
using System.Linq;
using AppKit;
using System.Threading.Tasks;
using UniformTypeIdentifiers;
using Foundation;
using AnalysisITC.AppClasses.AnalysisClasses;
using System.Text.RegularExpressions;
using System.Globalization;

namespace DataReaders
{
    public static class DataReader
    {
        static void AddData(ITCDataContainer data)
        {
            if (data == null) return;
            bool valid = true;
            if (data is ExperimentData) valid = ImportValidator.ValidateData(data as ExperimentData);

            if (valid) DataManager.AddData(data);
        }

        static void AddData(ITCDataContainer[] data)
        {
            foreach (var dat in data)
            {
                AddData(dat);
            }
        }

        public static ITCDataFormat GetFormat(string path)
        {
            try
            {
                var ext = System.IO.Path.GetExtension(path);

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

        public static async void Read(NSUrl url) => Read(new NSUrl[] { url });

        public static async void Read(IEnumerable<NSUrl> urls)
        {
            StatusBarManager.SetStatus("Reading data...", 0);
            StatusBarManager.StartInderminateProgress();

            try
            {
                await Task.Delay(1);

                foreach (var url in urls)
                {
                    StatusBarManager.SetStatus("Reading file: " + url.LastPathComponent, 0);
                    await Task.Delay(1); //Necessary to update UI. Unclear why whole method has to be on UI thread.
                    var dat = ReadFile(url.Path);

                    if (dat != null)
                    {
                        AddData(dat);

                        NSDocumentController.SharedDocumentController.NoteNewRecentDocumentURL(url);
                        AppSettings.LastDocumentUrl = url;
                    }
                }
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }

            StatusBarManager.ClearAppStatus();
            StatusBarManager.StopIndeterminateProgress();
        }

        static ITCDataContainer[] ReadFile(string path)
        {
            try
            {
                var format = GetFormat(path);

                switch (format)
                {
                    case ITCDataFormat.FTITC:
                        return FTITCReader.ReadPath(path);
                    case ITCDataFormat.VPITC: // TODO No idea what vpitc files might look like if they exist
                    case ITCDataFormat.ITC200:
                        return new ExperimentData[] { MicroCalITC200Reader.ReadPath(path) };
                    case ITCDataFormat.TAITC:
                        return new ExperimentData[] { TAFileReader.ReadPath(path) };
                    case ITCDataFormat.IntegratedHeats:
                        return new ExperimentData[] { IntegratedHeatReader.ReadFile(path) };
                    case ITCDataFormat.PEAQITC:
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

            AppEventHandler.PrintAndLog("Proceesing injections for: " + experiment.FileName);

            switch (experiment.DataSourceFormat)
            {
                case ITCDataFormat.TAITC:
                    foreach (var inj in experiment.Injections) inj.InitializeIntegrationTimes();
                    ProcessInjectionsMicroCal(experiment); // We think this might be the same processing. Lack of information makes other assumptions hard.
                    break;
                default:
                case ITCDataFormat.PEAQITC:
                case ITCDataFormat.IntegratedHeats: // We absolutely need to recalculate concentrations since some programs export far too low precision 
                case ITCDataFormat.ITC200:
                    ProcessInjectionsMicroCal(experiment);
                    break;
            }
        }

        static void ProcessInjectionsMicroCal(ExperimentData experiment)
        {
            var x2vol0 = 2 * experiment.CellVolume;
            var deltaVolume = 0.0;

            foreach (var inj in experiment.Injections)
            {
                deltaVolume += inj.Volume;
                inj.InjectionMass = experiment.SyringeConcentration * inj.Volume;
                inj.ActualCellConcentration = experiment.CellConcentration * ((1 - deltaVolume / x2vol0) / (1 + deltaVolume / x2vol0));
                inj.ActualTitrantConcentration = experiment.SyringeConcentration * (deltaVolume / experiment.CellVolume) * (1 - deltaVolume / x2vol0);

                if (inj.ActualCellConcentration > float.Epsilon) inj.Ratio = inj.ActualTitrantConcentration / inj.ActualCellConcentration;
                else inj.Ratio = inj.ActualTitrantConcentration;
            }
        }

        /// <summary>
        /// Determine derived properties and try parse the comment for attributes
        /// </summary>
        /// <param name="experiment"></param>
        public static void ProcessExperiment(ExperimentData experiment)
        {
            experiment.MeasuredTemperature = experiment.DataPoints.Average(dp => dp.Temperature);

            // Try to extract attributes from comments
            if (!string.IsNullOrEmpty(experiment.Comments))
            {
                var comment = experiment.Comments;

                // Global pH fallback (if comment says "pH 7.4" once, apply to buffers that don’t have a local pH match)
                double? globalPH = null;
                {
                    var m = Regex.Match(comment, @"\bpH\s*[:=]?\s*([+-]?\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
                    if (m.Success && TryParseNumber(m.Groups[1].Value, out var ph)) globalPH = ph;
                }

                // Special buffers (expand into explicit components)
                if (Regex.IsMatch(comment, @"\b(1x)?PBS\b", RegexOptions.IgnoreCase))
                    BufferAttribute.SetupSpecialBuffer(experiment.Attributes, AnalysisITC.Buffer.PBS);
                if (Regex.IsMatch(comment, @"\b(1x)?TBS\b", RegexOptions.IgnoreCase))
                    BufferAttribute.SetupSpecialBuffer(experiment.Attributes, AnalysisITC.Buffer.TBS);

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

                    if (AnalysisITC.BufferAttribute.TryExtractPH(comment, matchedName, out var pH)) att.DoubleValue = pH;
                    else if (globalPH.HasValue) att.DoubleValue = globalPH.Value;

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
