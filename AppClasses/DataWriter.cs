using System;
using AppKit;
using Foundation;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using AnalysisITC.AppClasses.Analysis2;
using AnalysisITC.AppClasses.Analysis2.Models;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC
{
    public class FTITCFormat
    {
        public const string ExperimentHeader = "Experiment";
        public const string ID = "ID";
        public const string FileName = "FileName";
        public const string Comments = "Comments";
        public const string Date = "Date";
        public const string SourceFormat = "Source";
        public const string Instrument = "Instrument";
        public const string SyringeConcentration = "SyringeConcentration";
        public const string CellConcentration = "CellConcentration";
        public const string CellVolume = "CellVolume";
        public const string StirringSpeed = "StirringSpeed";
        public const string TargetTemperature = "TargetTemperature";
        public const string MeasuredTemperature = "MeasuredTemperature";
        public const string InitialDelay = "InitialDelay";
        public const string TargetPowerDiff = "TargetPowerDiff";
        public const string UseIntegrationFactorLength = "UseIntegrationFactorLength";
        public const string IntegrationLengthFactor = "IntegrationLengthFactor";
        public const string FeedBackMode = "FeedBackMode";
        public const string Include = "Include";
        public const string InjectionList = "InjectionList";
        public const string DataPointList = "DataPointList";
        public const string Processor = "DataProcessor";
        public const string ProcessorType = "ProcessorType";
        public const string SplineHandleMode = "SHandleMode";
        public const string SplineAlgorithm = "SAlgorithm";
        public const string SplineLocked = "SLocked";
        public const string SplineFraction = "SFraction";
        public const string PolynomiumDegree = "PDegree";
        public const string PolynomiumLimit = "PLimit";
        public const string SplinePointList = "SPList";
        public const string SolutionGUID = "SolutionGUID";
        public const string ExperimentAttributes = "ExpAttributes";
        public const string ExperimentSolutionHeader = "Solution";

        public const string SolutionHeader = "SolutionFile";
        public const string DataRef = "DataGUID";
        public const string SolModel = "MDL";
        public const string SolErrorMethod = "ErrorMethod";
        public const string SolCloneConcentrationVariance = "ConcVar";
        public const string SolCloneAutoVariance = "AutoConcVar";
        public const string SolCloneAutoVarianceValue = "AutoConcVarValue";
        public const string SolParamsRaw = "RawParameters";
        public const string SolParams = "Parameters";
        public const string SolConstraints = "SolCons";
        public const string SolLoss = "Loss";
        public const string SolBootN = "BootstrapIterations";
        public const string SolBootstrapSolutions = "BootSolutions";
        public const string SolConvergence = "Conv";
        public const string SolIterations = "Iter";
        public const string SolConvMsg = "MSG";
        public const string SolConvTime = "TIME";
        public const string SolConvBootstrapTime = "BTIME";
        public const string SolConvFailed = "Failed";
        public const string SolConvAlgorithm = "Algorithm";
        public const string MdlCloneOptions = "MdlClOpts";
        public const string MdlOptions = "MdlOpts";

        public const string GlobalSolutionHeader = "GlobalSolutionFile";
        public const string SolutionList = "SolutionList";

        public const string AnalysisResultHeader = "AnalysisResult";

        //ftitc_old formatting
        public static string OldHeader(string header) => "<" + header + ">";
        public static string OldEndHeader(string header) => OldHeader("/" + header);
        public static string OldEncapsulate(string header, string content) => OldHeader(header) + content + OldEndHeader(header) + Environment.NewLine;
        public static string OldEncapsulate(string header, double value) => OldEncapsulate(header, value.ToString());
        public static string OldEncapsulate(string header, bool value) => OldEncapsulate(header, value ? 1 : 0);

        public static string OldReaderPattern(string header) => OldHeader(header) + ".*?" + OldEndHeader(header);

        //ftitc formatting code
        public const string EndFileHeader = "ENDFILE";
        public const string EndListHeader = "ENDLIST";
        public const string EndObjectHeader = "ENDOBJECT";

        public static string FileHeader(string header, string filename) => "FILE:" + header + ":" + filename;
        public static string ObjectHeader(string header) => "OBJECT:" + header; 
        public static string Variable(string header, string value) => header + ":" + value;
        public static string Variable(string header, double value) => Variable(header, value.ToString());
        public static string Variable(string header, bool value) => Variable(header, value ? "1" : "0");
        public static string Variable(string header, FloatWithError value) => Variable(header, value.Value + "," + value.SD);
        public static string Variable(string header, Energy value) => Variable(header, value.FloatWithError);
        public static string ListHeader(string header) => "LIST:" + header;
        public static string Attribute(ModelOptions opt)
        {
            string str = "";

            str += opt.Key.ToString() + ";"
                + (int)opt.Key + ";"
                + Variable("B", opt.BoolValue) + ";"
                + Variable("I", opt.IntValue) + ";"
                + Variable("D", opt.DoubleValue) + ";"
                + Variable("FWE", opt.ParameterValue);

            return str;
        }

        public static double DParse(string value) => double.Parse(value);
        public static float FParse(string value) => float.Parse(value);
        public static int IParse(string value) => int.Parse(value);
        public static bool BParse(string value) => value == "1";
        public static Energy EParse(string value) => new Energy(FWEParse(value));
        public static FloatWithError FWEParse(string value)
        {
            var s = value.Split(",");

            if (s.Length > 1) return new FloatWithError(DParse(s[0]), DParse(s[1]));
            else return new FloatWithError(DParse(s[0]));
        }
        public static TimeSpan TSParse(string value) => TimeSpan.FromSeconds(DParse(value));

        public static string CurrentAccessedAppDocumentPath { get; set; } = "";
    }

    public class FTITCWriter : FTITCFormat
    {
        public static bool IsSaved => !string.IsNullOrEmpty(CurrentAccessedAppDocumentPath);

        public static void SaveState2()
        {
            var dlg = new NSSavePanel();
            dlg.Title = "Save FT-ITC File";
            dlg.AllowedFileTypes = new string[] { "ftitc" };

            dlg.BeginSheet(NSApplication.SharedApplication.MainWindow, async (result) =>
            {
                if (result == 1)
                {
                    await WriteFile(dlg.Filename);

                    CurrentAccessedAppDocumentPath = dlg.Filename;
                    AppSettings.LastDocumentUrl = dlg.Url;
                }
            });
        }

        public static async void SaveWithPath()
        {
            await WriteFile(CurrentAccessedAppDocumentPath);
        }

        public static void SaveSelected(ITCDataContainer data)
        {
            var dlg = new NSSavePanel();
            dlg.Title = "Save FT-ITC " + (data is ExperimentData ? "Experiment Data" : "Analysis Results");
            dlg.AllowedFileTypes = (data is ExperimentData ? new string[] { "ftitc" } : new string[] { "ftitc", "csv" });

            dlg.BeginSheet(NSApplication.SharedApplication.MainWindow, async (result) =>
            {
                if (result == 1)
                {
                    StatusBarManager.SetSavingFileMessage();

                    switch (data)
                    {
                        case ExperimentData:
                            using (var writer = new StreamWriter(dlg.Filename))
                            {
                                await WriteExperimentDataToFile(data as ExperimentData, writer);
                            }
                            break;
                        case AnalysisResult when dlg.Url.PathExtension == "ftitc":
                            using (var writer = new StreamWriter(dlg.Filename))
                            {
                                await WriteAnalysisResultToFile(data as AnalysisResult, writer);
                            }
                            break;
                        case AnalysisResult when dlg.Url.PathExtension == "csv": throw new NotImplementedException("CSV save not yet implemented");
                            break;
                    }

                    StatusBarManager.SetFileSaveSuccessfulMessage(dlg.Filename);
                }
            });
        }

        static async Task WriteFile(string path)
        {
            StatusBarManager.SetSavingFileMessage();

            using (var writer = new StreamWriter(path))
            {
                foreach (var data in DataManager.Data)
                {
                    await WriteExperimentDataToFile(data, writer);
                }
                foreach(var res in DataManager.Results)
                {
                    await WriteAnalysisResultToFile(res, writer);
                }
                //foreach (var data in DataManager.Data.Where(d => d.Solution != null))
                //{
                //    await WriteSolutionToFile(data.Solution, writer);
                //}
            }

            StatusBarManager.SetFileSaveSuccessfulMessage(path);
        }

        static async Task WriteExperimentDataToFile(ExperimentData data, StreamWriter stream)
        {
            var file = new List<string>();
            file.Add(FileHeader(ExperimentHeader, data.FileName));
            file.Add(Variable(ID, data.UniqueID));
            file.Add(Variable(Date, data.Date.ToString("O")));
            file.Add(Variable(SourceFormat, (int)data.DataSourceFormat));
            file.Add(Variable(Comments, data.Comments));
            file.Add(Variable(Include, data.Include));
            file.Add(Variable(SyringeConcentration, data.SyringeConcentration));
            file.Add(Variable(CellConcentration, data.CellConcentration));
            file.Add(Variable(StirringSpeed, data.StirringSpeed));
            file.Add(Variable(TargetTemperature, data.TargetTemperature));
            file.Add(Variable(MeasuredTemperature, data.MeasuredTemperature));
            file.Add(Variable(InitialDelay, data.InitialDelay));
            file.Add(Variable(TargetPowerDiff, data.TargetPowerDiff));
            file.Add(Variable(UseIntegrationFactorLength, (int)data.IntegrationLengthMode));
            file.Add(Variable(IntegrationLengthFactor, data.IntegrationLengthFactor));
            file.Add(Variable(FeedBackMode, (int)data.FeedBackMode));
            file.Add(Variable(CellVolume, data.CellVolume));
            file.Add(Variable(Instrument, (int)data.Instrument));

            if (data.Attributes.Count > 0)
            {
                file.Add(ListHeader(ExperimentAttributes));
                foreach (var att in data.Attributes) file.Add(Attribute(att));
                file.Add(EndListHeader);
            }

            if (data.Solution != null)
            {
                file.Add(Variable(SolutionGUID, data.Solution.Guid));
                file.Add(ObjectHeader(ExperimentSolutionHeader));
                file.AddRange(GetSolutionLines(data.Solution));
                file.Add(EndObjectHeader);
            }

            file.Add(ListHeader(InjectionList));
            foreach (var inj in data.Injections)
            {
                string injection = "";

                injection += inj.ID + ",";
                injection += (inj.Include ? 1 : 0) + ",";
                injection += inj.Time + ",";
                injection += inj.Volume + ",";
                injection += inj.Delay + ",";
                injection += inj.Duration + ",";
                injection += inj.Temperature + ",";
                injection += inj.IntegrationStartDelay + ",";
                injection += inj.IntegrationLength;

                file.Add(injection);
            }
            file.Add(EndListHeader);

            file.Add(ListHeader(DataPointList));
            foreach (var dp in data.DataPoints)
            {
                string line = "";
                line += dp.Time + ",";
                line += dp.Power + ",";
                line += dp.Temperature + ",";
                line += dp.ShieldT.ToString();

                file.Add(line);
            }
            file.Add(EndListHeader);

            if (data.Processor != null)
            {
                file.Add(ObjectHeader(Processor));
                file.Add(Variable(ProcessorType, (int)data.Processor.BaselineType));

                switch (data.Processor.BaselineType)
                {
                    case BaselineInterpolatorTypes.Polynomial:
                        file.Add(Variable(PolynomiumDegree, (data.Processor.Interpolator as PolynomialLeastSquaresInterpolator).Degree));
                        file.Add(Variable(PolynomiumLimit, (data.Processor.Interpolator as PolynomialLeastSquaresInterpolator).ZLimit));
                        break;
                    case BaselineInterpolatorTypes.Spline:
                        var spinterpolator = (data.Processor.Interpolator as SplineInterpolator);
                        file.Add(Variable(SplineAlgorithm, (int)spinterpolator.Algorithm));
                        file.Add(Variable(SplineHandleMode, (int)spinterpolator.HandleMode));
                        file.Add(Variable(SplineFraction, spinterpolator.FractionBaseline));
                        file.Add(Variable(SplineLocked, spinterpolator.IsLocked));
                        file.Add(ListHeader(SplinePointList));
                        foreach (var sp in spinterpolator.SplinePoints) file.Add(sp.Time + "," + sp.Power + "," + sp.ID + "," + sp.Slope);
                        file.Add(EndListHeader);
                        break;
                    default:
                    case BaselineInterpolatorTypes.ASL:
                    case BaselineInterpolatorTypes.None:
                        break;
                }

                file.Add(EndObjectHeader);
            }

            file.Add(EndFileHeader);
            foreach (var line in file) await stream.WriteLineAsync(line);
        }

        static List<string> GetSolutionLines(SolutionInterface solution)
        {
            var file = new List<string>();
            file.Add(FileHeader(SolutionHeader, solution.Guid));
            file.Add(Variable(DataRef, solution.Data.UniqueID));
            file.Add(Variable(SolModel, (int)solution.ModelType));
            if (solution.ErrorMethod != ErrorEstimationMethod.None) file.Add(Variable(SolErrorMethod, (int)solution.ErrorMethod));
            AddConvergenceLine(solution.Convergence, file);

            file.Add(ListHeader(SolParams));
            foreach (var par in solution.Model.Parameters.Table)
            {
                file.Add(Variable(par.Key.ToString() + ":" + ((int)par.Key).ToString(), par.Value.Value));
            }
            file.Add(EndListHeader);

            file.Add(ListHeader(MdlOptions));
            foreach (var opt in solution.Model.ModelOptions.ToList())
            {
                file.Add(Attribute(opt.Value));
            }
            file.Add(EndListHeader);

            if (solution.BootstrapSolutions.Count > 0)
            {
                file.Add(ListHeader(SolBootstrapSolutions));
                foreach (var bsol in solution.BootstrapSolutions)
                {
                    var lines = GetSolutionLines(bsol);

                    file.AddRange(lines);
                }
                file.Add(EndListHeader);
            }
            file.Add(EndFileHeader);

            return file;
        }

        private static void AddConvergenceLine(SolverConvergence convergence, List<string> file)
        {
            file.Add(ObjectHeader(SolConvergence));
            var conv = "";
            conv += Variable(SolIterations, convergence.Iterations) + ";";
            conv += Variable(SolConvMsg, convergence.Message) + ";";
            conv += Variable(SolConvTime, convergence.Time.TotalSeconds) + ";";
            conv += Variable(SolConvBootstrapTime, convergence.BootstrapTime.TotalSeconds) + ";";
            conv += Variable(SolLoss, convergence.Loss) + ";";
            conv += Variable(SolConvFailed, convergence.Failed) + ";";
            conv += Variable(SolConvAlgorithm, (int)convergence.Algorithm);
            file.Add(conv);
            file.Add(EndObjectHeader);
        }

        static async Task WriteSolutionToFile2(SolutionInterface solution, StreamWriter stream)
        {
            var file = GetSolutionLines(solution);
            foreach (var line in file) await stream.WriteLineAsync(line);
        }

        static async Task WriteGlobalSolutionToFile(GlobalSolution solution, StreamWriter stream)
        {
            var file = GetGlobalSolutionLines(solution);
            foreach (var line in file) await stream.WriteLineAsync(line);
        }

        static List<string> GetGlobalSolutionLines(GlobalSolution solution)
        {
            var file = new List<string>();
            file.Add(FileHeader(GlobalSolutionHeader, ""));
            file.Add(Variable(SolModel, (int)solution.Model.ModelType));

            //DataRefs
            file.Add(ListHeader(DataRef));
            foreach (var mdl in solution.Model.Models)
            {
                file.Add(mdl.Data.UniqueID);
            }
            file.Add(EndListHeader);

            //Parameter Constraints
            file.Add(ListHeader(SolConstraints));
            foreach (var par in solution.Model.Parameters.Constraints)
            {
                file.Add(Variable(par.Key.ToString() + ":" + ((int)par.Key).ToString(), (int)par.Value));
            }
            file.Add(EndListHeader);

            //Parameter Table
            file.Add(ListHeader(SolParams));
            foreach (var par in solution.Model.Parameters.GlobalTable)
            {
                file.Add(Variable(par.Key.ToString() + ":" + ((int)par.Key).ToString(), par.Value.Value));
            }
            file.Add(EndListHeader);

            //ModelCloneOptions
            file.Add(ObjectHeader(MdlCloneOptions));
            file.Add(Variable(SolErrorMethod, (int)solution.Model.ModelCloneOptions.ErrorEstimationMethod));
            file.Add(Variable(SolCloneConcentrationVariance, solution.Model.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap));
            file.Add(Variable(SolCloneAutoVariance, solution.Model.ModelCloneOptions.EnableAutoConcentrationVariance));
            file.Add(Variable(SolCloneAutoVarianceValue, solution.Model.ModelCloneOptions.AutoConcentrationVariance));     
            file.Add(EndObjectHeader);

            //Convergence
            AddConvergenceLine(solution.Convergence, file); 

            file.Add(ListHeader(SolutionList));
            foreach (var sol in solution.Solutions)
            {
                file.AddRange(GetSolutionLines(sol));
            }
            file.Add(EndListHeader);

            file.Add(EndFileHeader);

            return file;
        }

        static async Task WriteAnalysisResultToFile(AnalysisResult result, StreamWriter stream)
        {
            var file = new List<string>();
            file.Add(FileHeader(AnalysisResultHeader, result.UniqueID));
            file.AddRange(GetGlobalSolutionLines(result.Solution));
            file.Add(EndFileHeader);
            foreach (var line in file) await stream.WriteLineAsync(line);
        }
    }

    public class Exporter
    {
        static char Delimiter = ' ';
        static char BlankChar = ' ';
        static ExportAccessoryViewController.ExportAccessoryViewSettings ExportSettings;

        public static void Export(ExportType type)
        {
            ExportSettings = type == ExportType.Data ? ExportAccessoryViewController.ExportAccessoryViewSettings.DataDefault() : ExportAccessoryViewController.ExportAccessoryViewSettings.PeaksDefault();

            var storyboard = NSStoryboard.FromName("Main", null);
            var viewController = (ExportAccessoryViewController)storyboard.InstantiateControllerWithIdentifier("ExportAccessoryViewController");
            viewController.Setup(ExportSettings);

            var dlg = new NSSavePanel();
            dlg.Title = "Export";
            dlg.AllowedFileTypes = new string[] { "csv", "txt" };
            dlg.AccessoryView = viewController.View;
            dlg.NameFieldStringValue = "out";

            dlg.BeginSheet(NSApplication.SharedApplication.MainWindow, async (result) =>
            {
                if (result == 1)
                {
                    StatusBarManager.StartInderminateProgress();
                    StatusBarManager.SetStatusScrolling("Saving file: " + dlg.Filename);
                    SetDelimiter(dlg.Url);
                    switch (ExportSettings.Export)
                    {
                        case ExportType.Data: await WriteDataFile(dlg.Filename);break;
                        case ExportType.Peaks: await WritePeakFile(dlg.Filename); break;
                        case ExportType.ITCsim: await WriteITCsimFile(dlg.Filename, ExportColumns.SelectionITCsim); break;
                    }
                    
                }
            });
        }

        static void SetDelimiter(NSUrl url)
        {
            switch (url.PathExtension)
            {
                case "csv": Delimiter = ','; BlankChar = ' ';  break;
                case "txt": Delimiter = ' '; BlankChar = '.';  break;
            }
        }

        private static List<ExperimentData> GetData()
        {
            return AppSettings.ExportSelectionMode switch
            {
                ExportDataSelection.IncludedData => DataManager.Data.Where(d => d.Include).ToList(),
                ExportDataSelection.AllData => DataManager.Data,
                _ => new List<ExperimentData> { DataManager.Current },
            };
        }

        static async Task WriteDataFile(string path)
        {
            await Task.Run(async () =>
            {
                var lines = ExportSettings.UnifyTimeAxis ? GetUnifiedDataLines(ExportSettings.Data) : GetDataLines(ExportSettings.Data);

                using (var writer = new StreamWriter(path))
                {
                    foreach (var line in lines)
                    {
                        await writer.WriteLineAsync(line);
                    }
                }
            });

            StatusBarManager.SetStatus("Finished exporting data file", 3000);
            StatusBarManager.StopIndeterminateProgress();
        }

        static List<string> GetUnifiedDataLines(List<ExperimentData> data)
        {
            //Get time axis
            var mostcommonstep = FindMostCommonStep(data);
            var newvalues = new Dictionary<string, List<float>>();
            var xaxis = new List<float>();
            var min = data.Min(d => d.DataPoints.First().Time);
            var max = data.Max(d => d.DataPoints.Last().Time);

            //Add points to new x axis
            for (float i = min; i <= max; i += mostcommonstep) xaxis.Add(i); 

            //Implemented unified x axis
            foreach (var dat in data)
            {
                var dps = ExportSettings.ExportBaselineCorrectDataPoints ? dat.BaseLineCorrectedDataPoints : dat.DataPoints;
                var points = new List<float>();
                var prevtime = 0f;
                foreach (var t in xaxis)
                {
                    var group = dps.Where(dp => dp.Time > prevtime && dp.Time <= t);
                    float newdp;

                    if (group.Count() == 0) //Are we averaging a number of datapoints?
                    {
                        if (prevtime >= dps.Last().Time || t < dps.First().Time) newdp = float.NaN; //Outside data set?
                        else //Interpolate from datapoints
                        {
                            var p1 = dps.Where(dp => dp.Time > prevtime).First();
                            var p2 = dps.Where(dp => dp.Time < t).Last();

                            var weight = (t - p2.Time) / (p2.Time - p1.Time);

                            newdp = weight * p2.Power + (1 - weight) * p1.Power;
                        }
                    }
                    //Average datapoints in window. Probably not 100% accurate.
                    else newdp = dps.Where(dp => dp.Time > prevtime && dp.Time <= t).Select(dp => dp.Power).Average();

                    points.Add(newdp);

                    prevtime = t;
                }

                newvalues[dat.FileName] = points;
            }

            var lines = new List<string>();
            var header = "time" + Delimiter;
            foreach (var dat in newvalues) header += dat.Key + Delimiter;
            lines.Add(header.TrimEnd(Delimiter));

            for (int i = 0; i < xaxis.Count; i++)
            {
                var x = xaxis[i];
                var line = x.ToString("F1") + Delimiter;

                foreach (var dat in newvalues)
                {
                    var v = dat.Value[i];
                    if (float.IsNaN(v)) line += BlankChar + Delimiter;
                    else line += dat.Value[i].ToString() + Delimiter;
                }

                lines.Add(line.TrimEnd(Delimiter));
            }

            return lines;
        }

        static List<string> GetDataLines(List<ExperimentData> data)
        {
            var lines = new List<string>();
            var header = "";
            foreach (var dat in data) header += "time" + Delimiter + dat.FileName + Delimiter;
            lines.Add(header.TrimEnd(Delimiter));

            int index = 0;

            while (data.Any(d => d.DataPoints.Count > index))
            {
                string line = "";

                foreach (var d in data)
                {
                    var dps = ExportSettings.ExportBaselineCorrectDataPoints ? d.BaseLineCorrectedDataPoints : d.DataPoints;

                    if (dps.Count > index)
                    {
                        var dp = dps[index];
                        line += dp.Time.ToString() + Delimiter + dp.Power.ToString() + Delimiter;
                    }
                    else line += BlankChar.ToString() + Delimiter + BlankChar + Delimiter;
                }

                lines.Add(line.TrimEnd(Delimiter));

                index++;
            }

            return lines;
        }

        static float FindMostCommonStep(List<ExperimentData> data)
        {
            // Flatten the datasets into a single list of x-axis values
            List<float> allXValues = new List<float>();
            foreach (var dataset in data.Select(d => d.DataPoints))
            {
                allXValues.AddRange(dataset.Select(dp => dp.Time));
            }

            // Determine the most frequently used step
            var stepFrequencies = allXValues
                .Select((x, i) => i > 0 ? x - allXValues[i - 1] : 0)
                .GroupBy(step => step)
                .OrderByDescending(group => group.Count());
            float mostCommonStep = stepFrequencies.First().Key;

            return mostCommonStep;
        }

        static async Task WritePeakFile(string path)
        {
            await Task.Run(async () =>
            {
                foreach (var data in ExportSettings.Data)
                {
                    var dataname = Path.GetFileNameWithoutExtension(data.FileName);
                    var ext = Path.GetExtension(path);
                    var output = Path.GetDirectoryName(path) + "/" + Path.GetFileNameWithoutExtension(path) + "_" + dataname + ext;

                    var lines = GetColumns(data, ExportSettings.Columns);

                    using (var writer = new StreamWriter(output))
                    {
                        foreach (var line in lines)
                        {
                            await writer.WriteLineAsync(line);
                        }
                    }
                }
            });

            StatusBarManager.SetStatus("Finished exporting " + Utils.MarkdownStrings.ITCsimName, 3000);
            StatusBarManager.StopIndeterminateProgress();
        }

        static List<string> GetPeakLines(List<ExperimentData> data)
        {
            List<string> lines = new List<string>();

            string header = "";

            bool exportsolution = ExportSettings.ExportFittedPeaks;
            bool exportconcentration = ExportSettings.ExportConcentrations;

            foreach (var d in data)
            {
                header += "x" + Delimiter;

                if (ExportSettings.ExportConcentrations) header += "[cell]" + Delimiter + "[syringe]" + Delimiter + "n_injmass" + Delimiter + "V_inj" + Delimiter + "Included" + Delimiter;

                header += d.FileName + "_peak" + Delimiter;

                if (ExportSettings.ExportFittedPeaks) header += d.FileName + "_fit" + Delimiter;
            }

            lines.Add(header.TrimEnd(Delimiter));

            int index = 0;

            while (data.Any(d => d.Injections.Count > index))
            {
                string line = "";

                foreach (var d in data)
                {
                    if (d.Injections.Count > index)
                    {
                        var inj = d.Injections[index];
                        line += inj.Ratio.ToString() + Delimiter;

                        if (ExportSettings.ExportConcentrations)
                        {
                            line += inj.ActualCellConcentration.ToString() + Delimiter
                                + inj.ActualTitrantConcentration.ToString() + Delimiter
                                + inj.InjectionMass.ToString() + Delimiter
                                + inj.Volume.ToString() + Delimiter
                                + (inj.Include ? "1" : "0") + Delimiter;
                        }
                        
                        var enthalpy = ExportSettings.ExportOffsetCorrected ? inj.OffsetEnthalpy : inj.Enthalpy;
                        line += enthalpy.ToString() + Delimiter;

                        if (ExportSettings.ExportFittedPeaks)
                        {
                            if (d.Solution != null) line += d.Model.EvaluateEnthalpy(index, !ExportSettings.ExportOffsetCorrected).ToString() + Delimiter;
                            else line += BlankChar.ToString() + Delimiter;
                        }
                    }
                    else
                    {
                        line += BlankChar.ToString() + Delimiter + BlankChar + Delimiter;
                        if (ExportSettings.ExportConcentrations) line += BlankChar.ToString() + Delimiter + BlankChar.ToString() + Delimiter + BlankChar.ToString() + Delimiter + BlankChar.ToString() + Delimiter + BlankChar.ToString() + Delimiter;
                        if (ExportSettings.ExportFittedPeaks) line += BlankChar.ToString() + Delimiter;
                    }
                }

                lines.Add(line.TrimEnd(Delimiter));

                index++;
            }

            return lines;
        }

        static async Task WriteITCsimFile(string path, ExportColumns columns)
        {
            await Task.Run(async () =>
            {
                foreach (var data in ExportSettings.Data)
                {
                    var dataname = Path.GetFileNameWithoutExtension(data.FileName);
                    var ext = Path.GetExtension(path);
                    var output = Path.GetDirectoryName(path) + "/" + Path.GetFileNameWithoutExtension(path) + "_" + dataname + ext;

                    var lines = GetColumns(data, columns);

                    lines.AddRange(GetMetaData(data));

                    using (var writer = new StreamWriter(output))
                    {
                        foreach (var line in lines)
                        {
                            await writer.WriteLineAsync(line);
                        }
                    }
                }
            });

            StatusBarManager.SetStatus("Finished exporting " + Utils.MarkdownStrings.ITCsimName, 3000);
            StatusBarManager.StopIndeterminateProgress();
        }

        static List<string> GetColumns(ExperimentData data, ExportColumns columns)
        {
            var lines = new List<string>();

            // Build column header
            string header = "";

            for (int i = 1; i < 9999; i *= 2)
            {
                if (!Enum.IsDefined(typeof(ExportColumns), i)) { break; }
                if (header.Length > 0) header += Delimiter.ToString();

                header += ExportColumnHandler.GetColumnHeader((ExportColumns)i);
            }

            lines.Add(header);

            // Build file
            for (int j = 0; j < data.InjectionCount; j++)
            {
                var line = "";

                for (int i = 1; i < 9999; i *= 2)
                {
                    if (!Enum.IsDefined(typeof(ExportColumns), i)) { break; }
                    if (line.Length > 0) line += Delimiter.ToString();

                    line += ExportColumnHandler.GetColumnValue((ExportColumns)i, data, j);
                }

                lines.Add(line);
            }

            return lines;
        }

        static List<string> GetMetaData(ExperimentData data)
        {
            var lines = new List<string>
            {
                "# EXPINFO CELLCONC " + data.CellConcentration.Value.ToString("F9"),
                "# EXPINFO SYRINGECONC " + data.SyringeConcentration.Value.ToString("F9"),
                "# EXPINFO CELLVOLUME " + data.CellVolume.ToString("F9")
            };

            return lines;
        }

        public static void CopyToClipboard(AnalysisResult analysis, ConcentrationUnit kdunit, EnergyUnit eunit, bool usekelvin)
        {
            NSPasteboard.GeneralPasteboard.ClearContents();

            var solution = analysis.Solution;
            var delimiter = ',';
            var paste = Header() + Environment.NewLine;

            foreach (var data in solution.Solutions)
            {
                paste += data.Data.FileName + delimiter;
                paste += (usekelvin ? data.TempKelvin : data.Temp).ToString("F2") + delimiter;

                if (analysis.IsElectrostaticsAnalysisDependenceEnabled) paste += (1000 * BufferAttribute.GetIonicStrength(data.Data)).ToString("F2") + delimiter;
                if (analysis.IsProtonationAnalysisEnabled) paste += BufferAttribute.GetProtonationEnthalpy(data.Data).ToString(eunit, "F1", withunit: false) + delimiter;

                foreach (var par in data.ReportParameters)
                {
                    switch (par.Key)
                    {
                        case ParameterType.Nvalue1:
                        case ParameterType.Nvalue2: paste += par.Value.ToString("F3"); break;
                        case ParameterType.Affinity1:
                        case ParameterType.Affinity2: paste += par.Value.AsConcentration(kdunit, withunit: false); break;
                        default: paste += new Energy(par.Value).ToString(eunit, formatter: "G3", withunit: false); break;
                    }
                    paste += delimiter;
                }
                paste = paste.Trim() + Environment.NewLine;
            }

            paste = paste.Replace('±', delimiter);

            NSPasteboard.GeneralPasteboard.SetStringForType(paste, "NSStringPboardType");

            StatusBarManager.SetStatus("Results copied to clipboard", 3333);

            string Header()
            {
                string header = "exp" + delimiter + "temperature(C)" + delimiter;

                if (analysis.IsElectrostaticsAnalysisDependenceEnabled) header += "IS(mM)" + delimiter;
                if (analysis.IsProtonationAnalysisEnabled) header += "∆Hbufferprotonation(" + eunit.GetUnit() + ")" + delimiter;

                foreach (var par in solution.IndividualModelReportParameters)
                {
                    var s = ParameterTypeAttribute.TableHeader(par, solution.Solutions[0].ParametersConformingToKey(par).Count > 1, eunit, kdunit.GetName());

                    header += s + delimiter + s + "_SD" + delimiter;
                }

                return header.Substring(0, header.Length - 1);
            }
        }

        public enum ExportType
        {
            Data,
            Peaks,
            ITCsim
        }

        public enum ExportDataSelection
        {
            SelectedData,
            IncludedData,
            AllData
        }

        [Flags]
        public enum ExportColumns
        {
            None = 0,
            
            MolarRatio = 1 << 0,
            Included = 1 << 1,
            Peak = 1 << 2,
            Fit = 1 << 3,
            InjectionVolume = 1 << 4,
            InjectionDelay = 1 << 5,
            CellConc = 1 << 6,
            SyrConc = 1 << 7,

            Concentrations = CellConc | SyrConc,
            InjectionInfo = InjectionVolume | InjectionDelay,

            Default = MolarRatio | Included | Peak | Fit,
            SelectionMinimal = MolarRatio | Peak | Fit,
            SelectionITCsim = MolarRatio | Included | InjectionVolume | InjectionDelay | Peak | Fit,
        }

        private class ExportColumnHandler
        {
            public static string GetColumnHeader(ExportColumns column)
            {
                switch (column)
                {
                    case ExportColumns.MolarRatio: return "MolarRatio";
                    case ExportColumns.Included: return "Included";
                    case ExportColumns.Peak: return "PeakHeat";
                    case ExportColumns.Fit: return "FittedHeat";
                    case ExportColumns.InjectionVolume: return "InjVolume";
                    case ExportColumns.InjectionDelay: return "InjDelay";
                    case ExportColumns.CellConc: return "[cell]";
                    case ExportColumns.SyrConc: return "[syr]";
                    default: return "unknown_column_selection_" + column.ToString();
                }
            }

            public static string GetColumnValue(ExportColumns column, ExperimentData data, int i)
            {
                if (data == null) throw new Exception("No data selected");
                if (data.Injections == null) throw new Exception("Data does not contain injection information");
                if (data.Injections.Count < i) return "";

                var inj = data.Injections[i];

                switch (column)
                {
                    case ExportColumns.MolarRatio: return inj.Ratio.ToString("F5");
                    case ExportColumns.Included: return inj.Include ? "1" : "0";
                    case ExportColumns.InjectionVolume: return inj.Volume.ToString("E2");
                    case ExportColumns.InjectionDelay: return inj.Delay.ToString();
                    case ExportColumns.CellConc: return inj.ActualCellConcentration.ToString("F8");
                    case ExportColumns.SyrConc: return inj.ActualTitrantConcentration.ToString("F8");
                    case ExportColumns.Peak: return ExportSettings.ExportOffsetCorrected ? inj.OffsetEnthalpy.ToString("F3") : inj.Enthalpy.ToString("F3");
                    case ExportColumns.Fit:
                        if (data.Solution != null) return data.Model.EvaluateEnthalpy(i, !ExportSettings.ExportOffsetCorrected).ToString("F3");
                        else return BlankChar.ToString();
                    default: return BlankChar.ToString();
                }
            }
        }
    }
}
