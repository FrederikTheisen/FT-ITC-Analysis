using System;
using System.IO;
using AnalysisITC;
using System.Collections.Generic;
using System.Linq;
using Utilities;
using AppKit;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UniformTypeIdentifiers;
using Foundation;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace DataReaders
{
    public static class DataReader
    {
        static void AddData(ITCDataContainer data)
        {
            bool valid = true;
            if (data is ExperimentData) valid = ValidateData(data as ExperimentData);

            if (valid) DataManager.AddData(data);
        }

        static void AddData(ITCDataContainer[] data)
        {
            foreach (var dat in data)
            {
                AddData(dat);
            }
        }

        public static ITCDataFormat GetFormat(string path)
        {
            try
            {
                var ext = System.IO.Path.GetExtension(path);

                foreach (var format in ITCFormatAttribute.GetAllFormats())
                {
                    var fprop = format.GetProperties();

                    if (ext == fprop.Extension) return format;
                }
            }
            catch
            {
                AppEventHandler.PrintAndLog("GetFormat Error: " + path);
            }

            return ITCDataFormat.Unknown;
        }

        public static async void Read(NSUrl url) => Read(new NSUrl[] { url });

        public static async void Read(IEnumerable<NSUrl> urls)
        {
            StatusBarManager.SetStatus("Reading data...", 0);
            StatusBarManager.StartInderminateProgress();

            try
            {
                await Task.Delay(1);

                foreach (var url in urls)
                {
                    StatusBarManager.SetStatus("Reading file: " + url.LastPathComponent, 0);
                    await Task.Delay(1); //Necessary to update UI. Unclear why whole method has to be on UI thread.
                    var dat = ReadFile(url.Path);

                    if (dat != null)
                    {
                        AddData(dat);

                        NSDocumentController.SharedDocumentController.NoteNewRecentDocumentURL(url);
                        AppSettings.LastDocumentUrl = url;
                    }
                }
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }

            StatusBarManager.ClearAppStatus();
            StatusBarManager.StopIndeterminateProgress();
        }

        static ITCDataContainer[] ReadFile(string path)
        {
            try
            {
                var format = GetFormat(path);

                switch (format)
                {
                    case ITCDataFormat.FTITC: return FTITCReader.ReadPath(path);
                    case ITCDataFormat.ITC200: return new ExperimentData[] { MicroCalITC200Reader.ReadPath(path) };
                    case ITCDataFormat.VPITC: break;
                    case ITCDataFormat.Unknown:
                        AppEventHandler.PrintAndLog($"Unknown File Format: {path}");
                        break;
                }
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
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

    public class RawDataReader
    {
        public static void ProcessInjections(ExperimentData experiment)
        {
            switch (experiment.DataSourceFormat)
            {
                default:
                case ITCDataFormat.ITC200:
                    ProcessInjectionsMicroCal(experiment);
                    break;
            }
        }

        static void ProcessInjectionsMicroCal(ExperimentData experiment)
        {
            var x2vol0 = 2 * experiment.CellVolume;
            var deltaVolume = 0.0;

            foreach (var inj in experiment.Injections)
            {
                deltaVolume += inj.Volume;
                inj.InjectionMass = experiment.SyringeConcentration * inj.Volume;
                inj.ActualCellConcentration = experiment.CellConcentration * ((1 - deltaVolume / x2vol0) / (1 + deltaVolume / x2vol0));
                inj.ActualTitrantConcentration = experiment.SyringeConcentration * (deltaVolume / experiment.CellVolume) * (1 - deltaVolume / x2vol0);
                inj.Ratio = inj.ActualTitrantConcentration / inj.ActualCellConcentration;
            }
        }

        public static void ProcessData(ExperimentData experiment)
        {
            experiment.MeasuredTemperature = experiment.DataPoints.Average(dp => dp.Temperature);

            experiment.CalculatePeakHeatDirection();
        }
    }

    class MicroCalITC200Reader : RawDataReader
    {
        public static ExperimentData ReadPath(string path)
        {
            var experiment = new ExperimentData(Path.GetFileName(path));
            experiment.Date = File.GetLastWriteTimeUtc(path);
            experiment.DataSourceFormat = ITCDataFormat.ITC200;

            using (var stream = new StreamReader(path))
            {
                int counter = 0;
                int counter2 = 0;
                int counter3 = -1;
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
                        continue;
                    }

                    if (counter == 4) experiment.TargetTemperature = LineToFloat(line);
                    else if (counter == 5) experiment.InitialDelay = LineToFloat(line);
                    else if (counter == 6) experiment.StirringSpeed = LineToFloat(line);
                    else if (counter == 7) experiment.TargetPowerDiff = LineToFloat(line);
                    else if (counter == 8) experiment.FeedBackMode = (FeedbackMode)LineToInt(line);
                    else if (counter >= 11 && line[0] == '$')
                    {
                        experiment.AddInjection(line);
                    }
                    else if (line[0] == '#')
                    {
                        counter2++;

                        if (counter2 == 2) experiment.SyringeConcentration = new FloatWithError(LineToFloat(line) * (float)Math.Pow(10, -3));
                        else if (counter2 == 3) experiment.CellConcentration = LineToFloat(line) != 0 ? new FloatWithError(LineToFloat(line) * (float)Math.Pow(10, -3)) : experiment.SyringeConcentration / 10f;
                        else if (counter2 == 4) experiment.CellVolume = LineToFloat(line) * (float)Math.Pow(10, -3);
                    }
                    else if (line[0] == '?')
                    {
                        counter3 = 0;
                    }
                    else if (counter3 == 1)
                    {
                        experiment.Instrument = ITCInstrumentAttribute.GetInstrument(line);
                    }

                    if (counter3 > -1) counter3++;
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
            return float.Parse(line.Substring(1).Trim());
        }

        private static int LineToInt(string line)
        {
            return int.Parse(line.Substring(1).Trim());
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
            var dat = StringParsers.ParseLine(line);

            experiment.DataPoints.Add(new DataPoint(dat[0], (float)Energy.ConvertToJoule(dat[1], EnergyUnit.MicroCal), dat[2], dat[3], dat[4], dat[5], dat[6]));
        }
    }

    class FTITCReader : FTITCFormat
    {
        public static ITCDataContainer[] ReadPath(string path)
        {
            var data = new List<ITCDataContainer>();

            using (var reader = (new StreamReader(path)))
            {
                string line;

                while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                {
                    var input = line.Split(':');

                    if (input[0] == "FILE")
                    {
                        if (input[1] == ExperimentHeader) data.Add(ProcessExperimentData(reader, line));
                    }
                }
            }

            CurrentAccessedAppDocumentPath = path;

            return data.ToArray();
        }

        static ExperimentData ProcessExperimentData(StreamReader reader, string firstline)
        {
            string[] a = firstline.Split(':');

            if (a[1] != ExperimentHeader) return null;

            var exp = new ExperimentData(a[2]);

            string line;

            while ((line = reader.ReadLine()) != EndFileHeader)
            {
                string[] v = line.Split(':');

                switch (v[0])
                {
                    case ID: exp.SetID(v[1]); break;
                    case Date: exp.Date = DateTime.Parse(line[5..]); break;
                    case SourceFormat: exp.DataSourceFormat = (ITCDataFormat)IParse(v[1]); break;
                    case SyringeConcentration: exp.SyringeConcentration = FWEParse(v[1]); break;
                    case CellConcentration: exp.CellConcentration = FWEParse(v[1]); break;
                    case CellVolume: exp.CellVolume = DParse(v[1]); break;
                    case StirringSpeed: exp.StirringSpeed = DParse(v[1]); break;
                    case TargetTemperature: exp.TargetTemperature = DParse(v[1]); break;
                    case MeasuredTemperature: exp.MeasuredTemperature = DParse(v[1]); break;
                    case InitialDelay: exp.InitialDelay = DParse(v[1]); break;
                    case TargetPowerDiff: exp.TargetPowerDiff = DParse(v[1]); break;
                    case UseIntegrationFactorLength: exp.IntegrationLengthMode = (InjectionData.IntegrationLengthMode)IParse(v[1]); break;
                    case IntegrationLengthFactor: exp.IntegrationLengthFactor = FParse(v[1]); break;
                    case FeedBackMode: exp.FeedBackMode = (FeedbackMode)int.Parse(v[1]); break;
                    case Include: exp.Include = BParse(v[1]); break;
                    case "LIST" when v[1] == InjectionList: ReadInjectionList(exp, reader); break;
                    case "LIST" when v[1] == DataPointList: ReadDataList(exp, reader); break;
                    case "LIST" when v[1] == ExperimentAttributes: ReadAttributes(exp, reader); break;
                    case "OBJECT" when v[1] == Processor: ReadProcessor(exp, reader); break;
                }
            }

            RawDataReader.ProcessInjections(exp);

            return exp;
        }

        private static void ReadProcessor(ExperimentData exp, StreamReader reader)
        {
            var p = new DataProcessor(exp);

            string line = reader.ReadLine();
            string[] v = line.Split(':');

            if (v[0] != ProcessorType) return;

            p.InitializeBaseline((BaselineInterpolatorTypes)int.Parse(v[1]));

            while ((line = reader.ReadLine()) != EndObjectHeader)
            {
                v = line.Split(':');

                switch (v[0])
                {
                    case SplineHandleMode: (p.Interpolator as SplineInterpolator).HandleMode = (SplineInterpolator.SplineHandleMode)IParse(v[1]); break;
                    case SplineAlgorithm: (p.Interpolator as SplineInterpolator).Algorithm = (SplineInterpolator.SplineInterpolatorAlgorithm)IParse(v[1]); break;
                    case SplineLocked: (p.Interpolator as SplineInterpolator).IsLocked = BParse(v[1]); break;
                    case SplineFraction: (p.Interpolator as SplineInterpolator).FractionBaseline = FParse(v[1]); break;
                    case "LIST" when v[1] == SplinePointList: ReadSplineList(p.Interpolator as SplineInterpolator, reader); break;
                    case PolynomiumDegree: (p.Interpolator as PolynomialLeastSquaresInterpolator).Degree = IParse(v[1]); break;
                    case PolynomiumLimit: (p.Interpolator as PolynomialLeastSquaresInterpolator).ZLimit = DParse(v[1]); break;
                }
            }

            exp.SetProcessor(p);

            p.ProcessData(replace: false);
        }

        static void ReadSplineList(SplineInterpolator interpolator, StreamReader reader)
        {
            var splinepoints = new List<SplineInterpolator.SplinePoint>();

            string line;

            while ((line = reader.ReadLine()) != EndListHeader)
            {
                var _spdat = line.Split(',');
                splinepoints.Add(new SplineInterpolator.SplinePoint(double.Parse(_spdat[0]), double.Parse(_spdat[1]), int.Parse(_spdat[2]), double.Parse(_spdat[3])));
            }

            interpolator.SetSplinePoints(splinepoints) ;
        }

        private static void ReadAttributes(ExperimentData exp, StreamReader reader)
        {
            var attributes = new List<ModelOptions>();

            string line;

            while ((line = reader.ReadLine()) != EndListHeader)
            {
                var dat = line.Split(';');

                var opt = ModelOptions.FromKey((ModelOptionKey)IParse(dat[1]));
                opt.BoolValue = BParse(dat[2].Split(':')[1]);
                opt.IntValue = IParse(dat[3].Split(':')[1]);
                opt.DoubleValue = DParse(dat[4].Split(':')[1]);
                opt.ParameterValue = FWEParse(dat[5].Split(':')[1]);

                attributes.Add(opt);
            }

            foreach (var att in attributes)
                exp.ExperimentOptions.Add(att.DictionaryEntry);
        }

        static void ReadInjectionList(ExperimentData exp, StreamReader reader)
        {
            var injections = new List<InjectionData>();

            string line;

            while ((line = reader.ReadLine()) != EndListHeader) injections.Add(new InjectionData(exp, line));

            exp.Injections = injections;
        }

        static void ReadDataList(ExperimentData exp, StreamReader reader)
        {
            var datapoints = new List<DataPoint>();

            string line;

            while ((line = reader.ReadLine()) != EndListHeader)
            {
                var dp = line.Split(',');
                datapoints.Add(new DataPoint(float.Parse(dp[0]), float.Parse(dp[1]), float.Parse(dp[2]), shieldt: float.Parse(dp[3])));
            }

            exp.DataPoints = datapoints;
        }
    }

    class FTITCReaderOld : FTITCFormat
    {
        public static ITCDataContainer[] ReadPath(string path)
        {
            var data = new List<ITCDataContainer>();

            var file = string.Join("", File.ReadAllLines(path));

            Regex regex = new Regex(OldReaderPattern(ExperimentHeader), RegexOptions.Singleline | RegexOptions.Compiled);
            var v = regex.Matches(file);

            foreach (var m in v.AsEnumerable())
            {
                var result = m.Value;

                data.Add(ProcessExperimentData(result.Substring(ExperimentHeader.Length + 2, result.Length - (2 * ExperimentHeader.Length + 5))));
            }

            return data.ToArray();
        }

        static string GContent(string header, string data)
        {
            Regex regex = new Regex(OldReaderPattern(header), RegexOptions.Singleline | RegexOptions.Compiled);
            var v = regex.Matches(data);

            if (v.Count == 0) return null;

            var result = v.First().Value;

            return result.Substring(header.Length + 2, result.Length - (2 * header.Length + 5));
        }

        static ExperimentData ProcessExperimentData(string data)
        {
            var exp = new ExperimentData(GContent(FileName, data));
            exp.SetID(GContent(ID, data));
            exp.Date = DateTime.Parse(GContent(Date, data));
            //exp.Include = GContent(Include, data) == "1";
            exp.SyringeConcentration = FWEParse(GContent(SyringeConcentration, data));
            exp.CellConcentration = FWEParse(GContent(CellConcentration, data));
            exp.StirringSpeed = double.Parse(GContent(StirringSpeed, data));
            exp.TargetTemperature = double.Parse(GContent(TargetTemperature, data));
            exp.MeasuredTemperature = double.Parse(GContent(MeasuredTemperature, data));
            exp.InitialDelay = double.Parse(GContent(InitialDelay, data));
            exp.TargetPowerDiff = double.Parse(GContent(TargetPowerDiff, data));
            exp.Include = GContent(Include, data) == "1";
            exp.IntegrationLengthMode = (InjectionData.IntegrationLengthMode)int.Parse(GContent(UseIntegrationFactorLength, data));
            exp.IntegrationLengthFactor = float.Parse(GContent(IntegrationLengthFactor, data));
            exp.CellVolume = double.Parse(GContent(CellVolume, data));
            exp.CellVolume = double.Parse(GContent(CellVolume, data));
            exp.FeedBackMode = (FeedbackMode)int.Parse(GContent(FeedBackMode, data));
            exp.Instrument = GContent(Instrument, data) != null ? (ITCInstrument)int.Parse(GContent(Instrument, data)) : ITCInstrument.Unknown;

            var datapoints = new List<DataPoint>();

            var dpdata = GContent(DataPointList, data).Split(";");
            foreach (var dp in dpdata)
            {
                var _dp = dp.Split(',');
                datapoints.Add(new DataPoint(float.Parse(_dp[0]), float.Parse(_dp[1]), float.Parse(_dp[2]), shieldt: float.Parse(_dp[3])));
            }

            exp.DataPoints = datapoints;

            var injections = new List<InjectionData>();

            var injdata = GContent(InjectionList, data).Split(";");
            foreach (var inj in injdata)
            {
                injections.Add(new InjectionData(exp, inj));
            }

            exp.Injections = injections;

            string processordata = GContent(Processor, data);
            {
                if (!string.IsNullOrEmpty(processordata))
                {
                    var processor = new DataProcessor(exp);
                    processor.InitializeBaseline((BaselineInterpolatorTypes)int.Parse(GContent(ProcessorType, processordata)));

                    switch (processor.BaselineType)
                    {
                        case BaselineInterpolatorTypes.None: break;
                        default:
                        case BaselineInterpolatorTypes.Spline:
                            (processor.Interpolator as SplineInterpolator).Algorithm = (SplineInterpolator.SplineInterpolatorAlgorithm)int.Parse(GContent(SplineAlgorithm, processordata));
                            (processor.Interpolator as SplineInterpolator).HandleMode = (SplineInterpolator.SplineHandleMode)int.Parse(GContent(SplineHandleMode, processordata));
                            (processor.Interpolator as SplineInterpolator).FractionBaseline = float.Parse(GContent(SplineFraction, processordata));
                            (processor.Interpolator as SplineInterpolator).IsLocked = GContent(SplineLocked, processordata) == "1";
                            var splinepoints = new List<SplineInterpolator.SplinePoint>();

                            var spdata = GContent(SplinePointList, data).Split(";");
                            foreach (var sp in spdata)
                            {
                                var _spdat = sp.Split(',');
                                splinepoints.Add(new SplineInterpolator.SplinePoint(double.Parse(_spdat[0]), double.Parse(_spdat[1]), int.Parse(_spdat[2]), double.Parse(_spdat[3])));
                            }

                            (processor.Interpolator as SplineInterpolator).SetSplinePoints(splinepoints);
                            break;
                        case BaselineInterpolatorTypes.Polynomial:
                            (processor.Interpolator as PolynomialLeastSquaresInterpolator).Degree = int.Parse(GContent(PolynomiumDegree, processordata));
                            (processor.Interpolator as PolynomialLeastSquaresInterpolator).ZLimit = int.Parse(GContent(PolynomiumLimit, processordata));
                            break;
                    }

                    exp.SetProcessor(processor);

                    processor.ProcessData(replace: false);
                }
            }


            return exp;
        }
    }
}
