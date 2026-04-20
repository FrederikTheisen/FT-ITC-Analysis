using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AnalysisITC;
using AnalysisITC.GUI.MacOS;

namespace DataReaders
{
    /// <summary>
    /// Reader for integrated-heats exports.
    /// Expected columns (semicolon separated): DH;INJV;Xt;Mt;Xmt;NDH
    ///
    /// Notes:
    /// - No thermogram exists in this format; this reader populates Injections with PeakArea and InjectionMass.
    /// - Also supports legacy .DH integrated-heat files with a fixed metadata header followed by volume/heat rows.
    /// - DH is assumed to be in kJ.
    /// - NDH is assumed to be in kJ/mol -> InjectionMass (mol) is DH/NDH.
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
            if (filepath == null) throw new ArgumentNullException(nameof(filepath));
            if (!File.Exists(filepath)) throw new FileNotFoundException("File not found", filepath);

            var lines = File.ReadAllLines(filepath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (lines.Count < 2) throw new FormatException("File contains too few lines.");

            var data = LooksLikeDhFile(filepath, lines) ? ReadDhFile(filepath, lines) : ReadDelimitedIntegratedHeats(filepath, lines, concentrationsAreMilliMolar);

            ProcessExperiment(data);

            return data;
        }

        static void ProcessExperiment(ExperimentData data)
        {
            if (data == null) return;

            // Disable first injection
            data.Injections.First().Include = false;
        }

        private static ExperimentData ReadDelimitedIntegratedHeats(string filepath, List<string> lines, bool concentrationsAreMilliMolar)
        {
            var ext = Path.GetExtension(filepath);
            separator = ResolveSeparator(lines[0]);

            // Header
            var header = SplitLine(lines[0]);
            var col = BuildColumnIndex(header);

            // Parse rows
            var rows = new List<Row>();
            var cCell_M = 0.0;
            AppEventHandler.PrintAndLog("Reading Injections...", 0);
            for (int i = 1; i < lines.Count - 1; i++)
            {
                var parts = SplitLine(lines[i]);
                var nextparts = SplitLine(lines[i + 1]);
                if (parts.Length == 0) continue;

                // Require at least DH and INJV to even consider as injection row
                TryGet(parts, col, "DH", out var dh);
                TryGet(parts, col, "INJV", out var injv);
                if (i == 1)
                {
                    TryGet(parts, col, "Mt", out cCell_M);
                    cCell_M /= 1000;
                }

                // NDH can be missing in some weird tail rows; we still keep row and back-fill later
                TryGet(parts, col, "NDH", out var ndh);
                TryGet(nextparts, col, "Xt", out var xt);
                TryGet(nextparts, col, "Mt", out var mt);
                TryGet(parts, col, "Xmt", out var xmt);

                rows.Add(new Row
                {
                    DH = dh,
                    InjV_uL = injv,
                    Xt = xt,
                    Mt = mt,
                    Xmt = xmt,
                    NDH = ndh
                });

                AppEventHandler.PrintAndLog($"{injv}\t{xt}\t{mt}\t{dh}");
            }

            if (rows.Count == 0) throw new FormatException("No injection rows found in file.");

            var data = new ExperimentData(Path.GetFileName(filepath))
            {
                DataPoints = new List<DataPoint>(),                  // no thermogram
                BaseLineCorrectedDataPoints = new List<DataPoint>(), // avoid null refs
                Date = File.GetCreationTime(filepath),
                Instrument = ITCInstrument.Unknown,
                DataSourceFormat = ITCDataFormat.IntegratedHeats,
                CellConcentration = new(cCell_M),
                TargetTemperature = AppSettings.ReferenceTemperature,
            };

            // Unit scaling for Xt/Mt
            var concScale = concentrationsAreMilliMolar ? 1e-3 : 1.0;

            // Unit scale for heat
            var maxv = rows.Max(r => Math.Abs(r.DH));

            var unit = ResolveEnergyUnit(filepath, maxv.ToString(Inv));
            if (unit == null) return null;

            // Infer cell volume from Mt dilution: Mt_{i+1} = Mt_i * (1 - Vinj/Vcell)
            AppEventHandler.PrintAndLog("Inferring Cell Volume...");
            var vcell_L = InferCellVolumeLiters(rows, concScale);
            data.CellVolume = vcell_L;
            AppEventHandler.PrintAndLog($"Volume = {vcell_L * 1000000} ul", 1);

            // Infer syringe concentration from injection moles / injection volume
            // Injection moles are DH/NDH = mol
            AppEventHandler.PrintAndLog("Inferring Syringe Concentration...");
            var csyr_M = InferSyringeConcentration(rows);
            data.SyringeConcentration = new(csyr_M);
            AppEventHandler.PrintAndLog($"Syringe Concentration = {1000000 * csyr_M} uM", 1);

            // Build injections
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                var vinj_L = r.InjV_L;
                var heat_J = Energy.ConvertToJoule(r.DH, unit.Value);
                var inj = new InjectionData(data, vinj_L);

                inj.SetPeakArea(new FloatWithError(heat_J, 0));
                inj.Ratio = r.Xmt;
                inj.ActualCellConcentration = r.Mt / 1000;
                inj.ActualTitrantConcentration = r.Xt / 1000;

                data.Injections.Add(inj);
            }

            if (AppSettings.ReprocessIntegratedHeatDataOnLoad) RawDataReader.ProcessInjections(data);

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
                Instrument = ITCInstrument.Unknown,
                DataSourceFormat = ITCDataFormat.IntegratedHeats,
                CellConcentration = new(metadata.CellConcentration_M),
                SyringeConcentration = new(metadata.SyringeConcentration_M),
                CellVolume = metadata.CellVolume_L,
                TargetTemperature = metadata.Temperature_C,
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

            var result = EnergyUnitPrompt.AskForEnergyUnit(null, filepath, encounteredValue, allowQueueReuse: true);
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

        private static double InferCellVolumeLiters(List<Row> rows, double concScale)
        {
            // Use Mt dilution across steps, in raw units (scale cancels in ratio anyway)
            var vCandidates = new List<double>();

            for (int i = 0; i < rows.Count - 1; i++)
            {
                var r0 = rows[i];
                var r1 = rows[i + 1];

                if (!double.IsFinite(r0.Mt) || !double.IsFinite(r1.Mt)) continue;
                if (r0.Mt <= 0 || r1.Mt <= 0) continue;

                // Pre->pre relationship: Mt_{i+1} is post-injection i (and pre for i+1)
                var f = r1.Mt / r0.Mt;
                if (f <= 0 || f >= 1) continue;

                var vinj_L = r0.InjV_L;
                var vcell = vinj_L / (1.0 - f);

                // Keep plausible ITC cell volumes (50 uL .. 10 mL)
                if (double.IsFinite(vcell) && vcell > 50e-6 && vcell < 10e-3)
                    vCandidates.Add(vcell);
            }

            if (vCandidates.Count == 0)
            {
                // Fallback: VP-ITC-ish 1.4 mL (safe default; user can edit later)
                AppEventHandler.PrintAndLog("Could not determine cell volume", 1);
                return 1.4e-3;
            }

            vCandidates.Sort();
            return vCandidates[vCandidates.Count / 2];
        }

        private static double InferSyringeConcentration(List<Row> rows)
        {
            var cCandidates = new List<double>();

            foreach (var r in rows)
            {
                var vinj_L = r.InjV_uL;
                if (vinj_L <= 0) continue;

                if (double.IsFinite(r.NDH) && Math.Abs(r.NDH) > 0)
                {
                    var injMol = r.DH / r.NDH;
                    var c = injMol / vinj_L; // mol/L
                    if (double.IsFinite(c) && c > 0 && c < 50) cCandidates.Add(c);
                }
            }

            if (cCandidates.Count == 0)
            {
                // fallback: 1 mM
                AppEventHandler.PrintAndLog("Could not determine syringe concentration", 1);
                return 1e-3;
            }

            cCandidates.Sort();
            return cCandidates[cCandidates.Count / 2];
        }

        private struct Row
        {
            public double DH;
            public double InjV_uL;
            public double Xt;
            public double Mt;
            public double Xmt;
            public double NDH;

            public readonly double InjV_L => InjV_uL * 1E-6;
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
