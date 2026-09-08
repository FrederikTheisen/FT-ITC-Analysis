using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using AnalysisITC.Platform;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Analysis;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
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

        public const string FTITCVersion = "FTITCVersion";

        public const string ExperimentHeader = "Experiment";
        public const string TandemExperimentHeader = "TandemExperiment";
        public const string ID = "ID";
        public const string AssignedName = "Name";
        public const string FileName = "FileName";
        public const string Comments = "Comments";
        public const string Date = "Date";
        public const string DateSource = "DateSource";
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
        public const string SolBootstrapSnapshots = "BootSnapshots";
        public const string BootSnapshot = "BootSnapshot";
        public const string BootSnapshotVersion = "BootSnapshotVersion";
        public const string BootReplicateIndex = "BootReplicateIndex";
        public const string BootCellConcentration = "BootCellConcentration";
        public const string BootSyringeConcentration = "BootSyringeConcentration";
        public const string BootCellVolume = "BootCellVolume";
        public const string BootMeasuredTemperature = "BootMeasuredTemperature";
        public const string BootSnapshotParameters = "BootSnapshotParameters";
        public const string BootSnapshotModelOptions = "BootSnapshotModelOptions";
        public const string BootSnapshotInjections = "BootSnapshotInjections";
        public const string BootSnapshotSegments = "BootSnapshotSegments";
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
        public const string SolConvErrorLimitTerminations = "ErrorLimitTerminations";
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

        public static event EventHandler CurrentAccessedAppDocumentPathChanged
        {
            add => ProjectDocumentState.PathChanged += value;
            remove => ProjectDocumentState.PathChanged -= value;
        }

        public static string CurrentAccessedAppDocumentPath
        {
            get => ProjectDocumentState.Path;
            set => ProjectDocumentState.Path = value;
        }
    }
}
