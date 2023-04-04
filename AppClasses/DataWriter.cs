using System;
using AppKit;
using Foundation;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using AnalysisITC.AppClasses.Analysis2;
using AnalysisITC.AppClasses.Analysis2.Models;

namespace AnalysisITC
{
    public class FTITCFormat
    {
        public const string ExperimentHeader = "Experiment";
        public const string ID = "ID";
        public const string FileName = "FileName";
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
        public const string Solution = "fit";

        public const string SolutionHeader = "SolutionFile";
        public const string SolModel = "MDL";
        public const string SolParamsRaw = "RawParameters";
        public const string SolLoss = "Loss";
        public const string SolH = "H";
        public const string SolK = "S";
        public const string SolN = "N";
        public const string SolO = "O";
        public const string SolBootN = "BootstrapIterations";

        public const string GlobalSolutionHeader = "GlobalSolutionFile";
        public const string SolutionList = "SolutionList";

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
                    StatusBarManager.SetStatusScrolling("Saving file: " + dlg.Filename);
                    await WriteFile(dlg.Filename);

                    CurrentAccessedAppDocumentPath = dlg.Filename;
                    AppSettings.LastDocumentUrl = dlg.Url;
                }
            });
        }

        public static async void SaveWithPath()
        {
            StatusBarManager.SetStatusScrolling("Saving file: " + CurrentAccessedAppDocumentPath);
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
                    StatusBarManager.SetStatusScrolling("Saving file: " + dlg.Filename);
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
                }
            });
        }

        static async Task WriteFile(string path)
        {
            using (var writer = new StreamWriter(path))
            {
                foreach (var data in DataManager.Data)
                {
                    await WriteExperimentDataToFile(data, writer);
                }
                //foreach (var data in DataManager.Data.Where(d => d.Solution != null))
                //{
                //    await WriteSolutionToFile(data.Solution, writer);
                //}
            }
        }

        static async Task WriteExperimentDataToFile(ExperimentData data, StreamWriter stream)
        {
            var file = new List<string>();
            file.Add(FileHeader(ExperimentHeader, data.FileName));
            file.Add(Variable(ID, data.UniqueID));
            file.Add(Variable(Date, data.Date.ToString("O")));
            file.Add(Variable(SourceFormat, (int)data.DataSourceFormat));
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
            if (data.Solution != null) file.Add(Variable(Solution, data.Solution.Guid));

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

        static async Task WriteSolutionToFile2(SolutionInterface solution, StreamWriter stream)
        {
            var file = new List<string>();
            file.Add(FileHeader(SolutionHeader, solution.Guid));
            file.Add(Variable(SolModel, solution.Model.ToString()));
            file.Add(Variable(SolLoss, solution.Loss));
            foreach (var par in solution.Parameters)
            {
                file.Add(Variable(par.Key.ToString(), par.Value));
            }
            file.Add(Variable(SolBootN, solution.BootstrapSolutions.Count));

            file.Add(EndFileHeader);
            foreach (var line in file) await stream.WriteLineAsync(line);
        }

        static async Task WriteGlobalSolutionToFile(GlobalSolution solution, StreamWriter stream)
        {
            var file = new List<string>();
            file.Add(FileHeader(GlobalSolutionHeader, ""));


            file.Add(ListHeader(SolutionList));
            foreach (var sol in solution.Solutions)
            {
                file.Add(sol.Guid);
            }
            file.Add(EndListHeader);

            file.Add(EndFileHeader);
            foreach (var line in file) await stream.WriteLineAsync(line);
        }

        static async Task WriteAnalysisResultToFile(AnalysisResult result, StreamWriter writer)
        {
            throw new NotImplementedException("Analysis Result save not yet implemented");
        }

        public static void CopyToClipboard(GlobalSolution solution, double kdmagnitude, EnergyUnit unit, bool usekelvin)
        {
            NSPasteboard.GeneralPasteboard.ClearContents();

            string paste = "";

            foreach (var data in solution.Solutions)
            {
                paste += (usekelvin ? data.TempKelvin : data.Temp).ToString("F2") + " ";
                foreach (var par in data.ReportParameters)
                {
                    switch (par.Key)
                    {
                        case ParameterTypes.Nvalue1:
                        case ParameterTypes.Nvalue2: paste += par.Value.ToString("F2"); break;
                        case ParameterTypes.Affinity1:
                        case ParameterTypes.Affinity2: paste += par.Value.AsDissociationConstant(kdmagnitude, withunit: false); break;
                        default: paste += new Energy(par.Value).ToString(unit, withunit: false); break;
                    }
                    paste += " ";
                }
                paste = paste.Trim() + Environment.NewLine;
            }

            paste = paste.Replace('±', ' ');

            NSPasteboard.GeneralPasteboard.SetStringForType(paste, "NSStringPboardType");

            StatusBarManager.SetStatus("Results copied to clipboard", 3333);
        }
    }

    public class Exporter
    {
        static char Delimiter = ' ';
        static char BlankChar = ' ';
        static ExportAccessoryViewController.ExportAccessoryViewSettings Settings;

        public static void Export(ExportType type)
        {
            Settings = type == ExportType.Data ? ExportAccessoryViewController.ExportAccessoryViewSettings.DataDefault() : ExportAccessoryViewController.ExportAccessoryViewSettings.PeaksDefault();

            var storyboard = NSStoryboard.FromName("Main", null);
            var viewController = (ExportAccessoryViewController)storyboard.InstantiateControllerWithIdentifier("ExportAccessoryViewController");
            viewController.Setup(Settings);

            var dlg = new NSSavePanel();
            dlg.Title = "Export";
            dlg.AllowedFileTypes = new string[] { "csv", "txt" };
            dlg.AccessoryView = viewController.View;

            dlg.BeginSheet(NSApplication.SharedApplication.MainWindow, async (result) =>
            {
                if (result == 1)
                {
                    StatusBarManager.StartInderminateProgress();
                    StatusBarManager.SetStatusScrolling("Saving file: " + dlg.Filename);
                    SetDelimiter(dlg.Url);
                    switch (Settings.Export)
                    {
                        case ExportType.Data: await WriteDataFile(dlg.Filename);break;
                        case ExportType.Peaks: await WritePeakFile(dlg.Filename); break;
                    }
                    
                }
            });
        }

        //public static void ExportData()
        //{
        //    List<ExperimentData> data = GetData();

        //    Settings = ExportAccessoryViewController.ExportAccessoryViewSettings.DataDefault(data);

        //    var storyboard = NSStoryboard.FromName("Main", null);
        //    var viewController = (ExportAccessoryViewController)storyboard.InstantiateControllerWithIdentifier("ExportAccessoryViewController");
        //    viewController.Setup(Settings);

        //    var dlg = new NSSavePanel();
        //    dlg.Title = "Export Data";
        //    dlg.AllowedFileTypes = new string[] { "csv", "txt" };
        //    dlg.AccessoryView = viewController.View;

        //    dlg.BeginSheet(NSApplication.SharedApplication.MainWindow, async (result) =>
        //    {
        //        if (result == 1)
        //        {
        //            StatusBarManager.StartInderminateProgress();
        //            StatusBarManager.SetStatusScrolling("Saving file: " + dlg.Filename);
        //            SetDelimiter(dlg.Url);
        //            await WriteDataFile(dlg.Filename, data, ExportAccessoryViewController.ExportBaselineCorrectDataPoints);
        //        }
        //    });
        //}

        static void SetDelimiter(NSUrl url)
        {
            switch (url.PathExtension)
            {
                case "csv": Delimiter = ','; BlankChar = ' ';  break;
                case "txt": Delimiter = ' '; BlankChar = '.';  break;
            }
        }

        //public static void ExportPeaks()
        //{
        //    List<ExperimentData> data = GetData();

        //    var dlg = new NSSavePanel();
        //    dlg.Title = "Export Peaks";
        //    dlg.AllowedFileTypes = new string[] { "csv", "txt" };

        //    dlg.BeginSheet(NSApplication.SharedApplication.MainWindow, async (result) =>
        //    {
        //        if (result == 1)
        //        {
        //            StatusBarManager.StartInderminateProgress();
        //            StatusBarManager.SetStatusScrolling("Saving file: " + dlg.Filename);
        //            SetDelimiter(dlg.Url);
        //            await WritePeakFile(dlg.Filename, data);
        //        }
        //    });
        //}

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
                var lines = Settings.UnifyTimeAxis ? GetUnifiedDataLines(Settings.Data) : GetDataLines(Settings.Data);

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
                var dps = Settings.ExportBaselineCorrectDataPoints ? dat.BaseLineCorrectedDataPoints : dat.DataPoints;
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
                    var dps = Settings.BaselineCorrectionEnabled ? d.BaseLineCorrectedDataPoints : d.DataPoints;

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
                var lines = GetPeakLines(Settings.Data);

                using (var writer = new StreamWriter(path))
                {
                    foreach (var line in lines)
                    {
                        await writer.WriteLineAsync(line);
                    }
                }
            });

            StatusBarManager.SetStatus("Finished exporting peaks", 3000);
            StatusBarManager.StopIndeterminateProgress();
        }

        static List<string> GetPeakLines(List<ExperimentData> data)
        {
            List<string> lines = new List<string>();

            string header = "";

            bool exportsolution = AppSettings.ExportFitPointsWithPeaks;

            foreach (var d in data)
            {
                header += "x" + Delimiter + d.FileName + "_peak" + Delimiter;
                if (exportsolution) header += d.FileName + "_fit" + Delimiter;
            }

            lines.Add(header);

            int index = 0;

            while (data.Any(d => d.Injections.Count > index))
            {
                string line = "";

                foreach (var d in data)
                {
                    if (d.Injections.Count > index)
                    {
                        var inj = d.Injections[index];
                        line += inj.Ratio.ToString() + Delimiter + inj.Enthalpy.ToString() + Delimiter;

                        if (exportsolution)
                        {
                            if (d.Solution != null) line += d.Model.EvaluateEnthalpy(index, false).ToString() + Delimiter;
                            else line += BlankChar.ToString() + Delimiter;
                        }
                    }
                    else
                    {
                        line += BlankChar.ToString() + Delimiter + BlankChar + Delimiter;

                        if (exportsolution) line += BlankChar.ToString() + Delimiter;
                    }
                }

                lines.Add(line.TrimEnd(Delimiter));

                index++;
            }

            return lines;
        }

        public enum ExportType
        {
            Data,
            Peaks
        }

        public enum ExportDataSelection
        {
            SelectedData,
            IncludedData,
            AllData
        }
    }
}
