using System;
using AppKit;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AnalysisITC;
using CoreGraphics;
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
            var accessory = new NSView(new CGRect(0, 0, 340, 92));
            var deadVolumeField = new NSTextField(new CGRect(190, 58, 80, 22))
            {
                StringValue = (defaults.DeadVolume * 1e6).ToString("G4", CultureInfo.InvariantCulture),
            };
            var mixingFractionField = new NSTextField(new CGRect(190, 30, 80, 22))
            {
                StringValue = "20",
            };
            var removeOverflowCheckbox = new NSButton(new CGRect(0, 0, 300, 20))
            {
                Title = "Remove overflow between segments",
                State = defaults.DidRemoveOverflow ? NSCellStateValue.On : NSCellStateValue.Off,
                ControlSize = NSControlSize.Small,
                Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize),
            };
            removeOverflowCheckbox.SetButtonType(NSButtonType.Switch);

            accessory.AddSubview(MakeLabel("Dead volume (uL)", 0, 61));
            accessory.AddSubview(deadVolumeField);
            accessory.AddSubview(MakeLabel("Mixing fraction (%)", 0, 33));
            accessory.AddSubview(mixingFractionField);
            accessory.AddSubview(removeOverflowCheckbox);

            var alert = new NSAlert
            {
                AlertStyle = NSAlertStyle.Informational,
                MessageText = "Tandem ITC File Detected",
                InformativeText = $"The file \"{experiment.FileName}\" contains {segmentCount} tandem segments. Choose how to process segment-to-segment concentrations.",
                AccessoryView = accessory,
            };

            alert.AddButton("Use MicroCal Defaults");
            alert.AddButton("Use Back-Mixing Compensation");
            alert.Layout();

            var response = (int)alert.RunModal();
            if (response != 1001) return defaults;

            if (!TryParseDouble(deadVolumeField.StringValue, out var deadVolumeMicroliters)
                || deadVolumeMicroliters <= 0)
                throw new FormatException("Dead volume must be a positive number in microliters.");
            if (!TryParseDouble(mixingFractionField.StringValue, out var mixingFractionPercent)
                || mixingFractionPercent < 0
                || mixingFractionPercent > 100)
                throw new FormatException("Mixing fraction must be a number from 0 to 100 percent.");

            return new TandemConcatenation.BackMixingSettings
            {
                UseBackMixingMethod = true,
                DeadVolume = deadVolumeMicroliters * 1e-6,
                MixingFraction = mixingFractionPercent / 100.0,
                DidRemoveOverflow = removeOverflowCheckbox.State == NSCellStateValue.On,
                RemoveOverflowVolume = defaults.RemoveOverflowVolume,
            };
        }

        static NSTextField MakeLabel(string text, nfloat x, nfloat y)
        {
            return new NSTextField(new CGRect(x, y, 170, 17))
            {
                StringValue = text,
                Editable = false,
                Bordered = false,
                DrawsBackground = false,
                Selectable = false,
            };
        }

        static bool TryParseDouble(string text, out double value)
        {
            return double.TryParse(
                (text ?? string.Empty).Trim().Replace(',', '.'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        static void ReadInjection(ExperimentData experiment, string line, MicroCalReadState readState)
        {
            var data = Utilities.StringParsers.ParseLine(line.Substring(1));
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
            var dat = StringParsers.ParseLine(line);

            experiment.DataPoints.Add(new DataPoint(dat[0], (float)Energy.ConvertToJoule(dat[1], EnergyUnit.MicroCal), dat[2], dat[3], dat[4], dat[5], dat[6]));
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
