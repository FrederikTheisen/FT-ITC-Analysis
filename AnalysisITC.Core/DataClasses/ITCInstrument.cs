using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Data
{
    public class ITCInstrumentAttribute : Attribute
    {
        const double UnvailableSyringeVolume = 3;

        public string Name { get; set; }
        public string Description { get; set; }
        public string InstrumentCode { get; set; }
        public double StandardCellVolume { get; private set; }
        public double StandardSyringeVolume { get; private set; }
        public double DeadVolume { get; private set; }

        public ITCInstrumentAttribute(string name, string description, string extension, double cellv, double syrv, double deadv = 0.0)
        {
            Name = name;
            Description = description;
            InstrumentCode = extension;
            StandardCellVolume = cellv / 1000000;
            StandardSyringeVolume = (syrv) / 1000000;

            if (deadv < 1)
            {
                deadv = 0.4 * StandardCellVolume;
            }

            DeadVolume = (double)deadv / 1000000;
        }

        public static List<ITCInstrument> GetITCInstruments()
        {
            return new List<ITCInstrument>
            {
                ITCInstrument.MicroCalITC200,
                ITCInstrument.MalvernITC200,
                ITCInstrument.MicroCalVPITC,
                ITCInstrument.TAInstrumentsNanoITCLowVolume,
                ITCInstrument.TAInstrumentsNanoITCStandard,
                ITCInstrument.TAInstrumentsAffinityITCLowVolume,
                ITCInstrument.TAInstrumentsAffinityITCStandard,
            };
        }

        public static ITCInstrument TryResolveMicroCalInstrument(string line)
        {
            if (string.IsNullOrEmpty(line)) return ITCInstrument.Unknown;

            foreach (var instrument in GetITCInstruments().Where(instrument => (instrument & ITCInstrument.MicroCal) != 0))
            {
                if (line.Contains(instrument.GetProperties().InstrumentCode)) return instrument;
            }

            return ITCInstrument.Unknown;
        }

        /// <summary>
        /// Identifies a TA instrument from explicit native metadata, then effective cell volume in litres.
        /// </summary>
        public static ITCInstrument ResolveTAInstrument(double cellVolume, string instrumentSettings = null, string isAffinity = null)
        {
            var lowVolume = cellVolume <= 0.2e-3;
            foreach (var line in (instrumentSettings ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var match = Regex.Match(line, @"^\s*Instrument\s+Type\s*:\s*(.*?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success) continue;
                var type = match.Groups[1].Value;
                if (Regex.IsMatch(type, @"^Nano\s*ITC$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return lowVolume ? ITCInstrument.TAInstrumentsNanoITCLowVolume : ITCInstrument.TAInstrumentsNanoITCStandard;
                if (Regex.IsMatch(type, @"^Affinity(?:\s+ITC)?(?:\s*\([^\r\n)]*\))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    return lowVolume ? ITCInstrument.TAInstrumentsAffinityITCLowVolume : ITCInstrument.TAInstrumentsAffinityITCStandard;
            }

            if (bool.TryParse(isAffinity, out var affinity) && affinity)
                return lowVolume ? ITCInstrument.TAInstrumentsAffinityITCLowVolume : ITCInstrument.TAInstrumentsAffinityITCStandard;

            var instrument = ClosestInstrument(cellVolume, GetITCInstruments().Where(IsTAInstrument));
            return instrument == ITCInstrument.Unknown ? GenericTAInstrument(cellVolume) : instrument;
        }

        static ITCInstrument GenericTAInstrument(double cellVolume)
            => cellVolume > 0.2e-3 ? ITCInstrument.TAInstrumentsITCStandard : ITCInstrument.TAInstrumentsITCLowVolume;

        public static bool IsTAInstrument(ITCInstrument instrument) => instrument is
            ITCInstrument.TAInstrumentsITCStandard or ITCInstrument.TAInstrumentsITCLowVolume or
            ITCInstrument.TAInstrumentsNanoITCStandard or ITCInstrument.TAInstrumentsNanoITCLowVolume or
            ITCInstrument.TAInstrumentsAffinityITCStandard or ITCInstrument.TAInstrumentsAffinityITCLowVolume;

        static ITCInstrument ClosestInstrument(double cellVolume, IEnumerable<ITCInstrument> instruments)
        {
            if (double.IsNaN(cellVolume) || double.IsInfinity(cellVolume) || cellVolume <= 0) return ITCInstrument.Unknown;
            var candidates = instruments.Select(instrument =>
            {
                var referenceVolume = instrument.GetProperties().StandardCellVolume;
                return (Instrument: instrument, Distance: Math.Abs(cellVolume - referenceVolume), Volume: referenceVolume);
            }).Where(candidate => candidate.Distance < candidate.Volume * 0.3)
                .OrderBy(candidate => candidate.Distance).ToList();
            if (candidates.Count == 0) return ITCInstrument.Unknown;

            // Treat mathematically equal distances alike despite floating-point subtraction rounding.
            if (candidates.Count > 1 && IsTAInstrument(candidates[0].Instrument) && IsTAInstrument(candidates[1].Instrument)
                && Math.Abs(candidates[0].Distance - candidates[1].Distance) <= cellVolume * 1e-12)
                return GenericTAInstrument(cellVolume);

            return candidates[0].Instrument;
        }

        public static void ResolveInstrument(ExperimentData data)
        {
            if (data.Instrument != ITCInstrument.Unknown) return;
            if (data.CellVolume <= 0) return;

            AppEventHandler.PrintAndLog("Resolving Instrument...");

            data.Instrument = data.DataSourceFormat is ITCDataFormat.NanoITC or ITCDataFormat.TAITC
                ? ResolveTAInstrument(data.CellVolume)
                : ClosestInstrument(data.CellVolume, GetITCInstruments());

            AppEventHandler.PrintAndLog($"{data.Instrument}", 1);
        }

        //public static string SourceInstrumentTitle(ExperimentData data)
        //{
        //    return data.Instrument switch
        //    {
        //        ITCInstrument.TAInstrumentsITCStandard or
        //        ITCInstrument.TAInstrumentsITCLowVolume or
        //        ITCInstrument.MalvernITC200 or
        //        ITCInstrument.MicroCalITC200 => data.Instrument.GetProperties().Name,
        //        _ => data.DataSourceFormat switch
        //        {
        //            ITCDataFormat.IntegratedHeats => "Integrated Heat File",
        //            _ => "Unknown Source Type",
        //        },
        //    };
        //}
    }

    [Flags]
    public enum ITCInstrument
    {
        [ITCInstrument("Unknown", "", "",
            cellv: 200,
            syrv: 40,
            deadv: 0)]
        Unknown = 0,

        [ITCInstrument("MicroCal ITC200", "", "ITC200_",
            cellv: 204,
            syrv: 39.5,
            deadv: 80)]
        MicroCalITC200 = 1,

        [ITCInstrument("MicroCal PEAQ-ITC", "", "MICROCALITC_MAL",
            cellv: 207.1,
            syrv: 39.0,
            deadv: 80)]
        MalvernITC200 = 2,

        [ITCInstrument("MicroCal VP-ITC", "", "VPITC",
            cellv: 1479.1,
            syrv: 310,
            deadv: 400)]
        MicroCalVPITC = 4,

        [ITCInstrument("TA Instruments ITC Standard", "", "TAITC",
            cellv: 1000.0,
            syrv: 250,
            deadv: 200)]
        TAInstrumentsITCStandard = 8,

        [ITCInstrument("TA Instruments ITC Low Vol", "", "TAITC",
            cellv: 170.0,
            syrv: 250,
            deadv: 60)]
        TAInstrumentsITCLowVolume = 16,

        // Effective-volume defaults: TA NanoAnalyze guide, p. 15. Dead volumes retain
        // the existing TA size-class assumptions rather than instrument calibrations.
        [ITCInstrument("TA Instruments Nano ITC (Standard Volume)", "", "TAITC",
            cellv: 950, syrv: 250, deadv: 200)]
        TAInstrumentsNanoITCStandard = 32,

        [ITCInstrument("TA Instruments Nano ITC (Low Volume)", "", "TAITC",
            cellv: 170, syrv: 50, deadv: 60)]
        TAInstrumentsNanoITCLowVolume = 64,

        [ITCInstrument("TA Instruments Affinity ITC (Standard Volume)", "", "TAITC",
            cellv: 965, syrv: 250, deadv: 200)]
        TAInstrumentsAffinityITCStandard = 128,

        [ITCInstrument("TA Instruments Affinity ITC (Low Volume)", "", "TAITC",
            cellv: 185, syrv: 250, deadv: 60)]
        TAInstrumentsAffinityITCLowVolume = 256,

        MicroCal = MicroCalITC200 | MalvernITC200 | MicroCalVPITC
    }

    public enum FeedbackMode
    {
        [FeedbackMode("Null")]
        Null = -1,
        [FeedbackMode("None")]
        None = 0,
        [FeedbackMode("Low")]
        Low = 1,
        [FeedbackMode("High")]
        High = 2
    }
}
