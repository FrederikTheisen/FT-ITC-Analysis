using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC;

namespace DataReaders
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
                ITCInstrument.TAInstrumentsITCLowVolume,
                ITCInstrument.TAInstrumentsITCStandard,
            };
        }

        public static ITCInstrument TryResolveMicroCalInstrument(string line)
        {
            foreach (var instrument in GetITCInstruments())
            {
                if (line.Contains(instrument.GetProperties().InstrumentCode)) return instrument;
            }

            return ITCInstrument.Unknown;
        }

        public static void ResolveInstrument(ExperimentData data)
        {
            if (data.Instrument != ITCInstrument.Unknown) return;
            if (data.CellVolume <= 0) return;

            AppEventHandler.PrintAndLog("Resolving Instrument...");

            var candidates = new List<(double, ITCInstrument)>();

            foreach (var instrument in GetITCInstruments())
            {
                var instrument_cell_volume = instrument.GetProperties().StandardCellVolume;
                var delta_v = Math.Abs(data.CellVolume - instrument_cell_volume);

                if (delta_v < instrument_cell_volume * 0.3f)
                    candidates.Add((delta_v, instrument));
            }

            if (candidates.Count > 0)
            {
                data.Instrument = candidates.OrderBy(instr => instr.Item1).First().Item2;
            }

            AppEventHandler.PrintAndLog($"{data.Instrument}", 1);
        }

        public static string SourceInstrumentTitle(ExperimentData data)
        {
            return data.Instrument switch
            {
                ITCInstrument.TAInstrumentsITCStandard or
                ITCInstrument.TAInstrumentsITCLowVolume or
                ITCInstrument.MicroCalVPITC or
                ITCInstrument.MalvernITC200 or
                ITCInstrument.MicroCalITC200 => data.Instrument.GetProperties().Name,
                _ => data.DataSourceFormat switch
                {
                    ITCDataFormat.IntegratedHeats => "Integrated Heat File",
                    _ => "Unknown Source Type",
                },
            };
        }
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
            cellv: 190.0,
            syrv: 250,
            deadv: 60)]
        TAInstrumentsITCLowVolume = 16,

        MicroCal = MicroCalITC200 | MalvernITC200 | MicroCalVPITC 
    }

    public enum FeedbackMode
    {
        [FeedbackMode("None")]
        None = 0,
        [FeedbackMode("Low")]
        Low = 1,
        [FeedbackMode("High")]
        High = 2
    }
}
