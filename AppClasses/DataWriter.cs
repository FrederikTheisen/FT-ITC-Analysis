using System;
using AppKit;
using Foundation;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AnalysisITC
{
    public class FTITCFormat
    {
        public const string ExperimentHeader = "Experiment";
        public const string ID = "ID";
        public const string FileName = "FileName";
        public const string Date = "Date";
        public const string SourceFormat = "Source";
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

        //ftitc_old formatting
        public static string Header(string header) => "<" + header + ">";
        public static string EndHeader(string header) => Header("/" + header);
        public static string Encapsulate(string header, string content) => Header(header) + content + EndHeader(header) + Environment.NewLine;
        public static string Encapsulate(string header, double value) => Encapsulate(header, value.ToString());
        public static string Encapsulate(string header, bool value) => Encapsulate(header, value ? 1 : 0);

        public static string ReaderPattern(string header) => Header(header) + ".*?" + EndHeader(header);

        //ftitc formatting code
        public const string EndFileHeader = "ENDFILE";
        public const string EndListHeader = "ENDLIST";
        public const string EndObjectHeader = "ENDOBJECT";

        public static string FileHeader(string header, string filename) => "FILE:" + header + ":" + filename;
        public static string ObjectHeader(string header) => "OBJECT:" + header; 
        public static string Variable(string header, string value) => header + ":" + value;
        public static string Variable(string header, double value) => Variable(header, value.ToString());
        public static string Variable(string header, bool value) => Variable(header, value ? "1" : "0");
        public static string ListHeader(string header) => "LIST:" + header;

        public static double DParse(string value) => double.Parse(value);
        public static float FParse(string value) => float.Parse(value);
        public static int IParse(string value) => int.Parse(value);
        public static bool BParse(string value) => value == "1";
    }

    public class FTITCWriter : FTITCFormat
    {
        public static void SaveState()
        {
            var file = new List<string>();

            foreach (var data in DataManager.Data)
            {
                file.Add(GetExperimentString(data));
            }

            var dlg = new NSSavePanel();
            dlg.Title = "Save FT-ITC File";
            dlg.AllowedFileTypes = new string[] { "ftitc" };

            dlg.BeginSheet(NSApplication.SharedApplication.MainWindow, (result) =>
            {
                if (result == 1)
                {
                    using (var writer = new StreamWriter(dlg.Filename))
                    {
                        foreach (var line in file) writer.Write(line);
                    }
                }
            });
        }

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
                }
            });
        }

        static async Task WriteFile(string path)
        {
            using (var writer = new StreamWriter(path))
            {
                foreach (var data in DataManager.Data)
                {
                    await WriteExperimentString(data, writer);
                }
            }
        }

        static string GetExperimentString(ExperimentData data)
        {
            string exp = "";
            exp += Encapsulate(ID, data.UniqueID);
            exp += Encapsulate(FileName, data.FileName);
            exp += Encapsulate(Date, data.Date.ToString());
            exp += Encapsulate(Include, data.Include);
            exp += Encapsulate(SyringeConcentration, data.SyringeConcentration);
            exp += Encapsulate(CellConcentration, data.CellConcentration);
            exp += Encapsulate(StirringSpeed, data.StirringSpeed);
            exp += Encapsulate(TargetTemperature, data.TargetTemperature);
            exp += Encapsulate(MeasuredTemperature, data.MeasuredTemperature);
            exp += Encapsulate(InitialDelay, data.InitialDelay);
            exp += Encapsulate(TargetPowerDiff, data.TargetPowerDiff);
            exp += Encapsulate(UseIntegrationFactorLength, (int)data.IntegrationLengthMode);
            exp += Encapsulate(IntegrationLengthFactor, data.IntegrationLengthFactor);
            exp += Encapsulate(FeedBackMode, (int)data.FeedBackMode);
            exp += Encapsulate(CellVolume, data.CellVolume);

            string injections = "";

            foreach (var inj in data.Injections)
            {
                injections += inj.ID + ",";
                injections += (inj.Include ? 1 : 0) + ",";
                injections += inj.Time + ",";
                injections += inj.Volume + ",";
                injections += inj.Delay + ",";
                injections += inj.Duration + ",";
                injections += inj.Temperature + ",";
                injections += inj.IntegrationStartDelay + ",";
                injections += inj.IntegrationLength + ";";
            }

            exp += Encapsulate(InjectionList, injections.Substring(0, injections.Length - 1));

            string datapoints = "";

            foreach (var dp in data.DataPoints)
            {
                string line = "";
                line += dp.Time + ",";
                line += dp.Power + ",";
                line += dp.Temperature + ",";
                line += dp.ShieldT.ToString() + ";";

                datapoints += line;
            }

            exp += Encapsulate(DataPointList, datapoints.Substring(0, datapoints.Length - 1));

            if (data.Processor != null)
            {
                string s = "";

                s += Encapsulate(ProcessorType, (int)data.Processor.BaselineType);
                switch (data.Processor.BaselineType)
                {
                    case BaselineInterpolatorTypes.Polynomial:
                        s += Encapsulate(PolynomiumDegree, (data.Processor.Interpolator as PolynomialLeastSquaresInterpolator).Degree);
                        s += Encapsulate(PolynomiumLimit, (data.Processor.Interpolator as PolynomialLeastSquaresInterpolator).ZLimit);
                        break;
                    case BaselineInterpolatorTypes.Spline:
                        var spinterpolator = (data.Processor.Interpolator as SplineInterpolator);
                        s += Encapsulate(SplineAlgorithm, (int)spinterpolator.Algorithm);
                        s += Encapsulate(SplineHandleMode, (int)spinterpolator.HandleMode);
                        s += Encapsulate(SplineFraction, spinterpolator.FractionBaseline);
                        s += Encapsulate(SplineLocked, spinterpolator.IsLocked ? 1 : 0);
                        string points = "";
                        foreach (var sp in spinterpolator.SplinePoints) points += sp.Time + "," + sp.Power + "," + sp.ID + "," + sp.Slope + ";";
                        s += Encapsulate(SplinePointList, points.Substring(0, points.Length - 1));
                        break;
                    default:
                    case BaselineInterpolatorTypes.ASL:
                    case BaselineInterpolatorTypes.None:
                        break;
                }

                exp += Encapsulate(Processor, s);
            }

            return Encapsulate(ExperimentHeader, exp);
        }

        static async Task WriteExperimentString(ExperimentData data, StreamWriter stream)
        {
            var file = new List<string>();
            file.Add(FileHeader(ExperimentHeader, data.FileName));
            file.Add(Variable(ID, data.UniqueID));
            file.Add(Variable(Date, data.Date.ToString()));
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

        static void GetAnalysisResultString(AnalysisResult result)
        {

        }
    }
}
