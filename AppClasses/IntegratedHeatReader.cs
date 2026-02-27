using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AnalysisITC;
using Utilities;

namespace DataReaders
{
    /// <summary>
    /// Reader for integrated-heats exports.
    /// Expected columns (semicolon separated): DH;INJV;Xt;Mt;Xmt;NDH
    ///
    /// Notes:
    /// - No thermogram exists in this format; this reader populates Injections with PeakArea and InjectionMass.
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

        public static ExperimentData ReadFile(string filepath, bool concentrationsAreMilliMolar = true)
        {
            if (filepath == null) throw new ArgumentNullException(nameof(filepath));
            if (!File.Exists(filepath)) throw new FileNotFoundException("File not found", filepath);

            var ext = Path.GetExtension(filepath);
            if (ext.ToLower().Contains("aff")) separator = ';';

            var lines = File.ReadAllLines(filepath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (lines.Count < 2) throw new FormatException("File contains too few lines.");

            // Header
            var header = SplitLine(lines[0]);
            var col = BuildColumnIndex(header);

            // Parse rows
            var rows = new List<Row>();
            var cCell_M = 0.0;
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
                    Dh_kJ = dh,
                    InjV_uL = injv,
                    Xt = xt,
                    Mt = mt,
                    Xmt = xmt,
                    Ndh_kJ_per_mol = ndh
                });
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
            };

            // Unit scaling for Xt/Mt
            var concScale = concentrationsAreMilliMolar ? 1e-3 : 1.0;

            // Infer cell volume from Mt dilution: Mt_{i+1} = Mt_i * (1 - Vinj/Vcell)
            var vcell_L = InferCellVolumeLiters(rows, concScale);
            data.CellVolume = vcell_L;

            // Infer syringe concentration from injection moles / injection volume
            // Injection moles are DH/NDH (kJ)/(kJ/mol) = mol
            var csyr_M = InferSyringeConcentration(rows);
            data.SyringeConcentration = new(csyr_M);

            // Build injections
            var injs = new List<InjectionData>(rows.Count);

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];

                var vinj_L = r.InjV_uL * 1e-6; // uL -> L
                var heat_J = r.Dh_kJ * 1000.0; // kJ -> J

                // Injection moles: prefer DH/NDH if NDH present; else fall back to Csyr * Vinj.
                var injMol = 0.0;
                if (double.IsFinite(r.Ndh_kJ_per_mol) && Math.Abs(r.Ndh_kJ_per_mol) > 0)
                    injMol = r.Dh_kJ / r.Ndh_kJ_per_mol;
                else
                    injMol = csyr_M * vinj_L;

                // Update concentrations using mixing model
                // var frac = (vcell_L > 0) ? (vinj_L / vcell_L) : 0.0;
                // frac = Math.Clamp(frac, 0.0, 0.1); // protect against nonsense Vcell; 0.1 is conservative

                var mt_post = r.Mt / 1000;
                var xt_post = r.Xt / 1000;

                var ratio_post = (mt_post > 0) ? (xt_post / mt_post) : 0.0;

                // Create InjectionData using the CSV-ctor to ensure Experiment is set (other ctor leaves Experiment null)
                // Format: ID,Include,Time,Volume,Delay,Duration,Temperature,IntegrationStartDelay,IntegrationLength
                // Delay/Duration/Temp are dummy placeholders for "integrated-only" imports.
                var csv = string.Format(
                    Inv,
                    "{0},1,{1},{2},{3},{4},{5},0,0",
                    i,
                    (float)i,
                    vinj_L,
                    300.0f,
                    1.0f,
                    25.0
                );

                var inj = new InjectionData(data, csv)
                {
                    InjectionMass = injMol,
                    Ratio = ratio_post,
                    ActualCellConcentration = mt_post,
                    ActualTitrantConcentration = xt_post,
                };

                inj.SetPeakArea(new FloatWithError(heat_J, 0));

                injs.Add(inj);
            }

            data.Injections = injs;

            // We need to recalculate concentrations for precission 
            RawDataReader.ProcessInjections(data);

            return data;
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

                var vinj_L = r0.InjV_uL * 1e-6;
                var vcell = vinj_L / (1.0 - f);

                // Keep plausible ITC cell volumes (50 uL .. 5 mL)
                if (double.IsFinite(vcell) && vcell > 50e-6 && vcell < 5e-3)
                    vCandidates.Add(vcell);
            }

            if (vCandidates.Count == 0)
            {
                // Fallback: VP-ITC-ish 1.4 mL (safe default; user can edit later)
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
                var vinj_L = r.InjV_uL * 1e-6;
                if (vinj_L <= 0) continue;

                if (double.IsFinite(r.Ndh_kJ_per_mol) && Math.Abs(r.Ndh_kJ_per_mol) > 0)
                {
                    var injMol = r.Dh_kJ / r.Ndh_kJ_per_mol;
                    var c = injMol / vinj_L; // mol/L
                    if (double.IsFinite(c) && c > 0 && c < 50) cCandidates.Add(c);
                }
            }

            if (cCandidates.Count == 0)
            {
                // fallback: 1 mM
                return 1e-3;
            }

            cCandidates.Sort();
            return cCandidates[cCandidates.Count / 2];
        }

        private struct Row
        {
            public double Dh_kJ;
            public double InjV_uL;
            public double Xt;
            public double Mt;
            public double Xmt;
            public double Ndh_kJ_per_mol;
        }
    }
}