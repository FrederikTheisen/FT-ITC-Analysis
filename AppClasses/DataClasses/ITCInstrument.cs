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
        public string Extension { get; set; }
        public double StandardCellVolume { get; private set; }
        public double StandardSyringeVolume { get; private set; }
        public double DeadVolume { get; private set; }

        public ITCInstrumentAttribute(string name, string description, string extension, double cellv, double syrv, double deadv = 0.0)
        {
            Name = name;
            Description = description;
            Extension = extension;
            StandardCellVolume = cellv / 1000000;
            StandardSyringeVolume = (syrv) / 1000000;

            if (deadv < 1)
            {
                deadv = 0.4 * StandardCellVolume;
            }

            DeadVolume = (double)deadv;
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

        public static ITCInstrument GetInstrument(string line)
        {
            foreach (var ins in GetITCInstruments())
            {
                if (line.Contains(ins.GetProperties().Extension)) return ins;
            }

            return ITCInstrument.Unknown;
        }
    }

    public enum ITCInstrument
    {
        [ITCInstrument("Unknown", "", "",
            cellv: 200,
            syrv: 40,
            deadv: 0)]
        Unknown,

        [ITCInstrument("MicroCal ITC200", "", "ITC200_",
            cellv: 204,
            syrv: 39.5,
            deadv: 80)]
        MicroCalITC200,

        [ITCInstrument("MicroCal PEAQ-ITC", "", "MICROCALITC_MAL",
            cellv: 207.1,
            syrv: 39.0,
            deadv: 80)]
        MalvernITC200,

        [ITCInstrument("MicroCal VP-ITC", "", "VPITC",
            cellv: 1479.1,
            syrv: 310,
            deadv: 400)]
        MicroCalVPITC,

        [ITCInstrument("TA Instruments ITC Standard", "", "TAITC",
            cellv: 1000.0,
            syrv: 250,
            deadv: 200)]
        TAInstrumentsITCStandard,

        [ITCInstrument("TA Instruments ITC Low Vol", "", "TAITC",
            cellv: 190.0,
            syrv: 250,
            deadv: 60)]
        TAInstrumentsITCLowVolume,
    }
}
