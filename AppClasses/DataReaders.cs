using System;
using System.IO;
using AnalysisITC;
using System.Collections.Generic;
using System.Linq;
using Utilities;
using AppKit;
using System.Threading.Tasks;
using UniformTypeIdentifiers;
using Foundation;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.AppClasses.Analysis2.Models;
using AnalysisITC.AppClasses.Analysis2;

namespace DataReaders
{
    public static class DataReader
    {
        static void AddData(ITCDataContainer data)
        {
            if (data == null) return;
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
                    case ITCDataFormat.VPITC: // TODO No idea what vpitc files might look like if they exist
                    case ITCDataFormat.ITC200: return new ExperimentData[] { MicroCalITC200Reader.ReadPath(path) };
                    case ITCDataFormat.TAITC: return new ExperimentData[] { TAFileReader.ReadPath(path) };
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
                case ITCDataFormat.TAITC:
                    foreach (var inj in experiment.Injections) inj.SetIntegrationTimes();
                    ProcessInjectionsMicroCal(experiment); // We think this might be the same proecessing. Lack of information makes other assumptions hard.
                    break;
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

                if (inj.ActualCellConcentration > float.Epsilon) inj.Ratio = inj.ActualTitrantConcentration / inj.ActualCellConcentration;
                else inj.Ratio = inj.ActualTitrantConcentration;
            }
        }

        /// <summary>
        /// Calculate properties for the experiment. Currently determines the measured temperature.
        /// </summary>
        /// <param name="experiment"></param>
        public static void ProcessData(ExperimentData experiment)
        {
            experiment.MeasuredTemperature = experiment.DataPoints.Average(dp => dp.Temperature);

            //experiment.CalculatePeakHeatDirection();
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
                    if (line.Count() == 0) continue;
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
                        experiment.Comments = line.Substring(1).Trim();
                    }
                    else if (counter3 == 1)
                    {
                        experiment.Instrument = ITCInstrumentAttribute.GetInstrument(line);
                    }
                    else if (counter3 == 17)
                    {
                        if (experiment.Instrument == ITCInstrument.MalvernITC200) //Try to get exp date from line
                        {
                            if (line.Contains("Run time:"))
                            {
                                int idx = line.IndexOf("Run time:");
                                var datestr = line.Substring(idx + 9);

                                var b = DateTime.TryParse(datestr, new System.Globalization.CultureInfo("en-US", false), System.Globalization.DateTimeStyles.AllowWhiteSpaces, out DateTime date);

                                if (b) experiment.Date = date;
                            }
                        }
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

    class TAFileReader : RawDataReader
    {
        public static ExperimentData ReadPath(string path)
        {
            var experiment = new ExperimentData(Path.GetFileName(path));
            experiment.Date = File.GetLastWriteTimeUtc(path);
            experiment.DataSourceFormat = ITCDataFormat.TAITC;
            experiment.FeedBackMode = FeedbackMode.High;
            experiment.StirringSpeed = 0;

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
                    if (line.Count() == 0) continue;
                    counter++;
                    if (line == "@0") { isDataStream = true; continue; }

                    if (isDataStream)
                    {
                        if (line.First() == '@') AddInjection(experiment, line);
                        else ReadDataPoint(experiment, line);
                        continue;
                    }

                    if (line[0] == '#')
                    {
                        counter2++;

                        if (counter2 == 2) experiment.SyringeConcentration = new FloatWithError(LineToFloat(line) * (float)Math.Pow(10, -3));
                        else if (counter2 == 3) experiment.CellConcentration = LineToFloat(line) != 0 ? new FloatWithError(LineToFloat(line) * (float)Math.Pow(10, -3)) : experiment.SyringeConcentration / 10f;
                        else if (counter2 == 4) experiment.CellVolume = LineToFloat(line) * (float)Math.Pow(10, -3);
                        else if (counter2 == 5) experiment.TargetTemperature = LineToFloat(line);
                    }
                    else if (line[0] == '?')
                    {
                        counter3 = 0;
                        experiment.Comments += line.Substring(1).Trim();
                    }

                    if (counter3 > -1) counter3++;
                }

                stream.Close();
                Console.WriteLine($"File has {counter} lines.");
            }

            experiment.TargetPowerDiff = experiment.DataPoints.First().Power;
            experiment.InitialDelay = experiment.Injections.First().Time;
            experiment.Instrument = experiment.CellVolume > 0.2e-3 ? ITCInstrument.TAInstrumentsITCStandard : ITCInstrument.TAInstrumentsITCLowVolume;

            ProcessInjections(experiment);
            ProcessData(experiment);

            return experiment;
        }

        private static float LineToFloat(string line)
        {
            return float.Parse(line.Substring(1).Trim());
        }

        static void AddInjection(ExperimentData experiment, string line)
        {
            var data = Utilities.StringParsers.ParseLine(line.Substring(1));
            int id = (int)data[0] - 1;
            double v = data[1] * 1e-6;

            var inj = InjectionData.FromTAFileLine(experiment, id, v, experiment.DataPoints.LastOrDefault(), experiment.Injections.LastOrDefault());

            experiment.Injections.Add(inj);
        }

        static void ReadDataPoint(ExperimentData experiment, string line)
        {
            var dat = StringParsers.ParseLine(line);

            experiment.DataPoints.Add(new DataPoint(dat[0], (float)Energy.ConvertToJoule(dat[1], EnergyUnit.MicroCal), temp: (float)experiment.TargetTemperature));
        }
    }

    class FTITCReader : FTITCFormat
    {
        static List<ITCDataContainer> Data { get; set; }

        public static ITCDataContainer[] ReadPath(string path)
        {
            AppEventHandler.PrintAndLog("Loading File " + path);

            Data = new List<ITCDataContainer>();

            using (var reader = (new StreamReader(path)))
            {
                string line;

                while (!string.IsNullOrEmpty(line = reader.ReadLine()))
                {
                    var input = line.Split(':');

                    if (input[0] == "FILE")
                    {
                        if (input[1] == ExperimentHeader) Data.Add(ReadExperimentDataFile(reader, line));
                        else if (input[1] == TandemExperimentHeader) Data.Add(ReadTandemExperimentDataFile(reader, line));
                        else if (input[1] == AnalysisResultHeader) Data.Add(ReadAnalysisResult(reader, line));
                    }
                }
            }

            CurrentAccessedAppDocumentPath = path;

            return Data.ToArray();
        }

        static ExperimentData ReadTandemExperimentDataFile(StreamReader reader, string firstline)
        {
            AppEventHandler.PrintAndLog("Loading Tandem Experiment Data...");

            string[] a = firstline.Split(':');
            var exp = new ExperimentData(a[2]);

            ReadExperimentData(reader, exp);

            if (exp.Solution != null) exp.UpdateSolution(exp.Solution.Model);

            return exp;
        }

        static ExperimentData ReadExperimentDataFile(StreamReader reader, string firstline)
        {
            AppEventHandler.PrintAndLog("Loading Experiment Data...");

            string[] a = firstline.Split(':');
            var exp = new ExperimentData(a[2]);

            ReadExperimentData(reader, exp);

            RawDataReader.ProcessInjections(exp);

            if (exp.Solution != null) exp.UpdateSolution(exp.Solution.Model);

            return exp;
        }

        static ExperimentData ReadExperimentData(StreamReader reader, ExperimentData exp)
        {
            SolutionInterface sol = null;

            string line;

            while ((line = reader.ReadLine()) != EndFileHeader)
            {
                string[] v = line.Split(':');

                switch (v[0])
                {
                    case ID: exp.SetID(v[1]); break;
                    case Date: exp.Date = DateTime.Parse(line[5..]); break;
                    case SourceFormat: exp.DataSourceFormat = (ITCDataFormat)IParse(v[1]); break;
                    case Comments: exp.Comments = v[1]; break;
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
                    case Instrument: exp.Instrument = (ITCInstrument)IParse(v[1]); break;
                    case "LIST" when v[1] == InjectionList: ReadInjectionList(exp, reader); break;
                    case "LIST" when v[1] == DataPointList: ReadDataList(exp, reader); break;
                    case "LIST" when v[1] == ExperimentAttributes: ReadAttributes(exp, reader); break;
                    case "LIST" when v[1] == SegmentList: ReadSegmentList(exp, reader);  break;
                    case "OBJECT" when v[1] == Processor: ReadProcessor(exp, reader); break;
                    case "OBJECT" when v[1] == ExperimentSolutionHeader: sol = ReadSolution(reader, reader.ReadLine(), exp); break;
                    //case "OBJECT" when v[1] == SolutionHeader: exp.UpdateSolution(ReadSolution(reader, line).Model); break; //Not certain about implementation
                }
            }

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
                    case SplineLocked: if (BParse(v[1])) p.Lock(); break;
                    case SplineFraction: (p.Interpolator as SplineInterpolator).FractionBaseline = FParse(v[1]); break;
                    case "LIST" when v[1] == SplinePointList: ReadSplineList(p.Interpolator as SplineInterpolator, reader); break;
                    case PolynomiumDegree: (p.Interpolator as PolynomialLeastSquaresInterpolator).Degree = IParse(v[1]); break;
                    case PolynomiumLimit: (p.Interpolator as PolynomialLeastSquaresInterpolator).ZLimit = DParse(v[1]); break;
                }
            }

            exp.SetProcessor(p);

            p.ProcessData(replace: false, invalidate: false);
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
            var attributes = ReadAttributeOptions(reader);

            foreach (var att in attributes)
                exp.Attributes.Add(att);
        }

        private static List<ExperimentAttribute> ReadAttributeOptions(StreamReader reader)
        {
            var options = new List<ExperimentAttribute>();

            string line;

            while ((line = reader.ReadLine()) != EndListHeader)
            {
                var dat = line.Split(';');

                var opt = ExperimentAttribute.FromKey((AttributeKey)IParse(dat[1]));
                opt.BoolValue = BParse(dat[2].Split(':')[1]);
                opt.IntValue = IParse(dat[3].Split(':')[1]);
                opt.DoubleValue = DParse(dat[4].Split(':')[1]);
                opt.ParameterValue = FWEParse(dat[5].Split(':')[1]);

                options.Add(opt);
            }

            return options;
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

        static void ReadSegmentList(ExperimentData exp, StreamReader reader)
        {
            string line;

            while ((line = reader.ReadLine()) != EndListHeader) exp.AddSegment(TandemExperimentSegment.FromFile(line));

            exp.InvalidateSegmentLookup();
        }

        static AnalysisResult ReadAnalysisResult(StreamReader reader, string firstline)
        {
            AppEventHandler.PrintAndLog("Loading Analysis Result...");

            try
            {
                var guid = firstline.Split(':')[2];
                var sol = ReadGlobalSolution(reader);

                AnalysisResult result = new AnalysisResult(sol);

                return result;
            }
            catch (Exception ex)
            {
                AppEventHandler.AddLog(ex);

                return null;
            }
        }

        static GlobalSolution ReadGlobalSolution(StreamReader reader)
        {
            reader.ReadLine(); //Header is empty
            string line = reader.ReadLine();
            var mdl = (AnalysisModel)IParse(line.Split(':')[1]);
            GlobalModelFactory factory = new GlobalModelFactory(mdl);
            var datas = new List<ExperimentData>();
            var solutions = new List<SolutionInterface>();
            SolverConvergence conv = null;

            while ((line = reader.ReadLine()) != EndFileHeader)
            {
                var v = line.Split(':');
                switch (v[0])
                {
                    case "LIST" when v[1] == DataRef:
                        {
                            string dref;
                            while ((dref = reader.ReadLine()) != EndListHeader)
                            {
                                datas.Add(Data.Find(d => d.UniqueID == dref) as ExperimentData);
                            }

                            factory.InitializeModel(datas);
                        }
                        break;
                    case "LIST" when v[1] == SolConstraints:
                        {
                            string line2;
                            while ((line2 = reader.ReadLine()) != EndListHeader)
                            {
                                var dat = line2.Split(':');
                                var par = (ParameterType)int.Parse(dat[1]);
                                var con = (VariableConstraint)IParse(dat[2]);

                                factory.Model.Parameters.SetConstraintForParameter(par, con);
                            }
                        }
                        break;
                    case "LIST" when v[1] == SolParams:
                        {
                            string line2;
                            while ((line2 = reader.ReadLine()) != EndListHeader)
                            {
                                var dat = line2.Split(':');
                                var par = (ParameterType)int.Parse(dat[1]);
                                var val = DParse(dat[2]);

                                factory.Model.Parameters.AddorUpdateGlobalParameter(par, val);
                            }
                        }
                        break;
                    case "LIST" when v[1] == SolutionList:
                        {
                            string solline;
                            while ((solline = reader.ReadLine()) != EndListHeader)
                            {
                                var sol = ReadSolution(reader, solline);
                                if (sol == null) break;
                                factory.Model.Models.Find(mdl => mdl.Data.UniqueID == sol.Data.UniqueID).Solution = sol;

                                solutions.Add(sol);
                            }
                        }
                        break;
                    case "OBJECT" when v[1] == SolConvergence:
                        {
                            conv = ReadConvergenceObject(reader);
                            reader.ReadLine();

                            break;
                        }
                    case "OBJECT" when v[1] == MdlCloneOptions:
                        {
                            var mco = new ModelCloneOptions();
                            string mcoline;
                            while ((mcoline = reader.ReadLine()) != EndObjectHeader)
                            {
                                var vv = line.Split(':');
                                switch (vv[0])
                                {
                                    case SolErrorMethod: mco.ErrorEstimationMethod = (ErrorEstimationMethod)IParse(v[1]); break;
                                    case SolCloneConcentrationVariance: mco.IncludeConcentrationErrorsInBootstrap = BParse(v[1]); break;
                                    case SolCloneAutoVariance: mco.EnableAutoConcentrationVariance = BParse(v[1]); break;
                                    case SolCloneAutoVarianceValue: mco.AutoConcentrationVariance = DParse(v[1]); break;
                                }
                            }

                            factory.Model.ModelCloneOptions = mco;
                        }
                        break;
                }
            }

            if (solutions.Count > 0) factory.Model.Solution = new GlobalSolution(new GlobalSolver() { Model = factory.Model, ErrorEstimationMethod = solutions[0].ErrorMethod }, solutions, conv);

            foreach (var sol in solutions) sol.SetIsGlobal(factory.Model.Solution);

            return factory.Model.Solution;
        }

        static SolutionInterface ReadSolution(StreamReader reader, string firstline, ExperimentData experimentData = null)
        {
            try
            {
                SingleModelFactory factory = null;
                string guid = firstline.Split(':')[2];
                string dataref = reader.ReadLine().Split(':')[1];
                var mdltype = (AnalysisModel)IParse(reader.ReadLine().Split(':')[1]);

                factory = new AnalysisITC.AppClasses.Analysis2.SingleModelFactory(mdltype);
                if (experimentData == null)
                    factory.InitializeModel(Data.Find(d => d.UniqueID == dataref) as ExperimentData);
                else factory.ConstructModel(experimentData);
                SolverConvergence conv = null;
                double loss;
                List<Parameter> parameters = null;
                List<SolutionInterface> bsols = null;

                string line;
                while ((line = reader.ReadLine()) != EndFileHeader)
                {
                    var v = line.Split(':');
                    switch (v[0])
                    {
                        //case SolErrorMethod: factory.Model.MCO = (ErrorEstimationMethod)IParse(v[1]); break;
                        case "LIST" when v[1] == SolParams:
                            parameters = new List<Parameter>();
                            string line2;
                            while ((line2 = reader.ReadLine()) != EndListHeader)
                            {
                                var dat = line2.Split(':');
                                var par = (ParameterType)int.Parse(dat[1]);
                                var val = DParse(dat[2]);

                                parameters.Add(new Parameter(par, val));
                            }
                            break;
                        case "LIST" when v[1] == SolBootstrapSolutions:
                            bsols = new List<SolutionInterface>();
                            var bsol = "";
                            while ((bsol = reader.ReadLine()) != EndListHeader)
                            {
                                bsols.Add(ReadSolution(reader, bsol, factory.Model.Data));
                            }
                            break;
                        case "LIST" when v[1] == MdlOptions: ReadModelOptions(factory.Model, reader); break;
                        case "OBJECT" when v[1] == SolConvergence: conv = ReadConvergenceObject(reader); break;

                    }
                }

                foreach (var par in parameters)
                    factory.Model.Parameters.AddOrUpdateParameter(par.Key, par.Value);

                factory.Model.Solution = SolutionInterface.FromModel(factory.Model, conv);

                if (bsols != null) factory.Model.Solution.SetBootstrapSolutions(bsols);

                return factory.Model.Solution;
            }
            catch (Exception ex)
            {
                ex.Source = "Solution Reading Error: " + firstline;
                AppEventHandler.DisplayHandledException(ex);

                return null;
            }
        }

        private static void ReadModelOptions(Model mdl, StreamReader reader)
        {
            List<ExperimentAttribute> options = ReadAttributeOptions(reader);

            foreach (var att in options)
            {
                if (mdl.ModelOptions.ContainsKey(att.Key))
                {
                    mdl.ModelOptions[att.Key] = att;
                }
                else mdl.ModelOptions.Add(att.DictionaryEntry);
            }
        }

        private static SolverConvergence ReadConvergenceObject(StreamReader reader)
        {
            SolverConvergence conv;
            var dat = reader.ReadLine().Split(';');
            var dict = new Dictionary<string, string>();
            foreach (var d in dat.Where(s => !string.IsNullOrEmpty(s)))
                dict.Add(d.Split(':')[0], d.Split(':')[1]);

            conv = SolverConvergence.FromSave(
                IParse(dict[SolIterations]),
                DParse(dict[SolLoss]),
                TSParse(dict[SolConvTime]),
                TSParse(dict[SolConvBootstrapTime]),
                (SolverAlgorithm)IParse(dict[SolConvAlgorithm]),
                dict[SolConvMsg],
                BParse(dict[SolConvFailed]));
            return conv;
        }
    }
}
