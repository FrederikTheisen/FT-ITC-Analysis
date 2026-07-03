using System;
using System.Collections.Generic;
using System.IO;
using AnalysisITC;
using AnalysisITC.Platform;
using System.Linq;
using AnalysisITC.Core.Utilities;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.DataReaders
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

                bool shouldAscertainDataFormat = true;
                bool isDataStream = false;
                var readState = new MicroCalReadState();

                while ((line = stream.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Count() == 0) continue;
                    counter++;
                    if (line == "@0")
                    {
                        isDataStream = true;
                        readState.ProtocolInjectionCount = experiment.Injections.Count;
                        continue;
                    }

                    if (isDataStream)
                    {
                        if (shouldAscertainDataFormat)
                        {
                            experiment.DataSourceFormat = StringParsers.ParseLine(line).Count() > 3 ? ITCDataFormat.ITC200 : ITCDataFormat.VPITC;
                            shouldAscertainDataFormat = false;
                        }

                        if (line.First() == '@') ReadInjection(experiment, line, readState);
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
                        experiment.Instrument = ITCInstrumentAttribute.TryResolveMicroCalInstrument(line);
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

                var tandemSegments = readState.GetSegments(experiment.InjectionCount).ToList();
                if (tandemSegments.Count > 1)
                {
                    var tandemSettings = PromptBackMixingSettings(experiment, tandemSegments.Count);
                    TandemConcatenation.ProcessInjectionsWithBackMixing(
                        experiment,
                        tandemSegments,
                        tandemSettings);
                }
                else
                {
                    ProcessInjections(experiment);
                }
            }

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

        static TandemConcatenation.BackMixingSettings PromptBackMixingSettings(
            ExperimentData experiment,
            int segmentCount)
        {
            var defaults = TandemConcatenation.BackMixingSettings.MicroCalDefault();
            return PlatformServices.TandemImportPromptService.AskBackMixingSettings(
                experiment.FileName,
                segmentCount,
                defaults);
        }

        static void ReadInjection(ExperimentData experiment, string line, MicroCalReadState readState)
        {
            var data = StringParsers.ParseLine(line.Substring(1));
            int id = (int)data[0] - 1;

            var inj = experiment.Injections.Find(o => o.ID == id);
            if (inj == null)
            {
                inj = CreateInjectionFromDataStream(experiment, data, id, readState.ProtocolInjectionCount);
                experiment.Injections.Add(inj);
            }

            var isSegmentStart = readState.RegisterInjection(id, data.Length > 3 ? data[3] : (float?)null);
            if (isSegmentStart) inj.Include = false;

            if (data.Length > 3 && Math.Abs(data[3] - experiment.DataPoints.Last().Time) < 10)
                inj.Time = data[3];
            else
                inj.Time = experiment.DataPoints.Last().Time;

            inj.Temperature = experiment.DataPoints.Last().Temperature;
        }

        static InjectionData CreateInjectionFromDataStream(
            ExperimentData experiment,
            float[] data,
            int id,
            int protocolInjectionCount)
        {
            var template = protocolInjectionCount > 0
                ? experiment.Injections.FirstOrDefault(inj => inj.ID == id % protocolInjectionCount)
                : null;
            var volume = data.Length > 1 ? data[1] * 1e-6 : template?.Volume ?? 0.0;
            var duration = data.Length > 2 ? data[2] : template?.Duration ?? 0.0f;
            var delay = template?.Delay ?? 0.0f;
            var temperature = experiment.DataPoints.Count > 0
                ? experiment.DataPoints.Last().Temperature
                : experiment.TargetTemperature;

            return InjectionData.FromPEAQFile(
                experiment,
                id,
                id > 0,
                0.0,
                volume,
                delay,
                duration,
                temperature);
        }

        static void ReadDataPoint(ExperimentData experiment, string line)
        {
            switch (experiment.DataSourceFormat)
            {
                case ITCDataFormat.VPITC:
                    ReadVPITCDataPoint(experiment, line);
                    break;
                default:
                    ReadITC200DataPoint(experiment, line);
                    break;
            }
        }

        static void ReadITC200DataPoint(ExperimentData experiment, string line)
        {
            var dat = StringParsers.ParseLine(line);

            experiment.DataPoints.Add(new DataPoint(dat[0], (float)Energy.ConvertToJoule(dat[1], EnergyUnit.MicroCal), dat[2], dat[3], dat[4], dat[5], dat[6]));
        }

        static void ReadVPITCDataPoint(ExperimentData experiment, string line)
        {
            var dat = StringParsers.ParseLine(line);

            experiment.DataPoints.Add(new DataPoint(dat[0], (float)Energy.ConvertToJoule(dat[1], EnergyUnit.MicroCal), dat[2]));
        }

        sealed class MicroCalReadState
        {
            readonly List<TandemConcatenation.TandemInjectionSegment> segments = new List<TandemConcatenation.TandemInjectionSegment>();

            public int ProtocolInjectionCount { get; set; }
            int currentSegmentStart;
            float? previousLocalInjectionTime;

            public bool RegisterInjection(int id, float? localInjectionTime)
            {
                var isSegmentStart = false;

                if (localInjectionTime.HasValue
                    && previousLocalInjectionTime.HasValue
                    && localInjectionTime.Value + 1.0f < previousLocalInjectionTime.Value
                    && id > currentSegmentStart)
                {
                    segments.Add(new TandemConcatenation.TandemInjectionSegment(
                        currentSegmentStart,
                        id - currentSegmentStart));
                    currentSegmentStart = id;
                    isSegmentStart = true;
                }

                if (localInjectionTime.HasValue)
                    previousLocalInjectionTime = localInjectionTime.Value;

                return isSegmentStart;
            }

            public IReadOnlyList<TandemConcatenation.TandemInjectionSegment> GetSegments(int injectionCount)
            {
                if (segments.Count == 0) return Array.Empty<TandemConcatenation.TandemInjectionSegment>();
                if (injectionCount > currentSegmentStart)
                {
                    segments.Add(new TandemConcatenation.TandemInjectionSegment(
                        currentSegmentStart,
                        injectionCount - currentSegmentStart));
                }

                return segments;
            }
        }
    }
}
