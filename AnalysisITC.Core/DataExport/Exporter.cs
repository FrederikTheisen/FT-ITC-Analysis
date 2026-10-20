using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Globalization;
using AnalysisITC.Platform;
using AnalysisITC.Core.Analysis;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Export
{
    public class Exporter
    {
        const char Delimiter = ',';
        const char BlankChar = ' ';

        public static void Export(ExportType? type = null, ExportDataSelection? sel = null)
        {
            _ = ExportAsync(type, sel);
        }

        public static async Task ExportAsync(ExportType? type = null, ExportDataSelection? sel = null)
        {
            List<(ExperimentData data, string path)> outputs = null;
            var progressStarted = false;
            try
            {
                var selection = sel.HasValue ? (ExportDataSelection)sel : AppSettings.ExportSelectionMode;
                var settings = ExportAccessoryViewSettings.CreateDefault(type ?? AppSettings.DefaultExportType);
                settings.Selection = selection;
                settings.SetData();

                var folderPath = await PlatformServices.ExportPromptService.ChooseExportFolderAsync(settings);
                if (string.IsNullOrWhiteSpace(folderPath)) return;
                if (!TryPersistSettings(settings)) return;

                outputs = PlanOutputs(folderPath, settings);
                if (outputs.Count == 0)
                {
                    AppEventHandler.DisplayHandledException(new HandledException(
                        HandledException.Severity.Warning, "No Valid Data", "No valid data could be exported."));
                    return;
                }
                var paths = outputs.Select(output => output.path).ToList();
                var approved = ExportBatchWriter.CaptureExistingPaths(paths);
                if (!PlatformServices.ExportPromptService.ConfirmOverwrite(paths)) return;

                StatusBarManager.StartInderminateProgress();
                progressStarted = true;
                StatusBarManager.SetStatusScrolling($"Saving to {folderPath}...");

                var outcome = await WriteOutputs(outputs, settings, approved);
                if (!outcome.IsSuccess)
                    ExportFailurePresentation.Report("Numerical data export", outcome);
            }
            catch (Exception error)
            {
                ExportFailurePresentation.Report("Numerical data export", ExportOutcome.Failure(outputs?.Count ?? 0, error, outputs?.FirstOrDefault().path));
            }
            finally
            {
                if (progressStarted) StatusBarManager.StopIndeterminateProgress();
            }
        }

        internal static Task WriteOutputs(List<(ExperimentData data, string path)> outputs, ExportAccessoryViewSettings settings) =>
            WriteOutputs(outputs, settings, ExportBatchWriter.CaptureExistingPaths(outputs.Select(output => output.path)));

        static async Task<ExportOutcome> WriteOutputs(List<(ExperimentData data, string path)> outputs, ExportAccessoryViewSettings settings, ISet<string> approved)
        {
            Func<ExperimentData, IEnumerable<string>> lines;
            Action setStatus;
            switch (settings.Export)
            {
                case ExportType.InterchangeCsv:
                    lines = data => BuildInterchangeLines(data, settings);
                    setStatus = () => StatusBarManager.SetStatus("Finished exporting combined data", 3000);
                    break;
                case ExportType.Data:
                    lines = data => GetThermogramLines(data, settings);
                    setStatus = () => StatusBarManager.SetStatus("Finished exporting data file", 3000);
                    break;
                case ExportType.Peaks:
                    lines = data => BuildIntegratedPeakLines(data, settings);
                    setStatus = () => StatusBarManager.SetStatus("Finished exporting integrated peaks", 3000);
                    break;
                case ExportType.ITCsim:
                    lines = data => BuildITCsimLines(data, settings);
                    setStatus = () => StatusBarManager.SetStatus("Finished exporting " + MarkdownStrings.ITCsimName, 3000);
                    break;
                case ExportType.CSV:
                    lines = data => GetColumns(data, settings.Columns, settings);
                    setStatus = () => StatusBarManager.SetStatus("Finished exporting peak file", 3000);
                    break;
                case ExportType.MicroCal:
                    lines = BuildMicroCalLines;
                    setStatus = () => StatusBarManager.SetStatus("Finished exporting file", 3000);
                    break;
                case ExportType.PYTC:
                    lines = BuildPytcLines;
                    setStatus = () => StatusBarManager.SetStatus("Finished exporting file for pytc", 3000);
                    break;
                default:
                    lines = data => GetColumns(data, settings.Columns, settings);
                    setStatus = () => StatusBarManager.SetStatus("Finished exporting peak file", 3000);
                    break;
            }

            var paths = outputs.Select(output => output.path).ToList();
            var outcome = await ExportBatchWriter.WriteAsync(paths, approved, (index, stream) => Task.Run(async () =>
            {
                using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), 1024, true))
                    foreach (var line in lines(outputs[index].data)) await writer.WriteLineAsync(line).ConfigureAwait(false);
            }));
            if (outcome.IsSuccess) setStatus();
            return outcome;
        }

        static bool TryPersistSettings(ExportAccessoryViewSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.OutputBaseName) ||
                settings.OutputBaseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                AppEventHandler.DisplayHandledException(new HandledException(
                    HandledException.Severity.Warning,
                    "Invalid Export Name",
                    "Enter an output name without path separators or invalid filename characters."));
                return false;
            }

            AppSettings.DefaultExportType = settings.Export;
            AppSettings.ExportOutputBaseName = settings.OutputBaseName.Trim();
            AppSettings.ExportSelectionMode = settings.Selection;
            AppSettings.UnifyTimeAxisForExport = settings.UnifyTimeAxis;
            AppSettings.ExportBaselineCorrectedData = settings.ExportBaselineCorrectDataPoints;
            AppSettings.ExportFitPointsWithPeaks = settings.ExportFittedPeaks;
            AppSettings.ExportColumns = settings.Columns;
            AppSettings.Save();
            return true;
        }

        /// <summary>
        /// Allocates one unique output path per exported experiment. Experiments whose
        /// sanitized name is unique in the batch keep it; duplicate and blank names take
        /// the next free numeric suffix, so no two experiments share a file.
        /// </summary>
        internal static List<(ExperimentData data, string path)> PlanOutputs(string folderPath, ExportAccessoryViewSettings settings)
        {
            var data = (settings.Export == ExportType.Data
                ? settings.Data.Where(item => item.HasThermogram)
                : settings.Data).ToList();

            var baseName = settings.OutputBaseName.Trim();
            var extension = settings.Export.GetProperties().DotExtension();

            if (data.Count <= 1)
                return data.Select(item => (item, Path.Combine(folderPath, baseName + extension))).ToList();

            var stems = data.Select(item => SanitizeFileName(Path.GetFileNameWithoutExtension(item.Name))).ToList();
            var stemCounts = stems
                .Where(stem => stem != null)
                .GroupBy(stem => stem, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fileNames = new string[data.Count];

            for (int i = 0; i < data.Count; i++)
            {
                if (stems[i] == null || stemCounts[stems[i]] > 1) continue;

                fileNames[i] = baseName + "_" + stems[i] + extension;
                used.Add(fileNames[i]);
            }

            for (int i = 0; i < data.Count; i++)
            {
                if (fileNames[i] != null) continue;

                var stem = stems[i] ?? "experiment";
                for (int n = 1; ; n++)
                {
                    var candidate = baseName + "_" + stem + "_" + n.ToString(CultureInfo.InvariantCulture) + extension;
                    if (used.Add(candidate))
                    {
                        fileNames[i] = candidate;
                        break;
                    }
                }
            }

            return data.Select((item, i) => (item, Path.Combine(folderPath, fileNames[i]))).ToList();
        }

        /// <summary>
        /// Replaces invalid filename characters. Returns null when nothing usable remains.
        /// </summary>
        static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string((value ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
        }

        static List<string> GetThermogramLines(ExperimentData data, ExportAccessoryViewSettings settings)
        {
            var lines = new List<string> { "time_s,power_w" };
            var points = settings.ExportBaselineCorrectDataPoints && data.BaseLineCorrectedDataPoints != null
                ? data.BaseLineCorrectedDataPoints
                : data.DataPoints;

            foreach (var point in points ?? new List<DataPoint>())
            {
                lines.Add(Invariant(point.Time) + Delimiter + Invariant(point.Power));
            }
            return lines;
        }

        /// <summary>
        /// Builds a MicroCal/SEDPHAT integrated-heat table. The legacy contract uses
        /// microcalories for DH and calories per mole for NDH, DY and Fit.
        /// </summary>
        internal static List<string> BuildMicroCalLines(ExperimentData data)
        {
            const string unavailable = "--";
            var lines = new List<string>
            {
                "DH,INJV,Xt,Mt,XMt,NDH,DY,Fit"
            };

            var xt = 0.0;
            var mt = 1000.0 * data.CellConcentration.Value;
            var hasValidFit = data.Model != null && data.Solution?.IsValid == true;

            foreach (var item in data.Injections.Select((injection, index) => new { injection, index }))
            {
                var injection = item.injection;
                var validMass = FWEMath.IsFinite(injection.InjectionMass) && injection.InjectionMass > 0;
                var validHeat = FWEMath.IsFinite(injection.PeakArea.Value);

                var dh = validHeat
                    ? Invariant(Energy.ConvertFromJoule(injection.PeakArea.Value, EnergyUnit.MicroCal))
                    : unavailable;
                var injv = MicroCalValue(1_000_000.0 * injection.Volume);
                var xmt = MicroCalValue(injection.Ratio);
                var ndh = unavailable;
                var dy = unavailable;
                var fit = unavailable;
                double? measuredCalPerMole = null;
                double? fittedCalPerMole = null;

                if (item.index > 0 && validHeat && validMass)
                {
                    measuredCalPerMole = Energy.ConvertFromJoule(
                        injection.PeakArea.Value / injection.InjectionMass,
                        EnergyUnit.Cal);
                    ndh = MicroCalValue(measuredCalPerMole.Value);
                }

                if (hasValidFit && validMass)
                {
                    var fittedJoulesPerMole = data.Model.EvaluateEnthalpy(injection.ID, withoffset: true);
                    if (FWEMath.IsFinite(fittedJoulesPerMole))
                    {
                        fittedCalPerMole = Energy.ConvertFromJoule(fittedJoulesPerMole, EnergyUnit.Cal);
                        fit = MicroCalValue(fittedCalPerMole.Value);
                    }
                }

                if (item.index > 0 && measuredCalPerMole.HasValue && fittedCalPerMole.HasValue)
                    dy = MicroCalValue(measuredCalPerMole.Value - fittedCalPerMole.Value);

                lines.Add(string.Join(Delimiter.ToString(), new[]
                {
                    dh,
                    injv,
                    MicroCalValue(xt),
                    MicroCalValue(mt),
                    xmt,
                    ndh,
                    dy,
                    fit,
                }));

                xt = 1000.0 * injection.ActualTitrantConcentration;
                mt = 1000.0 * injection.ActualCellConcentration;
            }

            lines.Add(string.Join(Delimiter.ToString(), new[]
            {
                string.Empty,
                unavailable,
                MicroCalValue(xt),
                MicroCalValue(mt),
                unavailable,
                string.Empty,
                string.Empty,
                string.Empty,
            }));

            return lines;
        }

        static string MicroCalValue(double value) =>
            FWEMath.IsFinite(value) ? Invariant(value) : "--";

        static List<string> BuildPytcLines(ExperimentData data)
        {
            var lines = new List<string>
            {
                "10",
                "0," + data.InjectionCount.ToString(CultureInfo.InvariantCulture) + ",0,0,0",
                data.MeasuredTemperature.ToString("F3", CultureInfo.InvariantCulture) + "," + (1000 * data.CellConcentration).Value.ToString("F4", CultureInfo.InvariantCulture) + "," + (1000 * data.SyringeConcentration).Value.ToString("F4", CultureInfo.InvariantCulture) + "," + (1000 * data.CellVolume).ToString("F5", CultureInfo.InvariantCulture),
                "0",
                "0",
            };
            foreach (var injection in data.Injections)
                lines.Add((1000000 * injection.Volume).ToString("F2", CultureInfo.InvariantCulture) + "," + injection.PeakArea.Energy.ToUnit(EnergyUnit.MicroCal).Value.ToString("F5", CultureInfo.InvariantCulture));
            return lines;
        }

        internal static List<string> BuildIntegratedPeakLines(ExperimentData data, ExportAccessoryViewSettings settings)
        {
            var lines = new List<string> { BuildPeakHeader(data, settings.ExportEnergyUnit) };
            foreach (var injection in data.Injections ?? new List<InjectionData>())
                lines.Add(string.Join(Delimiter.ToString(), BuildPeakValues(data, injection, settings.ExportOffsetCorrected, settings.ExportEnergyUnit)));
            return lines;
        }

        internal static List<string> BuildInterchangeLines(ExperimentData data, ExportAccessoryViewSettings settings)
        {
            var lines = new List<string> { "time_s,raw_power_w,corrected_power_w," + BuildPeakHeader(data, settings.ExportEnergyUnit) };
            var rawPoints = data.DataPoints ?? new List<DataPoint>();
            var correctedPoints = data.BaseLineCorrectedDataPoints;
            var injections = data.Injections ?? new List<InjectionData>();
            var rowCount = Math.Max(rawPoints.Count, injections.Count);
            for (var index = 0; index < rowCount; index++)
            {
                var traceValues = index < rawPoints.Count
                    ? new[] { Invariant(rawPoints[index].Time), Invariant(rawPoints[index].Power), correctedPoints != null && index < correctedPoints.Count ? Invariant(correctedPoints[index].Power) : "" }
                    : new[] { "", "", "" };
                var peakValues = index < injections.Count
                    ? BuildPeakValues(data, injections[index], offsetCorrected: false, settings.ExportEnergyUnit)
                    : new[] { "", "", "", "", "" };
                lines.Add(string.Join(Delimiter.ToString(), traceValues.Concat(peakValues)));
            }
            return lines;
        }

        internal static List<string> BuildITCsimLines(ExperimentData data, ExportAccessoryViewSettings settings)
        {
            var lines = GetColumns(data, ExportColumns.SelectionITCsim, settings);
            lines.AddRange(GetMetaData(data, settings.ExportEnergyUnit));
            return lines;
        }

        static string BuildPeakHeader(ExperimentData data, EnergyUnit unit)
        {
            var suffix = unit == EnergyUnit.Joule ? "j_per_mol" : unit switch
            {
                EnergyUnit.KiloJoule => "kj_per_mol",
                EnergyUnit.Cal => "cal_per_mol",
                EnergyUnit.KCal => "kcal_per_mol",
                _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported molar-energy export unit.")
            };
            return GetXAxisHeader(data) + $",integrated_enthalpy_{suffix},sd_{suffix},model_{suffix},residual_{suffix}";
        }

        internal static string[] BuildPeakValues(ExperimentData data, InjectionData injection, bool offsetCorrected, EnergyUnit unit)
        {
            var peak = offsetCorrected ? injection.OffsetEnthalpy : injection.Enthalpy;
            var fit = data.Solution != null && data.Model != null
                ? data.Model.EvaluateEnthalpy(injection.ID, !offsetCorrected)
                : double.NaN;
            var residual = double.IsNaN(fit) ? double.NaN : peak - fit;
            return new[]
            {
                Invariant(GetXAxisValue(data, injection)),
                Invariant(Energy.ConvertFromJoule(peak, unit)),
                Invariant(Energy.ConvertFromJoule(injection.SD, unit)),
                Invariant(Energy.ConvertFromJoule(fit, unit)),
                Invariant(Energy.ConvertFromJoule(residual, unit))
            };
        }

        static string GetXAxisHeader(ExperimentData data)
        {
            return data.AxisType switch
            {
                AnalysisXAxisType.TitrantConcentration => "titrant_concentration_m",
                AnalysisXAxisType.ID => "injection_number",
                _ => "molar_ratio"
            };
        }

        static double GetXAxisValue(ExperimentData data, InjectionData injection)
        {
            return data.AxisType switch
            {
                AnalysisXAxisType.TitrantConcentration => injection.ActualTitrantConcentration,
                AnalysisXAxisType.ID => injection.ID + 1,
                _ => injection.Ratio
            };
        }

        static string Invariant(double value) => !double.IsNaN(value) && !double.IsInfinity(value) ? value.ToString("G17", CultureInfo.InvariantCulture) : "";
        static string Invariant(float value) => !float.IsNaN(value) && !float.IsInfinity(value) ? value.ToString("G9", CultureInfo.InvariantCulture) : "";

        static List<string> GetColumns(ExperimentData data, ExportColumns columns, ExportAccessoryViewSettings settings)
        {
            var lines = new List<string>();

            // Build column header
            string header = "";

            for (int i = 1; i < 9999; i *= 2)
            {
                if (!Enum.IsDefined(typeof(ExportColumns), i)) { break; } // We are through the list of valid enums, break
                if (!columns.HasFlag((ExportColumns)i)) { continue; } // Enum not in selection, try next
                if (header.Length > 0) header += Delimiter.ToString();

                header += ExportColumnHandler.GetColumnHeader((ExportColumns)i);
            }

            lines.Add(header);

            // Build file
            for (int j = 0; j < data.InjectionCount; j++)
            {
                var line = "";

                for (int i = 1; i < 9999; i *= 2)
                {
                    if (!Enum.IsDefined(typeof(ExportColumns), i)) { break; } // We are through the list of valid enums, break
                    if (!columns.HasFlag((ExportColumns)i)) { continue; } // Enum not in selection, try next
                    if (line.Length > 0) line += Delimiter.ToString();

                    line += ExportColumnHandler.GetColumnValue((ExportColumns)i, data, j, settings);
                }

                lines.Add(line);
            }

            return lines;
        }

        static List<string> GetMetaData(ExperimentData data, EnergyUnit energyUnit)
        {
            var lines = new List<string>
            {
                "#ITCSIM METADATA LIST",
                "#EXPINFO ENERGYUNIT " + ExportFormatDescription.ITCsimEnergyUnitToken(energyUnit),
                "#EXPINFO CELLCONC " + (1000000*data.CellConcentration).Value.ToString("F2", CultureInfo.InvariantCulture) + " uM",
                "#EXPINFO SYRINGECONC " + (1000000*data.SyringeConcentration).Value.ToString("F2", CultureInfo.InvariantCulture) + " uM",
                "#EXPINFO CELLVOLUME " + data.CellVolume.ToString("F9", CultureInfo.InvariantCulture) + " L"
            };

            return lines;
        }

        public static void CopyToClipboard(AnalysisResult analysis, ConcentrationUnit kdunit, EnergyUnit eunit, bool usekelvin)
        {
            CopyToClipboard(analysis, FamilyForEnergyUnit(eunit), eunit, usekelvin, kdunit);
        }

        public static void CopyToClipboard(AnalysisResult analysis, EnergyUnit eunit, bool usekelvin)
        {
            CopyToClipboard(analysis, FamilyForEnergyUnit(eunit), eunit, usekelvin);
        }

        public static void CopyToClipboard(AnalysisResult analysis, EnergyUnitFamily family, EnergyUnit? energyUnitOverride, bool usekelvin)
        {
            CopyToClipboard(analysis, family, energyUnitOverride, usekelvin, null);
        }

        public static void CopyToClipboard(AnalysisResult analysis, EnergyUnitFamily family, bool usekelvin)
        {
            CopyToClipboard(analysis, family, null, usekelvin);
        }

        static void CopyToClipboard(AnalysisResult analysis, EnergyUnitFamily family,
            EnergyUnit? energyUnitOverride, bool usekelvin, ConcentrationUnit? concentrationUnitOverride)
        {
            if (analysis == null) throw new ArgumentNullException(nameof(analysis));
            var table = AnalysisResultTableExporter.Build(new[] { analysis }, new AnalysisResultExportOptions
            {
                RowMode = AnalysisResultExportRowMode.AllRows,
                ErrorStyle = AnalysisResultExportErrorStyle.SeparateColumns,
                FileFormat = AnalysisResultExportFileFormat.TSV,
                UncertaintyDisplayStyle = AppSettings.UncertaintyDisplayStyle,
                EnergyUnitFamily = family,
                EnergyUnitOverride = energyUnitOverride,
                ConcentrationUnitOverride = concentrationUnitOverride,
                UseKelvin = usekelvin,
            });
            PlatformServices.ClipboardService.SetString(table);
            StatusBarManager.SetStatus("Results copied to clipboard", 3333);
        }

        static EnergyUnitFamily FamilyForEnergyUnit(EnergyUnit unit)
            => unit == EnergyUnit.MicroCal || unit == EnergyUnit.Cal || unit == EnergyUnit.KCal
                ? EnergyUnitFamily.Calories : EnergyUnitFamily.Joules;

        class ExportColumnHandler
        {
            public static string GetColumnHeader(ExportColumns column)
            {
                switch (column)
                {
                    case ExportColumns.MolarRatio: return "MolarRatio";
                    case ExportColumns.Included: return "Included";
                    case ExportColumns.Peak: return "PeakHeat";
                    case ExportColumns.Fit: return "Fit";
                    case ExportColumns.InjectionVolume: return "InjVolume";
                    case ExportColumns.InjectionDelay: return "InjDelay";
                    case ExportColumns.CellConc: return "[cell]";
                    case ExportColumns.SyrConc: return "[syr]";
                    case ExportColumns.PeakError: return "PeakHeatError";
                    case ExportColumns.IntegrationLength: return "PeakIntegrationLength";
                    case ExportColumns.Temperature: return "Temperature";
                    default: return "unknown_column_selection_" + column.ToString();
                }
            }

            /// <summary>
            /// Returns a formatted injection value for the configurable CSV exports.
            /// </summary>
            public static string GetColumnValue(ExportColumns column, ExperimentData data, int i, ExportAccessoryViewSettings settings)
            {
                if (data == null) throw new Exception("No data selected");
                if (data.Injections == null) throw new Exception("Data does not contain injection information");
                if (data.Injections.Count < i) return "";

                var inj = data.Injections[i];

                switch (column)
                {
                    case ExportColumns.MolarRatio: return inj.Ratio.ToString("F5", CultureInfo.InvariantCulture);
                    case ExportColumns.Included: return inj.Include ? "1" : "0";
                    case ExportColumns.InjectionVolume: return inj.Volume.ToString("E2", CultureInfo.InvariantCulture);
                    case ExportColumns.InjectionDelay: return inj.Delay.ToString(CultureInfo.InvariantCulture);
                    case ExportColumns.CellConc: return inj.ActualCellConcentration.ToString("F8", CultureInfo.InvariantCulture);
                    case ExportColumns.SyrConc: return inj.ActualTitrantConcentration.ToString("F8", CultureInfo.InvariantCulture);
                    case ExportColumns.Peak:
                        var molarHeat = settings.ExportOffsetCorrected ? inj.OffsetEnthalpy : inj.Enthalpy;
                        return settings.Export == ExportType.ITCsim
                            ? Invariant(Energy.ConvertFromJoule(molarHeat, settings.ExportEnergyUnit))
                            : molarHeat.ToString("F3", CultureInfo.InvariantCulture);
                    case ExportColumns.PeakError: return inj.SD.ToString("F2", CultureInfo.InvariantCulture);
                    case ExportColumns.Temperature: return inj.Temperature.ToString("F2", CultureInfo.InvariantCulture);
                    case ExportColumns.IntegrationLength: return inj.IntegrationEndOffset.ToString("F1", CultureInfo.InvariantCulture);
                    case ExportColumns.Fit:
                        if (data.Solution != null) return data.Model.EvaluateEnthalpy(i, !settings.ExportOffsetCorrected).ToString("F3", CultureInfo.InvariantCulture);
                        else return BlankChar.ToString();
                    default: return BlankChar.ToString();
                }
            }
        }
    }
}
