using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AnalysisITC.Platform;
using System.Linq;
using AnalysisITC.Core.Utilities;

using AnalysisITC.Core.Application;
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
            using (var stream = File.OpenRead(path))
                return ReadStream(stream, Path.GetFileName(path), File.GetCreationTime(path), interactive: true);
        }

        internal static ExperimentData ReadStream(
            Stream stream,
            string fileName,
            DateTime? fallbackDate = null,
            bool interactive = false,
            Action<string> warning = null)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var experiment = new ExperimentData(Path.GetFileName(fileName ?? "uploaded.itc"));
            experiment.Date = fallbackDate ?? default(DateTime);
            experiment.DateSource = fallbackDate.HasValue ? ExperimentDateSource.FileSystem : ExperimentDateSource.Unknown;
            experiment.DataSourceFormat = ITCDataFormat.ITC200;

            using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 4096, leaveOpen: true))
            {
                int counter = 0;
                int counter2 = 0;
                int counter3 = -1;
                string line;

                bool isDataStream = false;
                var readState = new MicroCalReadState();

                while ((line = reader.ReadLine()) != null)
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

                                if (b)
                                {
                                    experiment.Date = date;
                                    experiment.DateSource = ExperimentDateSource.DataFile;
                                }
                            }
                        }
                    }

                    if (counter3 > -1) counter3++;
                }

                if (interactive) Console.WriteLine($"File has {counter} lines.");

                var tandemSegments = readState.GetSegments(experiment.InjectionCount).ToList();
                if (tandemSegments.Count > 1)
                {
                    var dilutionMethod = interactive
                        ? AppSettings.DilutionCalculationMethod
                        : DilutionMethod.MicroCal;
                    var tandemSettings = interactive
                        ? PromptBackMixingSettings(experiment, tandemSegments.Count)
                        : TandemConcatenation.BackMixingSettings.MicroCalDefault();
                    if (!interactive)
                        warning?.Invoke("The raw file contains concatenated runs; deterministic MicroCal dilution without back-mixing was applied.");
                    TandemConcatenation.ProcessInjectionsForTandemImport(
                        experiment,
                        tandemSegments,
                        tandemSettings,
                        dilutionMethod);
                }
                else
                {
                    if (interactive) ProcessInjections(experiment);
                    else ProcessInjectionsMicroCal(experiment);
                }
            }

            ProcessExperiment(experiment);

            return experiment;
        }

        private static float LineToFloat(string line)
        {
            return float.Parse(line.Substring(1).Trim(), System.Globalization.CultureInfo.InvariantCulture);
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
            var injectionLine = line.Substring(1);
            var fields = injectionLine.Split(',');
            var data = StringParsers.ParseLine(injectionLine);
            int id = (int)data[0] - 1;

            var inj = experiment.Injections.Find(o => o.ID == id);
            if (inj == null)
            {
                inj = CreateInjectionFromDataStream(experiment, data, id, readState.ProtocolInjectionCount);
                experiment.Injections.Add(inj);
            }

            if (fields.Length > 1)
                inj.SetVolume(double.Parse(fields[1].Trim(), CultureInfo.InvariantCulture) * 1e-6);

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
            ReadITC200DataPoint(experiment, line);
        }

        static void ReadITC200DataPoint(ExperimentData experiment, string line)
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
