using System;
using System.IO;
using AnalysisITC;
using System.Linq;
using Utilities;

namespace DataReaders
{
    class TAFileReader : RawDataReader
    {
        public static ExperimentData ReadPath(string path)
        {
            var experiment = new ExperimentData(Path.GetFileName(path));
            experiment.Date = File.GetLastWriteTimeUtc(path);
            experiment.DataSourceFormat = ITCDataFormat.TAITC;
            experiment.FeedBackMode = FeedbackMode.High;
            experiment.StirringSpeed = -1;

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
            ProcessExperiment(experiment);

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
}
