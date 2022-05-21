using System;
using System.Text.Json;

namespace AnalysisITC
{
    public static class DataWriter
    {
        const string ID = "ID";
        const string FileName = "FileName";
        const string Date = "Date";
        const string SyringeConcentration = "SyringeConcentration";
        const string CellConcentration = "CellConcentration";
        const string CellVolume = "CellVolume";
        const string StirringSpeed = "StirringSpeed";
        const string TargetTemperature = "TargetTemperature";
        const string MeasuredTemperature = "MeasuredTemperature";
        const string InitialDelay = "InitialDelay";
        const string TargetPowerDiff = "TargetPowerDiff";
        const string UseIntegrationFactorLength = "UseIntegrationFactorLength";
        const string IntegrationLengthFactor = "IntegrationLengthFactor";
        const string FeedBackMode = "FeedBackMode";
        const string Include = "Include";
        const string InjectionList = "InjectionList";
        const string DataPointList = "DataPointList";

        static string Header(string header) => "<" + header + ">";
        static string EndHeader(string header) => "</" + header + ">";
        static string Encapsulate(string header, string content) => Header(header) + content + EndHeader(header);
        static string Encapsulate(string header, double value) => Encapsulate(header, value.ToString());

        public static void SaveState()
        {
            foreach (var data in DataManager.Data)
            {
                GetExperimentString(data);
            }
        }

        static string GetExperimentString(ExperimentData data)
        {
            string exp = "<exp>";
            exp += Encapsulate(ID, data.UniqueID);
            exp += Encapsulate(FileName, data.FileName);
            exp += Encapsulate(Date, data.Date.ToString());
            exp += Encapsulate(SyringeConcentration, data.SyringeConcentration);
            exp += Encapsulate(CellConcentration, data.CellConcentration);
            exp += Encapsulate(StirringSpeed, data.StirringSpeed);
            exp += Encapsulate(TargetTemperature, data.TargetTemperature);
            exp += Encapsulate(MeasuredTemperature, data.MeasuredTemperature);
            exp += Encapsulate(InitialDelay, data.InitialDelay);
            exp += Encapsulate(TargetPowerDiff, data.TargetPowerDiff);
            exp += Encapsulate(UseIntegrationFactorLength, data.UseIntegrationFactorLength.ToString());
            exp += Encapsulate(IntegrationLengthFactor, data.IntegrationLengthFactor);
            exp += Encapsulate(FeedBackMode, (int)data.FeedBackMode);
            exp += Encapsulate(CellVolume, data.CellVolume);
            exp += Encapsulate(CellVolume, data.CellVolume);

            string injections = Header(InjectionList);

            foreach (var inj in data.Injections)
            {
                injections += ">" + inj.ID + ",";
                injections += inj.Time + ",";
                injections += inj.Volume + ",";
                injections += inj.Delay + ",";
                injections += inj.Duration + ",";
                injections += inj.Temperature + ",";
                injections += inj.IntegrationStartDelay + ",";
                injections += inj.IntegrationLength + ",";
            }

            exp += injections + EndHeader(InjectionList);

            string datapoints = Header(DataPointList);

            foreach (var dp in data.DataPoints)
            {
                string line = ">";
                line += dp.Time.ToString() + ",";
                line += dp.Power.ToString() + ",";
                line += dp.Temperature.ToString() + ",";
                line += dp.ShieldT.ToString();

                datapoints += line;
            }

            exp += datapoints + EndHeader(DataPointList);

            if (data.Processor != null)
            {
                string processor = "<processor>";

                exp += processor + "</processor>";
            }

            return "<exp>" + "</exp>";
        }

        static void SaveAnalysisResult(AnalysisResult result)
        {

        }
    }
}
