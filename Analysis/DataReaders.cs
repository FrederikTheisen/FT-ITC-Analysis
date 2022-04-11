using System;
using System.IO;
using AnalysisITC;
using System.Collections.Generic;
using System.Linq;
using Utilities;
using AppKit;

namespace DataReaders
{
    public static class DataReader
    {
        static void AddData(ExperimentData data)
        {
            bool valid = ValidateData(data);

            if (valid) DataManager.AddData(data);
        }

        public static ITCDataFormat GetFormat(string path)
        {
            var ext = System.IO.Path.GetExtension(path);

            foreach (var format in ITCFormatAttribute.GetAll())
            {
                var fprop = format.GetProperties();

                if (ext == fprop.Name) return format;
            }

            return ITCDataFormat.ITC200;
        }

        public static void Read(List<string> paths)
        {
            foreach (var path in paths)
            {
                var dat = ReadFile(path);

                dat.Date = File.GetLastWriteTimeUtc(path);

                if (dat != null) AddData(dat);
            }
        }

        static ExperimentData ReadFile(string path)
        {
            try
            {
                var format = GetFormat(path);

                switch (format)
                {
                    case ITCDataFormat.ITC200: return MicroCalITC200.ReadPath(path);
                    case ITCDataFormat.VPITC: break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("ReadFile Error: " + ex.StackTrace + " " + ex.Message);
            }

            return null;
        }

        static bool ValidateData(ExperimentData data)
        {
            string errormsg = "";
            DataFixProtocol fixable = DataFixProtocol.None;

            if (data.DataPoints.Count < 10) errormsg = "Data contains very few data points";
            else if (DataManager.Data.Exists(dat => dat.FileName == data.FileName)) { errormsg = "Experiment with same file name already exists"; fixable = DataFixProtocol.FileExists; }
            else if (DataManager.Data.Exists(dat => dat.MeasuredTemperature == data.MeasuredTemperature)) { errormsg = "Experiment appears identical to: " + DataManager.Data.Find(dat => dat.MeasuredTemperature == data.MeasuredTemperature).FileName; }
            else if (data.InjectionCount == 0) errormsg = "Data contains no injections";
            else if (data.Injections.Any(inj => inj.Time < 0)) { errormsg = "Data contains injections with no connected data. Attempt to fix problematic injections?"; fixable = DataFixProtocol.InvalidInjection; }
            else if (data.Injections.All(inj => !data.DataPoints.Any(dp => dp.Time > inj.Time + 10))) { errormsg = "Data contains injections outside the recorded data range. Attempt to fix problematic injections?"; fixable = DataFixProtocol.InvalidInjection; }
            else if (Math.Abs(data.MeasuredTemperature - data.TargetTemperature) > 0.5) errormsg = "Measured temperature deviates from target temperature"; 

            if (errormsg != "") using (var alert = new NSAlert()
            {
                AlertStyle = NSAlertStyle.Critical,
                MessageText = "Potential Error Detected: " + data.FileName,
                InformativeText = errormsg,
            })
                {
                    alert.AddButton("Discard");
                    alert.AddButton("Keep");
                    if (fixable != DataFixProtocol.None) alert.AddButton("Attempt Fix");
                    var response = alert.RunModal();

                    switch (response)
                    {
                        case 1000: return false;
                        case 1001: return true;
                        case 1002: return ValidateData(AttemptDataFix(data, fixable));
                    }
                }

            return true;
        }

        enum DataFixProtocol
        {
            None,
            FileExists,
            InvalidInjection,
            Concentrations,
        }

        static ExperimentData AttemptDataFix(ExperimentData data, DataFixProtocol fix)
        {
            switch (fix)
            {
                case DataFixProtocol.FileExists: data.IterateFileName(); break;
                case DataFixProtocol.InvalidInjection: break;
                case DataFixProtocol.Concentrations: break;
            }

            return data;
        }
    }

    public static class MicroCalITC200
    {
        public static ExperimentData ReadPath(string path)
        {
            var experiment = new ExperimentData(Path.GetFileName(path));

            using (var stream = new StreamReader(path))
            {
                int counter = 0;
                int counter2 = 0;
                string line;

                bool isDataStream = false;

                while ((line = stream.ReadLine()) != null)
                {
                    line = line.Trim();
                    counter++;
                    if (line == "@0") { isDataStream = true; continue; }

                    if (isDataStream)
                    {
                        if (line.First() == '@') ReadInjection(experiment, line);
                        else ReadDataPoint(experiment, line);
                    }

                    if (counter == 4) experiment.TargetTemperature = LineToFloat(line);
                    else if (counter == 5) experiment.InitialDelay = LineToFloat(line);
                    else if (counter == 6) experiment.StirringSpeed = LineToFloat(line);
                    else if (counter == 7) experiment.TargetPowerDiff = LineToFloat(line);
                    else if (counter >= 11 && line[0] == '$')
                    {
                        experiment.AddInjection(line);
                    }
                    else if (line[0] == '#')
                    {
                        counter2++;

                        if (counter2 == 2) experiment.SyringeConcentration = LineToFloat(line) * (float)Math.Pow(10, -3);
                        else if (counter2 == 3) experiment.CellConcentration = LineToFloat(line) != 0 ? LineToFloat(line) * (float)Math.Pow(10, -3) : experiment.SyringeConcentration / 10f;
                        else if (counter2 == 4) experiment.CellVolume = LineToFloat(line) * (float)Math.Pow(10, -3);
                    }
                }

                stream.Close();
                Console.WriteLine($"File has {counter} lines.");
            }

            ProcessInjections(experiment);

            ProcessData(experiment);

            return experiment;
        }

        private static float LineToFloat(string line)
        {
            Console.WriteLine(line);
            return float.Parse(line.Substring(1).Trim());
        }

        static void ReadInjection(ExperimentData experiment, string line)
        {
            var data = Utilities.StringParsers.ParseLine(line.Substring(1));
            int id = (int)data[0] - 1;

            var inj = experiment.Injections.Find(o => o.ID == id);

            inj.Time = data[3];
            inj.Include = id != 0;
            inj.Temperature = experiment.DataPoints.Last().Temperature;
        }

        static void ReadDataPoint(ExperimentData experiment, string line)
        {
            experiment.DataPoints.Add(new DataPoint(line, ITCDataFormat.ITC200));
        }

        static void ProcessInjections(ExperimentData experiment)
        {
            var deltaVolume = 0.0;
            var totalmass = 0.0;

            foreach (var inj in experiment.Injections)
            {
                inj.InjectionMass = experiment.SyringeConcentration * inj.Volume;

                deltaVolume += inj.Volume;
                totalmass += inj.InjectionMass;

                var vcon = deltaVolume / (2 * experiment.CellVolume);

                var conc_ligand = totalmass / (experiment.CellVolume + deltaVolume * 0.5f);
                var conc_macro = experiment.CellConcentration * (1 - vcon) / (1 + vcon);

                inj.ActualCellConcentration = conc_macro;
                inj.ActualTitrantConcentration = conc_ligand;
                inj.Ratio = conc_ligand / conc_macro;
            }

            experiment.CalculatePeakHeatDirection();
        }

        static void ProcessData(ExperimentData experiment)
        {
            experiment.MeasuredTemperature = experiment.DataPoints.Average(dp => dp.Temperature);
        }
    }

    public class ITCFormatAttribute : Attribute
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Extension { get; set; }

        public ITCFormatAttribute(string name, string description, string extension)
        {
            Name = name;
            Description = description;
            Extension = extension;
        }

        public static List<ITCDataFormat> GetAll()
        {
            return new List<ITCDataFormat>
            {
                ITCDataFormat.ITC200,
                ITCDataFormat.VPITC
            };
        }

        public static string[] GetAllExtensions()
        {
            var formats = GetAll();

            var list = new string[0];

            foreach (var f in formats)
            {
                var props = f.GetProperties();

                list.Append(props.Extension);
            }

            return list;
        }
    }

    public enum ITCDataFormat
    {
        [ITCFormat("MicroCal ITC-200","Data format produced by the MicroCal ITC200 instrument", ".itc")]
        ITC200,
        [ITCFormat("VP-ITC", "Data format produced by the VP-ITC instrument", ".vpitc")]
        VPITC
    }
}
