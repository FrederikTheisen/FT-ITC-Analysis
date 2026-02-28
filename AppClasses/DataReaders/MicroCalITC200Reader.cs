using System;
using System.IO;
using AnalysisITC;
using System.Linq;
using Utilities;

namespace DataReaders
{
    class MicroCalITC200Reader : RawDataReader
    {
        public static ExperimentData ReadPath(string path)
        {
            var experiment = new ExperimentData(Path.GetFileName(path));
            experiment.Date = File.GetCreationTime(path);
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
            ProcessExperiment(experiment);

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

            if (data.Length > 3) inj.Time = data[3];
            else inj.Time = experiment.DataPoints.Last().Time;
            inj.Temperature = experiment.DataPoints.Last().Temperature;
        }

        static void ReadDataPoint(ExperimentData experiment, string line)
        {
            var dat = StringParsers.ParseLine(line);

            experiment.DataPoints.Add(new DataPoint(dat[0], (float)Energy.ConvertToJoule(dat[1], EnergyUnit.MicroCal), dat[2], dat[3], dat[4], dat[5], dat[6]));
        }
    }
}
