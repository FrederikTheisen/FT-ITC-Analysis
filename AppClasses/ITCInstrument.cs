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

        public ITCInstrumentAttribute(string name, string description, string extension, double cellv, double syrv)
        {
            Name = name;
            Description = description;
            Extension = extension;
            StandardCellVolume = cellv / 1000000;
            StandardSyringeVolume = (syrv) / 1000000;
        }

        public static List<ITCInstrument> GetITCInstruments()
        {
            return new List<ITCInstrument>
            {
                ITCInstrument.MicroCalITC200,
                ITCInstrument.MalvernITC200,
                ITCInstrument.MicroCalVPITC,
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
        [ITCInstrument("Unknown", "", "", 200, 40)]
        Unknown,
        [ITCInstrument("MicroCal ITC200", "", "ITC200_", 204, 39.84)]
        MicroCalITC200,
        [ITCInstrument("MicroCal PEAQ-ITC", "", "MICROCALITC_MAL", 207.1, 39.84)]
        MalvernITC200,
        [ITCInstrument("MicroCal VP-ITC", "", "VPITC", 1479.1, 310)]
        MicroCalVPITC,
    }
}
