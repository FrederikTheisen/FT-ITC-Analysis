using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using AnalysisITC.Platform;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Analysis;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Export
{
    public class FTITCFormat
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        const NumberStyles NumericStyle = NumberStyles.Float | NumberStyles.AllowThousands;
        const string TextPrefix = "text:";
        const string EncodedTextPrefix = "b64:";
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        static readonly Regex Base64TextPattern = new Regex(@"^[A-Za-z0-9+/]*={0,2}$", RegexOptions.Compiled);
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
        public const string SplineShowHandles = "SShowHandles";
        public const string SplineAllowPointTimeDragging = "SAllowPointTimeDragging";
        public const string SplinePointDensity = "SPointDensity";
        public const string SplineLocked = "SLocked";
        public const string SplinePointsPerInjection = "SPointsPerInjection";
        public const string PolynomiumDegree = "PDegree";
        public const string PolynomiumLimit = "PLimit";
        public const string SegmentedBaselineDegree = "SegDegree";
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
        public const string SolCloneIsGlobal = "IsGlobalClone";
        public const string SolCloneUnlockParameters = "UnlockBootParams";
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
        public const string AnalysisResultValiditySnapshotData = "AnalysisResultValiditySnapshot";

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
        public static string FileHeader(string header, string[] args) => "FILE:" + header + ":" + string.Join(",", args.Select(EncodeText));
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

            if (!NeedsTextMarker(value)) return value;

            return TextPrefix + EscapeText(value);
        }
        public static string DecodeText(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";

            if (value.StartsWith(TextPrefix, StringComparison.Ordinal))
                return UnescapeText(value.Substring(TextPrefix.Length));

            if (value.StartsWith(EncodedTextPrefix, StringComparison.Ordinal))
                return DecodeBase64Text(value.Substring(EncodedTextPrefix.Length), value);

            // Backward compatibility for files written before encoded text had an explicit marker.
            // Legacy project files stored text directly, so only accept unmarked Base64 when it
            // decodes to printable UTF-8. This prevents plain names like "test" or "AAAA" from
            // turning into garbage-looking strings when old projects are loaded.
            if (!LooksLikeBase64Text(value)) return value;

            var decoded = DecodeBase64Text(value, null);
            if (decoded == null || !IsPrintableText(decoded)) return value;

            return decoded;
        }

        static bool NeedsTextMarker(string value)
        {
            if (value.StartsWith(TextPrefix, StringComparison.Ordinal)) return true;
            if (value.StartsWith(EncodedTextPrefix, StringComparison.Ordinal)) return true;
            if (LooksLikeBase64Text(value)) return true;

            foreach (var c in value)
            {
                if (NeedsEscaping(c)) return true;
            }

            return false;
        }

        static string EscapeText(string value)
        {
            var text = new StringBuilder(value.Length);

            foreach (var c in value)
            {
                switch (c)
                {
                    case '%':
                        text.Append("%25");
                        break;
                    case ',':
                        text.Append("%2C");
                        break;
                    case ';':
                        text.Append("%3B");
                        break;
                    case '\r':
                        text.Append("%0D");
                        break;
                    case '\n':
                        text.Append("%0A");
                        break;
                    case '\t':
                        text.Append("%09");
                        break;
                    default:
                        if (char.IsControl(c)) text.Append("%u").Append(((int)c).ToString("X4", Invariant));
                        else text.Append(c);
                        break;
                }
            }

            return text.ToString();
        }

        static string UnescapeText(string value)
        {
            var text = new StringBuilder(value.Length);

            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '%' && TryReadEscapedChar(value, i, out var c, out var consumed))
                {
                    text.Append(c);
                    i += consumed - 1;
                    continue;
                }

                text.Append(value[i]);
            }

            return text.ToString();
        }

        static bool TryReadEscapedChar(string value, int index, out char c, out int consumed)
        {
            c = default;
            consumed = 0;

            if (index + 2 < value.Length && IsHex(value[index + 1]) && IsHex(value[index + 2]))
            {
                c = (char)Convert.ToInt32(value.Substring(index + 1, 2), 16);
                consumed = 3;
                return true;
            }

            if (index + 5 < value.Length
                && value[index + 1] == 'u'
                && IsHex(value[index + 2])
                && IsHex(value[index + 3])
                && IsHex(value[index + 4])
                && IsHex(value[index + 5]))
            {
                c = (char)Convert.ToInt32(value.Substring(index + 2, 4), 16);
                consumed = 6;
                return true;
            }

            return false;
        }

        static bool NeedsEscaping(char c)
        {
            return c == ','
                || c == ';'
                || c == '\r'
                || c == '\n'
                || c == '\t'
                || char.IsControl(c);
        }

        static bool IsHex(char c)
        {
            return c >= '0' && c <= '9'
                || c >= 'a' && c <= 'f'
                || c >= 'A' && c <= 'F';
        }

        static string DecodeBase64Text(string value, string fallback)
        {
            try
            {
                return StrictUtf8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return fallback;
            }
        }

        static bool LooksLikeBase64Text(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (value.Length % 4 != 0) return false;

            return Base64TextPattern.IsMatch(value);
        }

        static bool IsPrintableText(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;

            foreach (var c in value)
            {
                if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
                    return false;
            }

            return true;
        }
        public static FloatWithError FWEParse(string value)
        {
            if (value.Contains(';')) return FloatWithError.FromSaveString(value); // New save version

            var s = value.Split(',');

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
            var path = await PlatformServices.FileSavePromptService.ChooseSaveFilePathAsync("Save FT-ITC File", new[] { "ftitc" });
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                await WriteFile(path);

                CurrentAccessedAppDocumentPath = path;
                AppSettings.LastDocumentPath = path;
                DocumentDirtyTracker.MarkClean();
                return true;
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
                return false;
            }
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
            _ = SaveSelectedAsync(data);
        }

        public static async Task<bool> SaveSelectedAsync(ITCDataContainer data)
        {
            var title = "Save FT-ITC " + (data is ExperimentData ? "Experiment Data" : "Analysis Results");
            var allowedFileTypes = data is ExperimentData ? new[] { "ftitc" } : new[] { "ftitc", "csv" };
            var path = await PlatformServices.FileSavePromptService.ChooseSaveFilePathAsync(title, allowedFileTypes);
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                StatusBarManager.SetSavingFileMessage();

                switch (data)
                {
                    case ExperimentData:
                        using (var writer = GetFTITCStreamWriter(path))
                        {
                            await WriteExperimentDataToFile(data as ExperimentData, writer);
                        }
                        break;
                    case AnalysisResult when Path.GetExtension(path).TrimStart('.').ToLowerInvariant() == "ftitc":
                        using (var writer = GetFTITCStreamWriter(path))
                        {
                            await WriteAnalysisResultToFile(data as AnalysisResult, writer);
                        }
                        break;
                    case AnalysisResult when Path.GetExtension(path).TrimStart('.').ToLowerInvariant() == "csv":
                        Exporter.Export(ExportType.CSV);
                        break;
                }

                StatusBarManager.SetFileSaveSuccessfulMessage(path);
                return true;
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
                return false;
            }
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
                    case BaselineInterpolatorTypes.Segmented:
                        file.Add(Variable(SegmentedBaselineDegree, (data.Processor.Interpolator as SegmentedBaselineInterpolator).Degree));
                        break;
                    case BaselineInterpolatorTypes.Spline:
                        var spinterpolator = (data.Processor.Interpolator as SplineInterpolator);
                        file.Add(Variable(SplineAlgorithm, (int)spinterpolator.Algorithm));
                        file.Add(Variable(SplineShowHandles, spinterpolator.ShowHandles));
                        file.Add(Variable(SplineAllowPointTimeDragging, spinterpolator.AllowPointTimeDragging));
                        file.Add(Variable(SplinePointDensity, (int)spinterpolator.PointDensity));
                        file.Add(Variable(SplineHandleMode, (int)spinterpolator.HandleMode));
                        file.Add(Variable(SplinePointsPerInjection, FormatInt(spinterpolator.PointsPerInjection)));
                        file.Add(Variable(SplineLocked, spinterpolator.IsLocked));
                        file.Add(ListHeader(SplinePointList));
                        foreach (var sp in spinterpolator.SplinePoints)
                        {
                            file.Add(string.Join(",",
                                FormatDouble(sp.Time),
                                FormatDouble(sp.Power),
                                FormatInt(sp.ID),
                                FormatDouble(sp.Slope),
                                FormatInt(sp.Locked ? 1 : 0),
                                FormatInt(sp.UserDefined ? 1 : 0),
                                FormatInt(sp.SlopeLocked ? 1 : 0),
                                FormatInt(sp.Linear ? 1 : 0)));
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
                file.Add(ParameterLine(par.Key, par.Value));
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
            foreach (var par in solution.Parameters)
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
            if (result.ValiditySnapshot != null)
                file.Add(Variable(AnalysisResultValiditySnapshotData, EncodeText(result.ValiditySnapshot.ToJson())));

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
                file.Add(ParameterLine(par.Key, par.Value));
            }
            file.Add(EndListHeader);

            //ModelCloneOptions
            file.Add(ObjectHeader(MdlCloneOptions));
            file.Add(Variable(SolErrorMethod, (int)solution.Model.ModelCloneOptions.ErrorEstimationMethod));
            file.Add(Variable(SolCloneIsGlobal, solution.Model.ModelCloneOptions.IsGlobalClone));
            file.Add(Variable(SolCloneConcentrationVariance, solution.Model.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap));
            file.Add(Variable(SolCloneAutoVariance, solution.Model.ModelCloneOptions.EnableAutoConcentrationVariance));
            file.Add(Variable(SolCloneAutoVarianceValue, solution.Model.ModelCloneOptions.AutoConcentrationVariance));
            file.Add(Variable(SolCloneUnlockParameters, solution.Model.ModelCloneOptions.UnlockBootstrapParameters));
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

        static string ParameterLine(ParameterType key, Parameter parameter)
        {
            return Variable(key.ToString() + ":" + ((int)key).ToString(), parameter.Value)
                + ":" + FormatInt(parameter.IsLocked ? 1 : 0);
        }
    }
}
