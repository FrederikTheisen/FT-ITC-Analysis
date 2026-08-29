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
            var selection = sel.HasValue ? (ExportDataSelection)sel : AppSettings.ExportSelectionMode;
            var settings = ExportAccessoryViewSettings.CreateDefault(type ?? AppSettings.DefaultExportType);
            settings.Selection = selection;
            settings.SetData();

            var folderPath = await PlatformServices.ExportPromptService.ChooseExportFolderAsync(settings);
            if (string.IsNullOrWhiteSpace(folderPath)) return;

            if (!TryPersistSettings(settings)) return;

            var outputPaths = GetPlannedOutputPaths(folderPath, settings);
            if (!PlatformServices.ExportPromptService.ConfirmOverwrite(outputPaths)) return;

            StatusBarManager.StartInderminateProgress();

            try
            {
                StatusBarManager.SetStatusScrolling($"Saving to {folderPath}...");

                switch (settings.Export)
                {
                    case ExportType.InterchangeCsv:
                        await WriteInterchangeFiles(folderPath, settings);
                        break;
                    case ExportType.Data:
                        await WriteThermogramFiles(folderPath, settings);
                        break;

                    case ExportType.Peaks:
                        await WriteIntegratedPeakFiles(folderPath, settings);
                        break;

                    case ExportType.ITCsim:
                        await WriteITCsimFile(folderPath, settings);
                        break;

                    default:
                    case ExportType.CSV:
                        await WritePeakFile(folderPath, settings, settings.Columns);
                        break;

                    case ExportType.MicroCal:
                        await WriteMicroCalExportFile(folderPath, settings);
                        break;

                    case ExportType.PYTC:
                        await WritePytcExportFile(folderPath, settings);
                        break;
                }
            }
            finally
            {
                StatusBarManager.StopIndeterminateProgress();
            }
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

        static List<string> GetPlannedOutputPaths(string folderPath, ExportAccessoryViewSettings settings)
        {
            var data = settings.Export == ExportType.Data
                ? settings.Data.Where(item => item.HasThermogram)
                : settings.Data;

            return data
                .Select((data, index) => Path.Combine(folderPath, BuildOutputFileName(settings, data, index)))
                .ToList();
        }

        static string BuildOutputFileName(ExportAccessoryViewSettings settings, ExperimentData data, int index = 0)
        {
            var baseName = settings.OutputBaseName.Trim();
            if (data != null && settings.Data.Count > 1)
            {
                var experimentName = SanitizeFileName(Path.GetFileNameWithoutExtension(data.Name), index);
                baseName += "_" + experimentName;
                if (settings.Data.Count(item => string.Equals(
                        SanitizeFileName(Path.GetFileNameWithoutExtension(item.Name), 0),
                        experimentName,
                        StringComparison.OrdinalIgnoreCase)) > 1)
                    baseName += "_" + (index + 1).ToString(CultureInfo.InvariantCulture);
            }

            return baseName + settings.Export.GetProperties().DotExtension();
        }

        static string SanitizeFileName(string value, int index)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string((value ?? "experiment").Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? $"experiment_{index + 1}" : sanitized;
        }

        static async Task WriteThermogramFiles(string path, ExportAccessoryViewSettings settings)
        {
            await Task.Run(async () =>
            {
                var exportdata = settings.Data.Where(d => d.HasThermogram).ToList();

                if (exportdata.Count == 0)
                {
                    AppEventHandler.DisplayHandledException(new HandledException(HandledException.Severity.Warning, "No Valid Data", "No valid data could be exported"));
                    return;
                }

                foreach (var pair in exportdata.Select((data, index) => new { data, index }))
                {
                    var output = Path.Combine(path, BuildOutputFileName(settings, pair.data, pair.index));
                    using var writer = new StreamWriter(output);
                    foreach (var line in GetThermogramLines(pair.data, settings))
                        await writer.WriteLineAsync(line);
                }
            });

            StatusBarManager.SetStatus("Finished exporting data file", 3000);
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

        static async Task WritePeakFile(string path, ExportAccessoryViewSettings settings, ExportColumns columns)
        {
            await Task.Run(async () =>
            {
                foreach (var pair in settings.Data.Select((data, index) => new { data, index }))
                {
                    var output = Path.Combine(path, BuildOutputFileName(settings, pair.data, pair.index));

                    var lines = GetColumns(pair.data, columns, settings);

                    using (var writer = new StreamWriter(output))
                    {
                        foreach (var line in lines)
                        {
                            await writer.WriteLineAsync(line);
                        }
                    }
                }
            });

            StatusBarManager.SetStatus("Finished exporting peak file", 3000);
        }

        static async Task WriteIntegratedPeakFiles(string path, ExportAccessoryViewSettings settings)
        {
            await Task.Run(async () =>
            {
                foreach (var pair in settings.Data.Select((data, index) => new { data, index }))
                {
                    var output = Path.Combine(path, BuildOutputFileName(settings, pair.data, pair.index));
                    using var writer = new StreamWriter(output);
                    await writer.WriteLineAsync(BuildPeakHeader(pair.data));
                    foreach (var injection in pair.data.Injections ?? new List<InjectionData>())
                        await writer.WriteLineAsync(string.Join(Delimiter.ToString(), BuildPeakValues(pair.data, injection, settings.ExportOffsetCorrected)));
                }
            });

            StatusBarManager.SetStatus("Finished exporting integrated peaks", 3000);
        }

        static async Task WriteITCsimFile(string path, ExportAccessoryViewSettings settings)
        {
            await Task.Run(async () =>
            {
                foreach (var pair in settings.Data.Select((data, index) => new { data, index }))
                {
                    var output = Path.Combine(path, BuildOutputFileName(settings, pair.data, pair.index));

                    var lines = GetColumns(pair.data, ExportColumns.SelectionITCsim, settings);

                    lines.AddRange(GetMetaData(pair.data));

                    using (var writer = new StreamWriter(output))
                    {
                        foreach (var line in lines)
                        {
                            await writer.WriteLineAsync(line);
                        }
                    }
                }
            });

            StatusBarManager.SetStatus("Finished exporting " + MarkdownStrings.ITCsimName, 3000);
        }

        static async Task WriteMicroCalExportFile(string path, ExportAccessoryViewSettings settings)
        {
            await Task.Run(async () =>
            {
                foreach (var pair in settings.Data.Select((data, index) => new { data, index }))
                {
                    var data = pair.data;
                    var output = Path.Combine(path, BuildOutputFileName(settings, data, pair.index));

                    using (var writer = new StreamWriter(output))
                    {
                        foreach (var line in BuildMicroCalLines(data))
                        {
                            await writer.WriteLineAsync(line);
                        }
                    }
                }
            });

            StatusBarManager.SetStatus("Finished exporting file", 3000);
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

        static async Task WritePytcExportFile(string path, ExportAccessoryViewSettings settings)
        {
            await Task.Run(async () =>
            {
                foreach (var pair in settings.Data.Select((data, index) => new { data, index }))
                {
                    var data = pair.data;
                    var output = Path.Combine(path, BuildOutputFileName(settings, data, pair.index));

                    var lines = new List<string>
                    {
                        "10", // ?
                        "0," + data.InjectionCount.ToString() + ",0,0,0",
                        data.MeasuredTemperature.ToString("F3") + "," + (1000 * data.CellConcentration).Value.ToString("F4") + "," + (1000 * data.SyringeConcentration).Value.ToString("F4") + "," + (1000 * data.CellVolume).ToString("F5"),
                        "0", // ?
                        "0", // ?
                    };

                    foreach (var inj in data.Injections)
                    {
                        string line = (1000000 * inj.Volume).ToString("F2") + "," + inj.PeakArea.Energy.ToUnit(EnergyUnit.MicroCal).Value.ToString("F5");

                        lines.Add(line);
                    }

                    using (var writer = new StreamWriter(output))
                    {
                        foreach (var line in lines)
                        {
                            await writer.WriteLineAsync(line);
                        }
                    }
                }
            });

            StatusBarManager.SetStatus("Finished exporting file for pytc", 3000);
        }

        static async Task WriteInterchangeFiles(string path, ExportAccessoryViewSettings settings)
        {
            await Task.Run(async () =>
            {
                foreach (var pair in settings.Data.Select((data, index) => new { data, index }))
                {
                    var output = Path.Combine(path, BuildOutputFileName(settings, pair.data, pair.index));
                    using var writer = new StreamWriter(output);
                    await writer.WriteLineAsync("time_s,raw_power_w,corrected_power_w," + BuildPeakHeader(pair.data));

                    var rawPoints = pair.data.DataPoints ?? new List<DataPoint>();
                    var correctedPoints = pair.data.BaseLineCorrectedDataPoints;
                    var injections = pair.data.Injections ?? new List<InjectionData>();
                    var rowCount = Math.Max(rawPoints.Count, injections.Count);

                    for (var index = 0; index < rowCount; index++)
                    {
                        var traceValues = index < rawPoints.Count
                            ? new[]
                            {
                                Invariant(rawPoints[index].Time),
                                Invariant(rawPoints[index].Power),
                                correctedPoints != null && index < correctedPoints.Count ? Invariant(correctedPoints[index].Power) : ""
                            }
                            : new[] { "", "", "" };
                        var peakValues = index < injections.Count
                            ? BuildPeakValues(pair.data, injections[index], offsetCorrected: false)
                            : new[] { "", "", "", "", "" };
                        await writer.WriteLineAsync(string.Join(Delimiter.ToString(), traceValues.Concat(peakValues)));
                    }
                }
            });

            StatusBarManager.SetStatus("Finished exporting combined data", 3000);
        }

        static string BuildPeakHeader(ExperimentData data)
        {
            return GetXAxisHeader(data) + ",integrated_enthalpy_j_per_mol,sd_j_per_mol,model_j_per_mol,residual_j_per_mol";
        }

        static string[] BuildPeakValues(ExperimentData data, InjectionData injection, bool offsetCorrected)
        {
            var peak = offsetCorrected ? injection.OffsetEnthalpy : injection.Enthalpy;
            var fit = data.Solution != null && data.Model != null
                ? data.Model.EvaluateEnthalpy(injection.ID, !offsetCorrected)
                : double.NaN;
            var residual = double.IsNaN(fit) ? double.NaN : peak - fit;
            return new[]
            {
                Invariant(GetXAxisValue(data, injection)),
                Invariant(peak),
                Invariant(injection.SD),
                Invariant(fit),
                Invariant(residual)
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

        static List<string> GetMetaData(ExperimentData data)
        {
            var lines = new List<string>
            {
                "#ITCSIM METADATA LIST",
                "#EXPINFO CELLCONC " + (1000000*data.CellConcentration).ToString("F2") + " uM",
                "#EXPINFO SYRINGECONC " + (1000000*data.SyringeConcentration).ToString("F2") + " uM",
                "#EXPINFO CELLVOLUME " + data.CellVolume.ToString("F9") + " L"
            };

            return lines;
        }

        public static void CopyToClipboard(AnalysisResult analysis, ConcentrationUnit kdunit, EnergyUnit eunit, bool usekelvin)
        {
            CopyToClipboard(analysis, _ => kdunit, eunit, usekelvin);
        }

        public static void CopyToClipboard(AnalysisResult analysis, EnergyUnit eunit, bool usekelvin)
        {
            if (analysis == null) throw new ArgumentNullException(nameof(analysis));
            CopyToClipboard(analysis, analysis.GetAppropriateAffinityUnit, eunit, usekelvin);
        }

        public static void CopyToClipboard(AnalysisResult analysis, EnergyUnitFamily family, EnergyUnit? energyUnitOverride, bool usekelvin)
        {
            if (analysis == null) throw new ArgumentNullException(nameof(analysis));

            var energyValues = analysis.Solution?.Solutions
                ?.SelectMany(solution => solution.ReportParameters
                    .Where(item => ParameterTypeAttribute.IsEnergyUnitParameter(item.Key)))
                .ToList() ?? new List<KeyValuePair<ParameterType, FloatWithError>>();
            var molarValues = energyValues
                .Where(item => !IsHeatCapacityParameter(item.Key))
                .Select(item => item.Value.Value)
                .ToList();
            var heatCapacityValues = energyValues
                .Where(item => IsHeatCapacityParameter(item.Key))
                .Select(item => item.Value.Value)
                .ToList();
            heatCapacityValues.AddRange(analysis.Solution?.TemperatureDependence?.Values
                .Select(dependence => dependence.Slope.Value)
                ?? Enumerable.Empty<double>());
            if (analysis.IsProtonationAnalysisEnabled)
            {
                molarValues.AddRange(analysis.Solution.Solutions
                    .Select(solution => BufferAttribute.TryGetProtonationEnthalpy(solution.Data, out var value) ? value.Value : double.NaN));
            }

            var molar = EnergyUnitResolver.Resolve(family, energyUnitOverride, molarValues);
            var heatCapacity = EnergyUnitResolver.Resolve(family, energyUnitOverride, heatCapacityValues);
            CopyToClipboard(analysis, analysis.GetAppropriateAffinityUnit, molar, heatCapacity, usekelvin);
        }

        public static void CopyToClipboard(AnalysisResult analysis, EnergyUnitFamily family, bool usekelvin)
        {
            CopyToClipboard(analysis, family, null, usekelvin);
        }

        static void CopyToClipboard(
            AnalysisResult analysis,
            Func<ParameterType, ConcentrationUnit> affinityUnit,
            EnergyUnit eunit,
            bool usekelvin)
        {
            CopyToClipboard(analysis, affinityUnit, eunit, eunit, usekelvin);
        }

        static void CopyToClipboard(
            AnalysisResult analysis,
            Func<ParameterType, ConcentrationUnit> affinityUnit,
            EnergyUnit molarEnergyUnit,
            EnergyUnit heatCapacityUnit,
            bool usekelvin)
        {
            var solution = analysis.Solution;
            var delimiter = ",";
            var lines = new List<string>()
            {
                string.Join(delimiter, Header())
            };

            foreach (var sol in solution.Solutions)
            {
                var line = new List<string>
                {
                    sol.Data.Name,
                    (usekelvin ? sol.TempKelvin : sol.Temp).ToString("F2")
                };

                if (analysis.IsElectrostaticsAnalysisDependenceEnabled)
                    line.Add((1000 * BufferAttribute.GetIonicStrength(sol.Data)).ToString("F2"));

                if (analysis.IsProtonationAnalysisEnabled)
                    line.Add(BufferAttribute.TryGetProtonationEnthalpy(sol.Data, out var enthalpy)
                        ? enthalpy.ToString(molarEnergyUnit, "F1", withunit: false)
                        : "");

                foreach (var par in sol.ReportParameters)
                {
                    if (par.Key == ParameterType.Nvalue1 || par.Key == ParameterType.Nvalue2)
                    {
                        line.Add(par.Value.ToString("F3"));
                    }
                    else if (IsAffinityParameter(par.Key))
                    {
                        line.Add(par.Value.AsConcentration(affinityUnit(par.Key), withunit: false));
                    }
                    else
                    {
                        line.Add(new Energy(par.Value).ToString(IsHeatCapacityParameter(par.Key) ? heatCapacityUnit : molarEnergyUnit, formatter: "G3", withunit: false));
                    }
                }
                lines.Add(string.Join(delimiter, line).Replace("±", delimiter));
            }

            // Add line with averages
            var averageline = new List<string>
            {
                "mean",
                solution.Solutions.Average(sol => usekelvin ? sol.TempKelvin : sol.Temp).ToString("F2"),
            };
            if (analysis.IsElectrostaticsAnalysisDependenceEnabled)
                averageline.Add("-");

            if (analysis.IsProtonationAnalysisEnabled)
                averageline.Add("-");

            foreach (var par in solution.Solutions[0].ReportParameters)
            {
                var avg = new FloatWithError(solution.Solutions.Select(sol => sol.ReportParameters[par.Key]).ToList());

                if (par.Key == ParameterType.Nvalue1 || par.Key == ParameterType.Nvalue2)
                {
                    averageline.Add(avg.ToString("F3"));
                }
                else if (IsAffinityParameter(par.Key))
                {
                    averageline.Add(avg.AsConcentration(affinityUnit(par.Key), withunit: false));
                }
                else
                {
                    averageline.Add(new Energy(avg).ToString(IsHeatCapacityParameter(par.Key) ? heatCapacityUnit : molarEnergyUnit, formatter: "G3", withunit: false));
                }
            }

            lines.Add(string.Join(delimiter, averageline).Replace("±", delimiter));

            var paste = string.Join(Environment.NewLine, lines);

            PlatformServices.ClipboardService.SetString(paste);

            StatusBarManager.SetStatus("Results copied to clipboard", 3333);

            List<string> Header()
            {
                List<string> header = new() { "exp", "temperature" };

                if (analysis.IsElectrostaticsAnalysisDependenceEnabled) header.Add("IS(mM)");
                if (analysis.IsProtonationAnalysisEnabled) header.Add("∆Hbufferprotonation(" + molarEnergyUnit.GetUnit() + ")");

                var options = solution.Solutions[0].ModelOptions;

                foreach (var par in solution.IndividualModelReportParameters)
                {
                    var unit = IsAffinityParameter(par)
                        ? affinityUnit(par)
                        : AppSettings.DefaultConcentrationUnit;
                    var containsMultiple = ThermodynamicParameterSlots.TryResolve(par, out _, out _)
                        ? ThermodynamicParameterSlots.FamilyMemberCount(
                            solution.IndividualModelReportParameters,
                            par) > 1
                        : solution.Solutions[0].ParametersConformingToKey(par).Count > 1;
                    var s = ParameterTypeAttribute.TableHeader(
                        options,
                        par,
                        containsMultiple,
                        IsHeatCapacityParameter(par) ? heatCapacityUnit : molarEnergyUnit,
                        unit.GetName());

                    header.Add(s + "_value");
                    header.Add(s + "_sd");
                }

                return header;
            }

            static bool IsAffinityParameter(ParameterType key)
            {
                return key == ParameterType.ApparentAffinity
                    || key.GetProperties().ParentType == ParameterType.Affinity1;
            }

        }

        static bool IsHeatCapacityParameter(ParameterType key)
        {
            return key.GetProperties().ParentType == ParameterType.HeatCapacity1;
        }

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

            public static string GetColumnValue(ExportColumns column, ExperimentData data, int i, ExportAccessoryViewSettings settings)
            {
                if (data == null) throw new Exception("No data selected");
                if (data.Injections == null) throw new Exception("Data does not contain injection information");
                if (data.Injections.Count < i) return "";

                var inj = data.Injections[i];

                switch (column)
                {
                    case ExportColumns.MolarRatio: return inj.Ratio.ToString("F5");
                    case ExportColumns.Included: return inj.Include ? "1" : "0";
                    case ExportColumns.InjectionVolume: return inj.Volume.ToString("E2");
                    case ExportColumns.InjectionDelay: return inj.Delay.ToString();
                    case ExportColumns.CellConc: return inj.ActualCellConcentration.ToString("F8");
                    case ExportColumns.SyrConc: return inj.ActualTitrantConcentration.ToString("F8");
                    case ExportColumns.Peak: return settings.ExportOffsetCorrected ? inj.OffsetEnthalpy.ToString("F3") : inj.Enthalpy.ToString("F3");
                    case ExportColumns.PeakError: return inj.SD.ToString("F2");
                    case ExportColumns.Temperature: return inj.Temperature.ToString("F2");
                    case ExportColumns.IntegrationLength: return inj.IntegrationEndOffset.ToString("F1");
                    case ExportColumns.Fit:
                        if (data.Solution != null) return data.Model.EvaluateEnthalpy(i, !settings.ExportOffsetCorrected).ToString("F3");
                        else return BlankChar.ToString();
                    default: return BlankChar.ToString();
                }
            }
        }
    }
}
