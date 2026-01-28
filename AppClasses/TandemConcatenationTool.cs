// AppClasses/AnalysisClasses/TandemConcatenation.cs
//
// Tandem stitching (syringe reload / continue on same cell content)
//
// Style goals:
// - Use the existing ExperimentData / InjectionData / DataPoint structures
// - Reuse the existing dilution math (RawDataReader.ProcessInjectionsMicroCal)
// - Avoid introducing generic frameworks / adapters
//
// Notes:
// - We create new InjectionData objects via the existing CSV constructor
//   InjectionData(ExperimentData experiment, string line) so we can set ID/time/include/etc.
//   (ID has a private setter.) :contentReference[oaicite:0]{index=0}
// - After concatenation we call RawDataReader.ProcessInjections(result) to recompute
//   InjectionMass, ActualCellConcentration, ActualTitrantConcentration, Ratio using the
//   same code path as normal MicroCal parsing. :contentReference[oaicite:1]{index=1}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DataReaders;
using Utilities;

namespace AnalysisITC
{
    public static class TandemConcatenation
    {
        public class BackMixingSettings
        {
            /// <summary>
            /// Volume above the active cell that can hold displaced liquid (L). Typical iTC200 fill “extra” is ~80 µL.
            /// Store in liters (1 µL = 1e-6 L), same unit system as ExperimentData.CellVolume and InjectionData.Volume.
            /// </summary>
            public double DeadVolume = 80e-6;

            /// <summary>
            /// Volume removed from the dead/overflow compartment between segments (L). Applied after each segment except the last.
            /// (E.g. automated “continue injections” can remove a fixed amount.)
            /// </summary>
            public double RemoveOverflowVolume = 0.0;

            /// <summary>
            /// Fraction (0..1) of the dead/overflow compartment that “exchanges” with the active cell between segments.
            /// 0 -> no back-mixing; 1 -> full dead compartment participates.
            /// </summary>
            public double MixingFraction = 0.0;

            public BackMixingSettings Copy()
            {
                return (BackMixingSettings)MemberwiseClone();
            }

            public static double uL(double value) => value * 1e-6;
        }

        struct SegmentInfo
        {
            public string Name;
            public int InjectionNumStart;
            public int InjectionCount;

            public SegmentInfo(string name, int start, int count)
            {
                Name = name;
                InjectionNumStart = start;
                InjectionCount = count;
            }
        }

        /// <summary>
        /// Standard MicroCal-Concat-like stitching (no back-mixing). Uses RawDataReader.ProcessInjections().
        /// </summary>
        public static ExperimentData ConcatTandem(List<ExperimentData> experiments, string fileName = null)
        {
            var (result, segments) = ConcatCore(experiments, fileName, modeTag: "Tandem concatenation (no back-mixing).");

            RawDataReader.ProcessInjections(result);
            RawDataReader.ProcessData(result);

            return result;
        }

        /// <summary>
        /// Tandem stitching with optional back-mixing between segments.
        /// This function does NOT call RawDataReader.ProcessInjections(result) because we compute the concentrations
        /// with a stateful (multi-compartment) model instead.
        /// </summary>
        public static ExperimentData ConcatTandemWithBackMixing(List<ExperimentData> experiments, BackMixingSettings settings, string fileName = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var tag = "Tandem concatenation (back-mixing enabled): " +
                      $"DeadVolume={settings.DeadVolume.ToString("G", CultureInfo.InvariantCulture)} L, " +
                      $"RemoveOverflow={settings.RemoveOverflowVolume.ToString("G", CultureInfo.InvariantCulture)} L, " +
                      $"MixFrac={settings.MixingFraction.ToString("G", CultureInfo.InvariantCulture)}";

            var (result, segments) = ConcatCore(experiments, fileName, modeTag: tag);

            // Compute injection concentrations/ratios via back-mixing model
            ProcessInjections_BackMixingOverflowModel(result, segments, settings);

            // Keep the usual post-read processing (baseline, peak direction, etc.)
            RawDataReader.ProcessData(result);

            return result;
        }

        static (ExperimentData result, List<SegmentInfo> segments) ConcatCore(List<ExperimentData> experiments, string fileName, string modeTag)
        {
            if (experiments == null) throw new ArgumentNullException(nameof(experiments));
            if (experiments.Count < 2) throw new ArgumentException("ConcatTandem requires at least two experiments.");

            var first = experiments[0];

            if (string.IsNullOrEmpty(fileName))
            {
                fileName = "Tandem_" + first.FileName;
            }

            var result = new ExperimentData(fileName)
            {
                Instrument = first.Instrument,
                DataSourceFormat = first.DataSourceFormat,

                SyringeConcentration = first.SyringeConcentration,
                CellConcentration = first.CellConcentration,
                CellVolume = first.CellVolume,

                StirringSpeed = first.StirringSpeed,
                FeedBackMode = first.FeedBackMode,
                TargetTemperature = first.TargetTemperature,
                InitialDelay = first.InitialDelay,
                TargetPowerDiff = first.TargetPowerDiff,

                IntegrationLengthMode = first.IntegrationLengthMode,
                IntegrationLengthFactor = first.IntegrationLengthFactor,

                Date = first.Date,
                Comments = BuildConcatComment(experiments, first.Comments, modeTag),
            };

            foreach (var opt in first.Attributes) result.Attributes.Add(opt);

            foreach (var exp in experiments)
            {
                if (exp.DataSourceFormat != first.DataSourceFormat)
                    AppEventHandler.PrintAndLog("ConcatTandem warning: DataSourceFormat mismatch: " + exp.FileName);

                if (Math.Abs(exp.CellVolume - first.CellVolume) > 1e-12)
                    AppEventHandler.PrintAndLog("ConcatTandem warning: CellVolume mismatch: " + exp.FileName);

                if (exp.DataPoints == null || exp.DataPoints.Count == 0)
                    AppEventHandler.PrintAndLog("ConcatTandem warning: No datapoints in: " + exp.FileName);

                if (exp.Injections == null || exp.Injections.Count == 0)
                    AppEventHandler.PrintAndLog("ConcatTandem warning: No injections in: " + exp.FileName);
            }

            var datapoints = new List<DataPoint>();
            var injections = new List<InjectionData>();
            var segments = new List<SegmentInfo>();

            float nextStartTime = 0f;
            int injId = 0;

            foreach (var exp in experiments)
            {
                if (exp.DataPoints == null || exp.DataPoints.Count == 0) continue;

                var segFirstTime = exp.DataPoints.First().Time;
                var segLastTime = exp.DataPoints.Last().Time;

                float shift = nextStartTime - segFirstTime;

                foreach (var dp in exp.DataPoints)
                {
                    datapoints.Add(new DataPoint(
                        time: dp.Time + shift,
                        power: dp.Power,
                        temp: dp.Temperature,
                        dt: dp.DT,
                        shieldt: dp.ShieldT,
                        atp: dp.ATP,
                        jfbi: dp.JFBI
                    ));
                }

                int segStart = injections.Count;

                if (exp.Injections != null)
                {
                    foreach (var inj in exp.Injections)
                    {
                        var newInj = CopyInjectionWithNewIdAndShift(result, inj, injId, shift);
                        injections.Add(newInj);
                        injId++;
                    }
                }

                segments.Add(new SegmentInfo(exp.FileName, segStart, injections.Count - segStart));

                nextStartTime = (segLastTime + shift) + 1f;
            }

            result.DataPoints = datapoints;
            result.Injections = injections;

            return (result, segments);
        }

        static void ProcessInjections_BackMixingOverflowModel(ExperimentData experiment, List<SegmentInfo> segments, BackMixingSettings settings)
        {
            if (experiment == null) throw new ArgumentNullException(nameof(experiment));
            if (segments == null) throw new ArgumentNullException(nameof(segments));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            if (experiment.Injections == null || experiment.Injections.Count == 0) return;
            if (experiment.CellVolume <= 0) throw new InvalidOperationException("CellVolume must be > 0.");

            // Clamp settings
            double Vdead = settings.DeadVolume;
            double Vremove = settings.RemoveOverflowVolume;
            double mixFrac = Math.Clamp(settings.MixingFraction, 0.0, 1.0);
            double V0 = experiment.CellVolume;
            double Cs = experiment.SyringeConcentration.Value;
            double M0 = experiment.CellConcentration.Value; // initial macromolecule concentration in active cell

            // State variables (amounts in mol)
            double nM_active = M0 * V0;
            double nL_active = 0.0;

            double nM_dead = M0 * Vdead;
            double nL_dead = 0.0;

            for (int s = 0; s < segments.Count; s++)
            {
                var seg = segments[s];

                // Segment injections
                for (int i = seg.InjectionNumStart; i < seg.InjectionNumStart + seg.InjectionCount; i++)
                {
                    var inj = experiment.Injections[i];
                    var v_inj = inj.Volume;

                    // Inject titrant (mol)
                    inj.InjectionMass = Cs * v_inj;

                    // Instant overflow recursion:
                    // 1) mix injected volume with active contents
                    // 2) displace v of the well-mixed (V0+v) back to keep active at V0
                    var Vtot = V0 + v_inj;
                    var nM_mix = nM_active;
                    var nL_mix = nL_active + (Cs * v_inj);

                    var fracRemain = V0 / Vtot;
                    var fracDisp = v_inj / Vtot;

                    // Update active
                    nM_active = nM_mix * fracRemain;
                    nL_active = nL_mix * fracRemain;

                    // Update dead (accumulates displaced liquid)
                    nM_dead += nM_mix * fracDisp;
                    nL_dead += nL_mix * fracDisp;
                    Vdead += v_inj;

                    // Derived concentrations for this injection
                    inj.ActualCellConcentration = nM_active / V0;
                    inj.ActualTitrantConcentration = nL_active / V0;
                    inj.Ratio = inj.ActualTitrantConcentration / inj.ActualCellConcentration;
                }

                // Between segments: removal + back-mixing (skip after last segment)
                if (s < segments.Count - 1)
                {
                    ApplyOverflowRemoval(ref Vdead, ref nM_dead, ref nL_dead, Vremove);

                    if (mixFrac > 0 && Vdead > 0)
                    {
                        ApplyBackMixing(
                            V0,
                            mixFrac,
                            ref nM_active, ref nL_active,
                            Vdead, ref nM_dead, ref nL_dead);
                    }
                }
            }
        }

        /// <summary>
        /// Deletes mass from dead volume according the amount of liquid removed after the experiment
        /// </summary>
        /// <param name="Vdead">Reference</param>
        /// <param name="nM_dead">Reference</param>
        /// <param name="nL_dead">Reference</param>
        /// <param name="removeV"></param>
        static void ApplyOverflowRemoval(ref double Vdead, ref double nM_dead, ref double nL_dead, double removeV)
        {
            if (Vdead <= 0) return;
            if (removeV <= 0) return;

            var v = Math.Min(removeV, Vdead);
            var frac = v / Vdead;

            // Assume dead/overflow compartment is well mixed; remove proportionate masses
            nM_dead *= (1.0 - frac);
            nL_dead *= (1.0 - frac);
            Vdead -= v;
        }

        static void ApplyBackMixing(double V0, double mixFrac, ref double nM_active, ref double nL_active, double Vdead, ref double nM_dead, ref double nL_dead)
        {
            var Vmix = mixFrac * Vdead;
            if (Vmix <= 0) return;

            // Current dead concentrations
            var CdeadM = nM_dead / Vdead;
            var CdeadL = nL_dead / Vdead;

            // Compute mixed concentration in combined volume (V0 + Vmix)
            var Vcomb = V0 + Vmix;

            var CnewM = (nM_active + CdeadM * Vmix) / Vcomb;
            var CnewL = (nL_active + CdeadL * Vmix) / Vcomb;

            // Set new active amounts (active volume stays V0)
            nM_active = CnewM * V0;
            nL_active = CnewL * V0;

            // Update dead amounts to conserve total mass:
            // dead_after = old concentration in unmixed volume + new mixed conc in the mixed fraction
            nM_dead = CdeadM * (Vdead - Vmix) + (CnewM * Vmix);
            nL_dead = CdeadL * (Vdead - Vmix) + (CnewL * Vmix);
        }

        static InjectionData CopyInjectionWithNewIdAndShift(ExperimentData target, InjectionData inj, int newId, float shift)
        {
            var inv = CultureInfo.InvariantCulture;

            var include = inj.Include ? "1" : "0";
            var time = (inj.Time + shift).ToString(inv);
            var vol = inj.Volume.ToString(inv);
            var delay = inj.Delay.ToString(inv);
            var dur = inj.Duration.ToString(inv);
            var temp = inj.Temperature.ToString(inv);
            var istart = inj.IntegrationStartDelay.ToString(inv);
            var ilen = inj.IntegrationLength.ToString(inv);

            var line = string.Join(",", new string[]
            {
                newId.ToString(inv),
                include,
                time,
                vol,
                delay,
                dur,
                temp,
                istart,
                ilen
            });

            return new InjectionData(target, line);
        }

        static string BuildConcatComment(List<ExperimentData> experiments, string originalComment, string modeTag)
        {
            var files = string.Join(" + ", experiments.Select(e => e.FileName));
            var c = modeTag + Environment.NewLine + "Source files: " + files;

            if (!string.IsNullOrWhiteSpace(originalComment))
                c += Environment.NewLine + originalComment;

            return c;
        }
    }
}
