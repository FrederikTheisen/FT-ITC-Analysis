﻿using System;
using System.IO;
using AnalysisITC;
using System.Collections.Generic;
using System.Linq;
using Utilities;

namespace AnalysisITC
{
    public static class DataManager
    {
        public static List<ExperimentData> Data => DataSource.Data;
        static int SelectedIndex => DataSource.SelectedIndex;

        public static ExperimentDataSource DataSource { get; private set; }

        public static event EventHandler<ExperimentData> DataDidChange;
        public static event EventHandler<ExperimentData> SelectionDidChange;
        public static event EventHandler<int> ChangeProgramMode;

        public static int Count => Data.Count;

        public static bool DataIsLoaded => DataSource?.Data.Count > 0;
        public static bool DataIsProcessed => DataSource.Data.All(d => d.Processor.Completed);

        public static ExperimentData Current()
        {
            if (SelectedIndex == -1) return null;
            else if (SelectedIndex >= Count) return null;
            else return Data[SelectedIndex];
        }

        public static void Init()
        {
            DataSource = new ExperimentDataSource();

            DataDidChange.Invoke(null, null);
        }

        public static void SelectIndex(int index)
        {
            DataSource.SelectedIndex = index;

            SelectionChanged(index);
        }

        internal static void RemoveData(int index)
        {
            Data.RemoveAt(index);

            if (SelectedIndex == index) DataDidChange.Invoke(null, null);
            else if (SelectedIndex > index)
            {
                DataSource.SelectedIndex--;

                DataDidChange.Invoke(null, Current());
            }
        }

        public static void AddData(ExperimentData data)
        {
            Data.Add(data);

            DataDidChange.Invoke(null, data);
        }

        public static void Clear()
        {
            Init();
        }

        public static void SelectionChanged(int index) => SelectionDidChange?.Invoke(null, Current());

        public static void SetMode(int mode)
        {
            ChangeProgramMode.Invoke(null, mode);
        }
    }
}

namespace DataReaders
{
    public static class DataReader
    {
        static List<ExperimentData> ExperimentDataList { get; set; } = new List<ExperimentData>();

        static void AddData(ExperimentData data)
        {
            DataManager.AddData(data);
        }

        public static void Init()
        {
            ExperimentDataList = new List<ExperimentData>();
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
                        experiment.Injections.Add(new InjectionData(line, experiment.Injections.Count));
                    }
                    else if (line[0] == '#')
                    {
                        counter2++;

                        if (counter2 == 2) experiment.SyringeConcentration = LineToFloat(line) * (float)Math.Pow(10, -3);
                        else if (counter2 == 3) experiment.CellConcentration = LineToFloat(line) != 0 ? LineToFloat(line) * (float)Math.Pow(10, -3) : experiment.SyringeConcentration / 10f;
                        else if (counter2 == 4) experiment.CellVolume = LineToFloat(line) * (float)Math.Pow(10, -6);
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
            inj.Include = id == 0;
            inj.Temperature = experiment.DataPoints.Last().Temperature;
        }

        static void ReadDataPoint(ExperimentData experiment, string line)
        {
            experiment.DataPoints.Add(DataPoint.FromLine(line));
        }

        static void ProcessInjections(ExperimentData experiment)
        {
            var deltaVolume = 0.0f;
            var totalmass = 0.0f;

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