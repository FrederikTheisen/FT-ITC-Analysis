using System;
using AppKit;
using Foundation;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;

namespace AnalysisITC
{
    public class FTITCFormat
    {
        public const string ExperimentHeader = "Experiment";
        public const string ID = "ID";
        public const string FileName = "FileName";
        public const string Date = "Date";
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


        public static string Header(string header) => "<" + header + ">";
        public static string EndHeader(string header) => Header("/" + header);
        public static string Encapsulate(string header, string content) => Header(header) + content + EndHeader(header) + Environment.NewLine;
        public static string Encapsulate(string header, double value) => Encapsulate(header, value.ToString());
        public static string Encapsulate(string header, bool value) => Encapsulate(header, value ? 1 : 0);

        public static string ReaderPattern(string header) => Header(header) + ".*?" + EndHeader(header);
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

            if (true)
            {
                dlg.BeginSheet(NSApplication.SharedApplication.MainWindow, (result) => {
                    if (result == 1)
                    {
                        using (var writer = new StreamWriter(dlg.Filename))
                        {
                            foreach (var line in file) writer.Write(line);
                        }
                    }
                    //var alert = new NSAlert()
                    //{
                    //    AlertStyle = NSAlertStyle.Critical,
                    //    InformativeText = "We need to save the document here...",
                    //    MessageText = "Save Document",
                    //};
                    //alert.RunModal();
                });
            }
            else
            {
                if (dlg.RunModal() == 1)
                {
                    var alert = new NSAlert()
                    {
                        AlertStyle = NSAlertStyle.Critical,
                        InformativeText = "We need to save the document here...",
                        MessageText = "Save Document",
                    };
                    alert.RunModal();
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
            exp += Encapsulate(UseIntegrationFactorLength, data.UseIntegrationFactorLength);
            exp += Encapsulate(IntegrationLengthFactor, data.IntegrationLengthFactor);
            exp += Encapsulate(FeedBackMode, (int)data.FeedBackMode);
            exp += Encapsulate(CellVolume, data.CellVolume);
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

        static void SaveAnalysisResult(AnalysisResult result)
        {

        }
    }
}
