using System;
using AppKit;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Globalization;
using AnalysisITC.AppClasses.AnalysisClasses.Models;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC
{
    public class FTITCFormat
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        const NumberStyles NumericStyle = NumberStyles.Float | NumberStyles.AllowThousands;
        static string currentAccessedAppDocumentPath = "";

        public const string FTITCVersion = "FTITCVersion";

        public const string ExperimentHeader = "Experiment";
        public const string TandemExperimentHeader = "TandemExperiment";
        public const string ID = "ID";
        public const string AssignedName = "Name";
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
        public const string SegmentList = "SegmentList";
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
        public const string SolBootstrapParameters = "BootParameters";
        public const string SolConvergence = "Conv";
        public const string SolIterations = "Iter";
        public const string SolConvMsg = "MSG";
        public const string SolConvTime = "TIME";
        public const string SolConvBootstrapTime = "BTIME";
        public const string SolConvFailed = "Failed";
        public const string SolConvAlgorithm = "Algorithm";
        public const string SolConvergenceSnapshot = "ConvSnapshot";
        public const string SolConvSchemaVersion = "SchemaVersion";
        public const string SolConvTermination = "Termination";
        public const string SolConvErrorOutcome = "ErrorOutcome";
        public const string SolConvFailureReason = "FailureReason";
        public const string SolConvErrorSummary = "ErrorSummary";
        public const string SolWeightedError = "InjErrorWeighted";
        public const string MdlCloneOptions = "MdlClOpts";
        public const string MdlOptions = "MdlOpts";
        public const string SolParent = "Parent";

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

        public static string FileHeader(string header, string filename) => "FILE:" + header + ":" + EncodeText(filename);
        public static string FileHeader(string header, string[] args) => "FILE:" + header + ":" + string.Join(',', args.Select(EncodeText));
        public static string ObjectHeader(string header) => "OBJECT:" + header; 
        public static string Variable(string header, string value) => header + ":" + value;
        public static string Variable(string header, double value) => Variable(header, FormatDouble(value));
        public static string Variable(string header, bool value) => Variable(header, value ? "1" : "0");
        public static string Variable(string header, FloatWithError value) => Variable(header, value.ToSaveString());
        public static string Variable(string header, Energy value) => Variable(header, value.FloatWithError);
        public static string ListHeader(string header) => "LIST:" + header;
        public static string Attribute(ExperimentAttribute opt)
        {
            string str = "";

            str += opt.Key.ToString() + ";"
                + (int)opt.Key + ";"
                + Variable("B", opt.BoolValue) + ";"
                + Variable("I", opt.IntValue) + ";"
                + Variable("D", opt.DoubleValue) + ";"
                + Variable("S", EncodeText(opt.StringValue)) + ";"
                + Variable("FWE", opt.ParameterValue) + ";"
                + Variable("name", EncodeText(opt.OptionName));

            return str;
        }

        public static string FormatDouble(double value) => value.ToString("R", Invariant);
        public static string FormatFloat(float value) => value.ToString("R", Invariant);
        public static string FormatInt(int value) => value.ToString(Invariant);
        public static string[] SplitKeyValue(string line) => (line ?? string.Empty).Split(new[] { ':' }, 2);
        public static string[] SplitCsv(string line) => (line ?? string.Empty).Split(',');

        public static double DParse(string value)
        {
            if (double.TryParse(value, NumericStyle, Invariant, out var result)) return result;
            return double.Parse(value, NumericStyle, CultureInfo.CurrentCulture);
        }

        public static float FParse(string value)
        {
            if (float.TryParse(value, NumericStyle, Invariant, out var result)) return result;
            return float.Parse(value, NumericStyle, CultureInfo.CurrentCulture);
        }

        public static int IParse(string value) => int.Parse(value, NumberStyles.Integer, Invariant);
        public static bool BParse(string value) => value == "1";
        public static Energy EParse(string value) => new Energy(FWEParse(value));
        public static string EncodeText(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }
        public static string DecodeText(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return value;
            }
        }
        public static FloatWithError FWEParse(string value)
        {
            if (value.Contains(';')) return FloatWithError.FromSaveString(value); // New save version

            var s = value.Split(",");

            if (s.Length > 1) return new FloatWithError(DParse(s[0]), DParse(s[1]));
            else return new FloatWithError(DParse(s[0]));
        }
        public static TimeSpan TSParse(string value) => TimeSpan.FromSeconds(DParse(value));
        public static DateTime DTParse(string value)
        {
            if (DateTime.TryParseExact(value, "O", Invariant, DateTimeStyles.RoundtripKind, out var exact)) return exact;
            if (DateTime.TryParse(value, Invariant, DateTimeStyles.RoundtripKind, out var parsed)) return parsed;
            return DateTime.Parse(value, CultureInfo.CurrentCulture);
        }

        public static event EventHandler CurrentAccessedAppDocumentPathChanged;

        public static string CurrentAccessedAppDocumentPath
        {
            get => currentAccessedAppDocumentPath;
            set
            {
                var next = value ?? "";
                if (currentAccessedAppDocumentPath == next) return;

                currentAccessedAppDocumentPath = next;
                CurrentAccessedAppDocumentPathChanged?.Invoke(null, EventArgs.Empty);
            }
        }
    }

    public class FTITCWriter : FTITCFormat
    {
        public static bool IsSaved => !string.IsNullOrEmpty(CurrentAccessedAppDocumentPath);

        public static void SaveState2()
        {
            _ = SaveState2Async();
        }

        public static async Task<bool> SaveState2Async()
        {
            var dlg = new NSSavePanel();
            dlg.Title = "Save FT-ITC File";
            dlg.AllowedFileTypes = new string[] { "ftitc" };
            var tcs = new TaskCompletionSource<bool>();

            dlg.BeginSheet(NSApplication.SharedApplication.MainWindow, async (result) =>
            {
                try
                {
                    if (result != 1 || string.IsNullOrWhiteSpace(dlg.Filename))
                    {
                        tcs.TrySetResult(false);
                        return;
                    }

                    await WriteFile(dlg.Filename);

                    CurrentAccessedAppDocumentPath = dlg.Filename;
                    AppSettings.LastDocumentUrl = dlg.Url;
                    DocumentDirtyTracker.MarkClean();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    AppEventHandler.DisplayHandledException(ex);
                    tcs.TrySetResult(false);
                }
            });

            return await tcs.Task;
        }

        public static void SaveWithPath()
        {
            _ = SaveWithPathAsync();
        }

        public static async Task<bool> SaveWithPathAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentAccessedAppDocumentPath))
            {
                return false;
            }

            try
            {
                await WriteFile(CurrentAccessedAppDocumentPath);
                DocumentDirtyTracker.MarkClean();
                return true;
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
                return false;
            }
        }

        static StreamWriter GetFTITCStreamWriter(string path)
        {
            var sw = new StreamWriter(path);

            sw.WriteLine(Variable(FTITCVersion, AppVersion.FullVersionString));

            return sw;
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
                            using (var writer = GetFTITCStreamWriter(dlg.Filename))
                            {
                                await WriteExperimentDataToFile(data as ExperimentData, writer);
                            }
                            break;
                        case AnalysisResult when dlg.Url.PathExtension == "ftitc":
                            using (var writer = GetFTITCStreamWriter(dlg.Filename))
                            {
                                await WriteAnalysisResultToFile(data as AnalysisResult, writer);
                            }
                            break;
                        case AnalysisResult when dlg.Url.PathExtension == "csv":
                            Exporter.Export(ExportType.CSV);
                            break;
                    }

                    StatusBarManager.SetFileSaveSuccessfulMessage(dlg.Filename);
                }
            });
        }

        static async Task WriteFile(string path)
        {
            StatusBarManager.SetSavingFileMessage();

            using (var writer = GetFTITCStreamWriter(path))
            {
                foreach (var data in DataManager.Data)
                {
                    await WriteExperimentDataToFile(data, writer);
                }
                foreach(var res in DataManager.Results)
                {
                    await WriteAnalysisResultToFile(res, writer);
                }
            }

            StatusBarManager.SetFileSaveSuccessfulMessage(path);
        }

        static async Task WriteExperimentDataToFile(ExperimentData data, StreamWriter stream)
        {
            var file = new List<string>
            {
                FileHeader(ExperimentHeader, data.FileName),
                Variable(AssignedName, EncodeText(data.Name)),
                Variable(ID, data.UniqueID),
                Variable(Date, data.Date.ToString("O", CultureInfo.InvariantCulture)),
                Variable(SourceFormat, (int)data.DataSourceFormat),
                Variable(Comments, EncodeText(data.Comments)),
                Variable(Include, data.Include),
                Variable(SyringeConcentration, data.SyringeConcentration),
                Variable(CellConcentration, data.CellConcentration),
                Variable(StirringSpeed, data.StirringSpeed),
                Variable(TargetTemperature, data.TargetTemperature),
                Variable(MeasuredTemperature, data.MeasuredTemperature),
                Variable(InitialDelay, data.InitialDelay),
                Variable(TargetPowerDiff, data.TargetPowerDiff),
                //Variable(UseIntegrationFactorLength, (int)data.IntegrationLengthMode),
                //Variable(IntegrationLengthFactor, data.IntegrationLengthFactor),
                Variable(FeedBackMode, (int)data.FeedBackMode),
                Variable(CellVolume, data.CellVolume),
                Variable(Instrument, (int)data.Instrument)
            };

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
                file.Add(string.Join(",",
                    FormatInt(inj.ID),
                    inj.Include ? "1" : "0",
                    FormatFloat(inj.Time),
                    FormatDouble(inj.Volume),
                    FormatFloat(inj.Delay),
                    FormatFloat(inj.Duration),
                    FormatDouble(inj.Temperature),
                    FormatFloat(inj.IntegrationStartDelay),
                    FormatFloat(inj.IntegrationEndOffset),
                    FormatDouble(inj.ActualCellConcentration),
                    FormatDouble(inj.ActualTitrantConcentration),
                    FormatDouble(inj.RawPeakArea.Value),
                    FormatDouble(inj.RawPeakArea.SD)));
            }
            file.Add(EndListHeader);
            if (data.Segments != null)
            {
                file.Add(ListHeader(SegmentList));
                foreach (var seg in data.Segments)
                {
                    file.Add(string.Join(",",
                        FormatInt(seg.FirstInjectionID),
                        FormatDouble(seg.SegmentInitialActiveCellConc),
                        FormatDouble(seg.SegmentInitialActiveTitrantConc)));
                }
            }
            file.Add(EndListHeader);

            file.Add(ListHeader(DataPointList));
            foreach (var dp in data.DataPoints)
            {
                file.Add(string.Join(",",
                    FormatFloat(dp.Time),
                    FormatFloat(dp.Power),
                    FormatFloat(dp.Temperature),
                    FormatFloat(dp.ShieldT)));
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
                        foreach (var sp in spinterpolator.SplinePoints)
                        {
                            file.Add(string.Join(",",
                                FormatDouble(sp.Time),
                                FormatDouble(sp.Power),
                                FormatInt(sp.ID),
                                FormatDouble(sp.Slope)));
                        }
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
            file.Add(Variable(SolWeightedError, solution.UseWeightedFitting));
            if (solution.ParentSolution != null) file.Add(Variable(SolParent, solution.ParentSolution.UniqueID));
            if (solution.ErrorMethod != ErrorEstimationMethod.None) file.Add(Variable(SolErrorMethod, (int)solution.ErrorMethod));
            //AddConvergenceLine(solution.Convergence, file);
            AddConvergenceSnapshot(solution.Convergence, file);

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
                file.Add(ListHeader(SolBootstrapParameters));

                //file.Add(ListHeader(SolBootstrapSolutions));
                foreach (var bsol in solution.BootstrapSolutions)
                {
                    var lines = GetBootstrapSolutionLines(bsol);

                    file.AddRange(lines);
                }
                file.Add(EndListHeader);
            }

            file.Add(EndFileHeader);

            return file;
        }

        private static List<string> GetBootstrapSolutionLines(SolutionInterface solution)
        {
            var lines = new List<string>();
            foreach (var par in solution.Model.Parameters.Table)
            {
                lines.Add(Variable(par.Key.ToString() + ":" + ((int)par.Key).ToString(), par.Value.Value));
            }
            lines.Add(EndListHeader);
            return lines;
        }

        private static void AddConvergenceLine(SolverConvergence convergence, List<string> file)
        {
            if (convergence == null) return;

            file.Add(ObjectHeader(SolConvergence));
            var conv = "";
            conv += Variable(SolIterations, convergence.Iterations) + ";";
            conv += Variable(SolConvMsg, EncodeText(convergence.Message)) + ";";
            conv += Variable(SolConvTime, convergence.Time.TotalSeconds) + ";";
            conv += Variable(SolConvBootstrapTime, convergence.ErrorEstimationTime.TotalSeconds) + ";";
            conv += Variable(SolLoss, convergence.Loss) + ";";
            conv += Variable(SolConvFailed, convergence.Failed) + ";";
            conv += Variable(SolConvAlgorithm, (int)convergence.Algorithm);
            file.Add(conv);
            file.Add(EndObjectHeader);
        }

        private static void AddConvergenceSnapshot(SolverConvergence convergence, List<string> file)
        {
            if (convergence == null) return;

            var snapshot = convergence.ToSnapshot();

            file.Add(ObjectHeader(SolConvergenceSnapshot));
            file.Add(Variable(SolConvSchemaVersion, snapshot.SchemaVersion));
            file.Add(Variable(SolIterations, snapshot.Iterations));
            file.Add(Variable(SolLoss, snapshot.Loss));
            file.Add(Variable(SolConvTime, snapshot.TimeSeconds));
            file.Add(Variable(SolConvBootstrapTime, snapshot.ErrorEstimationTimeSeconds));
            file.Add(Variable(SolConvAlgorithm, (int)snapshot.Algorithm));
            file.Add(Variable(SolConvTermination, (int)snapshot.Termination));
            file.Add(Variable(SolConvErrorOutcome, (int)snapshot.ErrorEstimationOutcome));
            file.Add(Variable(SolConvFailureReason, EncodeText(snapshot.FailureReason)));
            file.Add(Variable(SolConvErrorSummary, EncodeText(snapshot.ErrorEstimationSummary)));
            file.Add(EndObjectHeader);
        }

        static async Task WriteAnalysisResultToFile(AnalysisResult result, StreamWriter stream)
        {
            var file = new List<string>()
            {
                FileHeader(AnalysisResultHeader, new[] { result.UniqueID, result.FileName }),
                Variable(Comments, EncodeText(result.Comments)),
                Variable(Date, result.Date.ToString("O", CultureInfo.InvariantCulture)),
                Variable(AssignedName, EncodeText(result.Name)),
            };
            file.AddRange(GetGlobalSolutionLines(result.Solution));
            file.Add(EndFileHeader);
            foreach (var line in file) await stream.WriteLineAsync(line);
        }

        static List<string> GetGlobalSolutionLines(GlobalSolution solution)
        {
            var file = new List<string>();
            file.Add(FileHeader(GlobalSolutionHeader, ""));
            file.Add(Variable(SolModel, (int)solution.Model.ModelType));
            file.Add(Variable(SolWeightedError, solution.UseWeightedFitting));

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
            //AddConvergenceLine(solution.Convergence, file);
            AddConvergenceSnapshot(solution.Convergence, file);

            file.Add(ListHeader(SolutionList));
            foreach (var sol in solution.Solutions)
            {
                file.AddRange(GetSolutionLines(sol));
            }
            file.Add(EndListHeader);

            file.Add(EndFileHeader);

            return file;
        }
    }
}
