using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AnalysisITC.Platform;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.DataReaders
{
    /// <summary>
    /// Reader for integrated-heats exports.
    /// Expected columns (semicolon separated): DH;INJV;Xt;Mt;Xmt;NDH
    ///
    /// Notes:
    /// - No thermogram exists in this format; this reader populates Injections with PeakArea and InjectionMass.
    /// - Also supports legacy .DH integrated-heat files with a fixed metadata header followed by volume/heat rows.
    /// - Delimited formats do not encode an unambiguous DH unit; the user is asked to select it.
    /// - Syringe concentration and cell volume are inferred from the Xt/Mt concentration trajectory,
    ///   independently of the optional NDH column.
    /// - Xt and Mt are assumed to be in mM by default (set concentrationsAreMilliMolar=false if you want raw as M).
    /// - Ratio is computed (preferred) from inferred concentrations/volume rather than trusting Xmt (which can be malformed).
    /// </summary>
    public static class IntegratedHeatReader
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const NumberStyles NumStyle = NumberStyles.Float | NumberStyles.AllowLeadingSign;
        private static char separator = ',';
        private static EnergyUnit? queuedEnergyUnit;
        private static bool cancelRemainingQueueItems;

        public static bool CancelRemainingQueueItems => cancelRemainingQueueItems;

        public static void BeginImportQueue()
        {
            queuedEnergyUnit = null;
            cancelRemainingQueueItems = false;
        }

        public static void EndImportQueue()
        {
            queuedEnergyUnit = null;
            cancelRemainingQueueItems = false;
        }

        public static ExperimentData ReadFile(string filepath, bool concentrationsAreMilliMolar = true)
        {
            return ReadFile(
                filepath,
                concentrationsAreMilliMolar,
                AppSettings.DilutionCalculationMethod,
                AppSettings.ReprocessIntegratedHeatDataOnLoad);
        }

        internal static ExperimentData ReadFile(
            string filepath,
            bool concentrationsAreMilliMolar,
            DilutionMethod dilutionMethod,
            bool reprocessIntegratedHeatData)
        {
            if (filepath == null) throw new ArgumentNullException(nameof(filepath));
            if (!File.Exists(filepath)) throw new FileNotFoundException("File not found", filepath);

            var lines = File.ReadAllLines(filepath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (lines.Count < 2) throw new FormatException("File contains too few lines.");

            var data = LooksLikeDhFile(filepath, lines)
                ? ReadDhFile(filepath, lines)
                : ReadDelimitedIntegratedHeats(
                    filepath,
                    lines,
                    concentrationsAreMilliMolar,
                    dilutionMethod,
                    reprocessIntegratedHeatData);

            ProcessExperiment(data);

            return data;
        }

        static void ProcessExperiment(ExperimentData data)
        {
            if (data == null) return;

            // Disable first injection
            data.Injections.First().Include = false;
        }

        private static ExperimentData ReadDelimitedIntegratedHeats(
            string filepath,
            List<string> lines,
            bool concentrationsAreMilliMolar,
            DilutionMethod dilutionMethod,
            bool reprocessIntegratedHeatData)
        {
            separator = ResolveSeparator(lines[0]);

            // Header
            var header = SplitLine(lines[0]);
            var col = BuildColumnIndex(header);
            if (!col.ContainsKey("DH") || !col.ContainsKey("INJV"))
                throw new FormatException("Delimited integrated-heats files must contain DH and INJV columns.");

            // Parse physical records first. Injection rows carry DH/INJV; an optional
            // final state-only record carries the concentration state after the last injection.
            var records = new List<DelimitedRecord>();
            var encounteredTerminalState = false;
            AppEventHandler.PrintAndLog("Reading Injections...", 0);
            for (int i = 1; i < lines.Count; i++)
            {
                var parts = SplitLine(lines[i]);
                if (parts.Length == 0) continue;

                var dhPresent = HasValueToken(parts, col, "DH");
                var injvPresent = HasValueToken(parts, col, "INJV");
                var dhValid = TryGet(parts, col, "DH", out var dh) && FWEMath.IsFinite(dh);
                var injvValid = TryGet(parts, col, "INJV", out var injv) && FWEMath.IsFinite(injv);

                if (dhPresent && !dhValid)
                    throw new FormatException($"Invalid DH value on line {i + 1}: \"{lines[i]}\"");
                if (injvPresent && !injvValid)
                    throw new FormatException($"Invalid INJV value on line {i + 1}: \"{lines[i]}\"");
                if (dhValid != injvValid)
                    throw new FormatException($"Line {i + 1} must provide both DH and INJV: \"{lines[i]}\"");
                if (injvValid && injv <= 0)
                    throw new FormatException($"INJV must be positive on line {i + 1}: \"{lines[i]}\"");

                TryGet(parts, col, "Xt", out var xt);
                TryGet(parts, col, "Mt", out var mt);
                TryGet(parts, col, "Xmt", out var xmt);
                var isInjection = dhValid && injvValid;
                var hasState = FWEMath.IsFinite(xt) || FWEMath.IsFinite(mt);
                if (!isInjection && !hasState)
                {
                    AppEventHandler.PrintAndLog($"Ignoring empty integrated-heats row {i + 1}.", 1);
                    continue;
                }

                if (encounteredTerminalState && isInjection)
                    throw new FormatException($"Injection row {i + 1} occurs after a terminal concentration-state row.");
                if (!isInjection) encounteredTerminalState = true;

                records.Add(new DelimitedRecord
                {
                    IsInjection = isInjection,
                    DH = dh,
                    InjV_uL = injv,
                    Xt = xt,
                    Mt = mt,
                    Xmt = xmt,
                });

                if (isInjection)
                    AppEventHandler.PrintAndLog($"{injv}\t{xt}\t{mt}\t{dh}");
            }

            var rows = new List<Row>();
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (!record.IsInjection) continue;

                var next = i + 1 < records.Count ? records[i + 1] : default;
                rows.Add(new Row
                {
                    DH = record.DH,
                    InjV_uL = record.InjV_uL,
                    PreXt = record.Xt,
                    PreMt = record.Mt,
                    PostXt = i + 1 < records.Count ? next.Xt : double.NaN,
                    PostMt = i + 1 < records.Count ? next.Mt : double.NaN,
                    Xmt = record.Xmt,
                });
            }

            if (rows.Count == 0) throw new FormatException("No injection rows found in file.");

            var concScale = concentrationsAreMilliMolar ? 1e-3 : 1.0;
            var initialCellConcentration = rows.Count > 0 && FWEMath.IsFinite(rows[0].PreMt)
                ? rows[0].PreMt * concScale
                : double.NaN;

            var data = new ExperimentData(Path.GetFileName(filepath))
            {
                DataPoints = new List<DataPoint>(),                  // no thermogram
                BaseLineCorrectedDataPoints = new List<DataPoint>(), // avoid null refs
                Date = File.GetCreationTime(filepath),
                DateSource = ExperimentDateSource.FileSystem,
                Instrument = ITCInstrument.Unknown,
                DataSourceFormat = ITCDataFormat.IntegratedHeats,
                CellConcentration = new(initialCellConcentration),
                SyringeConcentration = new(double.NaN),
                CellVolume = double.NaN,
                TargetTemperature = AppSettings.ReferenceTemperature,
            };

            // Unit scale for heat
            var maxv = rows.Max(r => Math.Abs(r.DH));

            var unit = ResolveEnergyUnit(filepath, maxv.ToString(Inv));
            if (unit == null) return null;

            // Infer cell volume and syringe concentration from the concentration
            // trajectory. This is independent of the DH/NDH relative unit scale.
            AppEventHandler.PrintAndLog("Inferring Cell Volume...");
            var vcell_L = InferCellVolumeLiters(rows, dilutionMethod);
            data.CellVolume = vcell_L;
            AppEventHandler.PrintAndLog(FWEMath.IsFinite(vcell_L)
                ? $"Volume = {vcell_L * 1000000} ul"
                : "Cell volume could not be inferred from Mt.", 1);

            AppEventHandler.PrintAndLog("Inferring Syringe Concentration...");
            var csyr_M = InferSyringeConcentration(rows, vcell_L, concScale, dilutionMethod);
            data.SyringeConcentration = new(csyr_M);
            AppEventHandler.PrintAndLog(FWEMath.IsFinite(csyr_M)
                ? $"Syringe Concentration = {1000000 * csyr_M} uM"
                : "Syringe concentration could not be inferred from Xt.", 1);

            // Build injections
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                var vinj_L = r.InjV_L;
                var heat_J = Energy.ConvertToJoule(r.DH, unit.Value);
                var inj = new InjectionData(data, vinj_L);

                inj.SetPeakArea(new FloatWithError(heat_J, 0));
                inj.Ratio = r.Xmt;
                inj.ActualCellConcentration = FWEMath.IsFinite(r.PostMt) ? r.PostMt * concScale : double.NaN;
                inj.ActualTitrantConcentration = FWEMath.IsFinite(r.PostXt) ? r.PostXt * concScale : double.NaN;

                data.Injections.Add(inj);
            }

            if (reprocessIntegratedHeatData && HasResolvedConcentrationMetadata(data))
                RawDataReader.ProcessInjections(data);

            // Try to get the instrument based on cell volume
            ITCInstrumentAttribute.ResolveInstrument(data);

            return data;
        }

        private static ExperimentData ReadDhFile(string filepath, List<string> lines)
        {
            if (lines.Count < 6) throw new FormatException("DH file contains too few lines.");

            var metadata = ParseDhMetadata(lines);
            var rows = new List<DhRow>();

            AppEventHandler.PrintAndLog("Reading DH injections...", 0);
            for (int i = 5; i < lines.Count; i++)
            {
                if (!TryParseDhNumbers(lines[i], out var values, minimumCount: 2))
                    throw new FormatException($"Could not parse DH injection row {i + 1}: \"{lines[i]}\"");

                rows.Add(new DhRow
                {
                    Volume_uL = values[0],
                    Heat = values[1]
                });

                AppEventHandler.PrintAndLog($"{values[0]}\t{values[1]}");
            }

            if (rows.Count == 0) throw new FormatException("No injection rows found in DH file.");
            if (metadata.InjectionCount > 0 && metadata.InjectionCount != rows.Count)
            {
                AppEventHandler.PrintAndLog(
                    $"DH file declared {metadata.InjectionCount} injections but {rows.Count} rows were read.",
                    1);
            }

            var maxv = rows.Max(r => Math.Abs(r.Heat));
            var unit = ResolveEnergyUnit(filepath, maxv.ToString(Inv));
            if (unit == null) return null;

            var data = new ExperimentData(Path.GetFileName(filepath))
            {
                DataPoints = new List<DataPoint>(),
                BaseLineCorrectedDataPoints = new List<DataPoint>(),
                Date = File.GetCreationTime(filepath),
                DateSource = ExperimentDateSource.FileSystem,
                Instrument = ITCInstrument.Unknown,
                DataSourceFormat = ITCDataFormat.IntegratedHeats,
                CellConcentration = new(metadata.CellConcentration_M),
                SyringeConcentration = new(metadata.SyringeConcentration_M),
                CellVolume = metadata.CellVolume_L,
                MeasuredTemperature = metadata.Temperature_C,
                TargetTemperature = Math.Round(4 * metadata.Temperature_C) / 4, //
            };

            foreach (var row in rows)
            {
                var inj = new InjectionData(data, row.Volume_L);
                inj.SetPeakArea(new FloatWithError(Energy.ConvertToJoule(row.Heat, unit.Value), 0));
                data.Injections.Add(inj);
            }

            RawDataReader.ProcessInjections(data);
            ITCInstrumentAttribute.ResolveInstrument(data);

            return data;
        }

        private static EnergyUnit? ResolveEnergyUnit(string filepath, string encounteredValue)
        {
            if (cancelRemainingQueueItems)
            {
                return null;
            }

            if (queuedEnergyUnit.HasValue)
            {
                AppEventHandler.PrintAndLog($"Energy Unit Reused From Queue: {queuedEnergyUnit}");
                return queuedEnergyUnit;
            }

            var result = PlatformServices.ImportPromptService.AskForEnergyUnit(filepath, encounteredValue, allowQueueReuse: true);
            AppEventHandler.PrintAndLog($"Energy Unit Selected: {result.Unit}");

            if (result.IsCancelled)
            {
                cancelRemainingQueueItems = true;
                AppEventHandler.PrintAndLog("Integrated heats import canceled. Remaining queued files will be skipped.");
                return null;
            }

            if (result.UseForRemainingFilesInQueue && result.Unit.HasValue)
            {
                queuedEnergyUnit = result.Unit.Value;
            }

            return result.Unit;
        }

        private static char ResolveSeparator(string line)
        {
            var separators = new[] { '\t', ';', ',' };

            foreach (var sep in separators) if (line.Contains(sep)) return sep;

            return ',';
        }

        private static string[] SplitLine(string line)
        {
            // Affinimeter uses ';' separators; header may contain trailing ";;;".
            // Keep empty entries so indexing stays consistent.
            return (line ?? string.Empty)
                .Trim()
                .Split(new[] { separator }, StringSplitOptions.None)
                .Select(s => s.Trim())
                .ToArray();
        }

        private static Dictionary<string, int> BuildColumnIndex(string[] header)
        {
            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < header.Length; i++)
            {
                var h = header[i];
                if (string.IsNullOrWhiteSpace(h)) continue;

                // Normalize common variants
                var key = h.Trim();
                if (!dict.ContainsKey(key)) dict.Add(key, i);
            }

            return dict;
        }

        private static bool TryGet(string[] parts, Dictionary<string, int> col, string name, out double value)
        {
            value = double.NaN;

            if (!col.TryGetValue(name, out var idx)) return false;
            if (idx < 0 || idx >= parts.Length) return false;

            var s = parts[idx];
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (s == "--") return false;

            // Handle both "." and "," decimals defensively
            s = s.Replace(',', '.');

            return double.TryParse(s, NumStyle, Inv, out value);
        }

        private static bool HasValueToken(string[] parts, Dictionary<string, int> col, string name)
        {
            if (!col.TryGetValue(name, out var idx) || idx < 0 || idx >= parts.Length) return false;
            var value = parts[idx];
            return !string.IsNullOrWhiteSpace(value) && value != "--";
        }

        private static bool LooksLikeDhFile(string filepath, List<string> lines)
        {
            if (string.Equals(Path.GetExtension(filepath), ".dh", StringComparison.OrdinalIgnoreCase))
                return true;

            if (lines.Count < 6) return false;
            if (!int.TryParse(lines[0].Trim(), NumberStyles.Integer, Inv, out _)) return false;
            if (!TryParseDhNumbers(lines[1], out var secondLine, minimumCount: 2)) return false;
            if (!TryParseDhNumbers(lines[2], out var thirdLine, minimumCount: 4)) return false;
            if (!TryParseDhNumbers(lines[5], out var firstInjection, minimumCount: 2)) return false;

            return secondLine.Length >= 2 && thirdLine.Length >= 4 && firstInjection.Length >= 2;
        }

        private static DhMetadata ParseDhMetadata(List<string> lines)
        {
            if (!TryParseDhNumbers(lines[1], out var line2, minimumCount: 2))
                throw new FormatException("Could not parse DH header line 2.");
            if (!TryParseDhNumbers(lines[2], out var line3, minimumCount: 4))
                throw new FormatException("Could not parse DH header line 3.");

            return new DhMetadata
            {
                InjectionCount = (int)Math.Round(line2[1]),
                Temperature_C = line3[0],
                CellConcentration_M = line3[1] * 1e-3,
                SyringeConcentration_M = line3[2] * 1e-3,
                CellVolume_L = line3[3] * 1e-3,
            };
        }

        private static bool TryParseDhNumbers(string line, out double[] values, int minimumCount = 0)
        {
            values = Array.Empty<double>();

            if (string.IsNullOrWhiteSpace(line)) return false;

            var parts = line.Split(',')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToArray();

            if (parts.Length < minimumCount) return false;

            var parsed = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i], NumStyle, Inv, out parsed[i]))
                    return false;
            }

            values = parsed;
            return true;
        }

        private static double InferCellVolumeLiters(List<Row> rows, DilutionMethod dilutionMethod)
        {
            var vCandidates = new List<double>();
            if (rows.Count == 0 || !FWEMath.IsFinite(rows[0].PreMt) || rows[0].PreMt <= 0)
                return double.NaN;

            var initialMt = rows[0].PreMt;
            var cumulativeVolume = 0.0;
            foreach (var row in rows)
            {
                cumulativeVolume += row.InjV_L;
                if (!FWEMath.IsFinite(row.PostMt) || row.PostMt <= 0) continue;

                var f = row.PostMt / initialMt;
                if (f <= 0 || f >= 1) continue;

                double vcell;
                switch (dilutionMethod)
                {
                    case DilutionMethod.Exponential:
                        vcell = -cumulativeVolume / Math.Log(f);
                        break;
                    case DilutionMethod.MicroCal:
                    default:
                        var a = (1.0 - f) / (1.0 + f);
                        vcell = cumulativeVolume / (2.0 * a);
                        break;
                }

                // Keep plausible ITC cell volumes (50 uL .. 10 mL)
                if (FWEMath.IsFinite(vcell) && vcell > 50e-6 && vcell < 10e-3)
                    vCandidates.Add(vcell);
            }

            return ConsistentMedianOrNaN(vCandidates, "cell-volume");
        }

        private static double InferSyringeConcentration(
            List<Row> rows,
            double cellVolume_L,
            double concScale,
            DilutionMethod dilutionMethod)
        {
            var cCandidates = new List<double>();
            if (rows.Count == 0 || !FWEMath.IsFinite(cellVolume_L) || cellVolume_L <= 0)
                return double.NaN;
            if (!FWEMath.IsFinite(rows[0].PreXt)) return double.NaN;

            var initialXt = rows[0].PreXt * concScale;
            var cumulativeVolume = 0.0;

            foreach (var r in rows)
            {
                cumulativeVolume += r.InjV_L;
                if (!FWEMath.IsFinite(r.PostXt)) continue;

                double remainingFraction;
                double injectedFraction;
                switch (dilutionMethod)
                {
                    case DilutionMethod.Exponential:
                        remainingFraction = Math.Exp(-cumulativeVolume / cellVolume_L);
                        injectedFraction = 1.0 - remainingFraction;
                        break;
                    case DilutionMethod.MicroCal:
                    default:
                        var a = cumulativeVolume / (2.0 * cellVolume_L);
                        if (a <= 0 || a >= 1) continue;
                        remainingFraction = (1.0 - a) / (1.0 + a);
                        injectedFraction = (cumulativeVolume / cellVolume_L) * (1.0 - a);
                        break;
                }

                if (!FWEMath.IsFinite(injectedFraction) || injectedFraction <= 0) continue;
                var postXt = r.PostXt * concScale;
                var c = (postXt - initialXt * remainingFraction) / injectedFraction;
                if (FWEMath.IsFinite(c) && c >= 0 && c < 50) cCandidates.Add(c);
            }

            return ConsistentMedianOrNaN(cCandidates, "syringe-concentration");
        }

        private static double ConsistentMedianOrNaN(List<double> candidates, string quantity)
        {
            if (candidates == null || candidates.Count == 0) return double.NaN;

            candidates.Sort();
            var median = Median(candidates);
            var deviations = candidates.Select(value => Math.Abs(value - median)).OrderBy(value => value).ToList();
            var mad = Median(deviations);
            var consistent = Math.Abs(median) <= 1e-15
                ? candidates.All(value => Math.Abs(value) <= 1e-12)
                : mad / Math.Abs(median) <= 0.10;

            if (!consistent)
            {
                AppEventHandler.PrintAndLog($"The {quantity} trajectory is inconsistent (relative MAD > 10%).", 1);
                return double.NaN;
            }

            return median;
        }

        private static double Median(IReadOnlyList<double> sorted)
        {
            if (sorted == null || sorted.Count == 0) return double.NaN;
            var middle = sorted.Count / 2;
            return sorted.Count % 2 == 0
                ? 0.5 * (sorted[middle - 1] + sorted[middle])
                : sorted[middle];
        }

        internal static bool HasResolvedConcentrationMetadata(ExperimentData data)
        {
            return data != null
                && FWEMath.IsFinite(data.CellVolume) && data.CellVolume > 0
                && FWEMath.IsFinite(data.CellConcentration.Value) && data.CellConcentration.Value >= 0
                && FWEMath.IsFinite(data.SyringeConcentration.Value) && data.SyringeConcentration.Value >= 0;
        }

        private struct Row
        {
            public double DH;
            public double InjV_uL;
            public double PreXt;
            public double PreMt;
            public double PostXt;
            public double PostMt;
            public double Xmt;

            public readonly double InjV_L => InjV_uL * 1E-6;
        }

        private struct DelimitedRecord
        {
            public bool IsInjection;
            public double DH;
            public double InjV_uL;
            public double Xt;
            public double Mt;
            public double Xmt;
        }

        private struct DhMetadata
        {
            public int InjectionCount;
            public double Temperature_C;
            public double CellConcentration_M;
            public double SyringeConcentration_M;
            public double CellVolume_L;
        }

        private struct DhRow
        {
            public double Volume_uL;
            public double Heat;

            public readonly double Volume_L => Volume_uL * 1e-6;
        }
    }
}
