using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using System.Threading.Tasks;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.DataReaders
{
    class FTITCReader : FTITCFormat
    {
        readonly List<ITCDataContainer> data = new List<ITCDataContainer>();
        readonly bool interactive;
        readonly bool processProcessorData;

        FTITCReader(bool interactive, bool processProcessorData)
        {
            this.interactive = interactive;
            this.processProcessorData = processProcessorData;
        }

        public static async Task<ITCDataContainer[]> ReadPath(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var result = await ReadStream(stream, interactive: true);
                // FTITC is an import format.  Never retain its source path as a save
                // target; the first Save must create a native FTXTC package.
                CurrentAccessedAppDocumentPath = "";
                return result;
            }
        }

        internal static async Task<ITCDataContainer[]> ReadStream(Stream stream, bool interactive = false, bool processProcessorData = true)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            MemoryStream buffered = null;
            var input = stream;
            if (!stream.CanSeek)
            {
                buffered = new MemoryStream();
                await stream.CopyToAsync(buffered);
                buffered.Position = 0;
                input = buffered;
            }

            try
            {
                if (IsOriginalTaggedFormat(input))
                    return await ReadOriginalTaggedFormat(input, processProcessorData);

                var parser = new FTITCReader(interactive, processProcessorData);
                using (var reader = new StreamReader(input, System.Text.Encoding.UTF8, true, 4096, leaveOpen: true))
                    return await parser.Read(reader);
            }
            finally
            {
                buffered?.Dispose();
            }
        }

        static bool IsOriginalTaggedFormat(Stream stream)
        {
            var position = stream.Position;
            try
            {
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 4096, leaveOpen: true);
                var buffer = new char[4096];
                var count = reader.ReadBlock(buffer, 0, buffer.Length);
                var prefix = new string(buffer, 0, count).TrimStart('\uFEFF', '\r', '\n', ' ', '\t');
                return prefix.StartsWith(OldHeader(ExperimentHeader), StringComparison.Ordinal)
                    || prefix.IndexOf(OldHeader(ExperimentHeader), StringComparison.Ordinal) >= 0
                        && prefix.IndexOf(FileHeader(ExperimentHeader, ""), StringComparison.Ordinal) < 0;
            }
            finally
            {
                stream.Position = position;
            }
        }

        async Task<ITCDataContainer[]> Read(StreamReader reader)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            string line;

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var startms = watch.ElapsedMilliseconds;
                var input = line.Split(new[] { ':' }, 3);
                if (interactive)
                    AppEventHandler.PrintAndLog($"Read {(input.Length == 3 ? $"{input[0]}:{input[1]}:{string.Join(",", input[2].Split(',').Select(DecodeText))}" : line)} Start: {watch.ElapsedMilliseconds}");

                if (input.Length > 1 && input[0] == "FILE")
                {
                    if (input[1] == ExperimentHeader) data.Add(await ReadExperimentDataFile(reader, line));
                    else if (input[1] == TandemExperimentHeader) data.Add(await ReadTandemExperimentDataFile(reader, line));
                    else if (input[1] == AnalysisResultHeader)
                    {
                        var result = await ReadAnalysisResult(reader, line);
                        if (result != null) data.Add(result);
                    }
                }

                if (interactive) AppEventHandler.PrintAndLog($"Total time: {watch.ElapsedMilliseconds - startms}");
            }

            return data.ToArray();
        }

        static async Task<ITCDataContainer[]> ReadOriginalTaggedFormat(Stream stream, bool processProcessorData)
        {
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, 4096, leaveOpen: true);
            var text = await reader.ReadToEndAsync();
            var experiments = new List<ITCDataContainer>();

            foreach (var section in TaggedSections(text, ExperimentHeader))
            {
                var fileName = TaggedContent(section, FileName) ?? string.Empty;
                var experiment = new ExperimentData(fileName);
                var sourceFormat = TaggedContent(section, SourceFormat);
                if (!string.IsNullOrWhiteSpace(sourceFormat))
                    experiment.DataSourceFormat = ParseSourceFormat(sourceFormat, fileName);
                var id = TaggedContent(section, ID);
                if (!string.IsNullOrWhiteSpace(id)) experiment.SetID(id);
                var name = TaggedContent(section, AssignedName);
                if (!string.IsNullOrWhiteSpace(name)) experiment.Name = name;
                var date = TaggedContent(section, Date);
                if (!string.IsNullOrWhiteSpace(date)) experiment.SetDate(DTParse(date));
                var dateSource = TaggedContent(section, DateSource);
                if (!string.IsNullOrWhiteSpace(dateSource)) experiment.DateSource = (ExperimentDateSource)ParseTaggedInt(section, DateSource, (int)ExperimentDateSource.Unknown);
                experiment.SyringeConcentration = ParseTaggedFwe(section, SyringeConcentration);
                experiment.CellConcentration = ParseTaggedFwe(section, CellConcentration);
                experiment.StirringSpeed = ParseTaggedDouble(section, StirringSpeed);
                experiment.TargetTemperature = ParseTaggedDouble(section, TargetTemperature);
                experiment.MeasuredTemperature = ParseTaggedDouble(section, MeasuredTemperature, experiment.TargetTemperature);
                experiment.InitialDelay = ParseTaggedDouble(section, InitialDelay);
                experiment.TargetPowerDiff = ParseTaggedDouble(section, TargetPowerDiff);
                experiment.CellVolume = ParseTaggedDouble(section, CellVolume);
                experiment.FeedBackMode = (FeedbackMode)ParseTaggedInt(section, FeedBackMode, (int)FeedbackMode.Null);
                experiment.Instrument = (ITCInstrument)ParseTaggedInt(section, Instrument, (int)ITCInstrument.Unknown);

                var injections = TaggedRows(section, InjectionList).Select(row => RestoreOriginalTaggedInjection(experiment, row)).ToList();
                experiment.Injections = injections;

                var points = new List<DataPoint>();
                foreach (var row in TaggedRows(section, DataPointList))
                {
                    var columns = SplitCsv(row);
                    if (columns.Length < 3)
                        throw new InvalidDataException("Original FTITC data-point rows must have at least three columns.");
                    points.Add(new DataPoint(FParse(columns[0]), FParse(columns[1]), FParse(columns[2])));
                }
                experiment.DataPoints = points;

                await RestoreOriginalTaggedProcessor(experiment, TaggedContent(section, Processor), processProcessorData);
                if (!experiment.IsTandemExperiment) RawDataReader.ProcessInjectionsMicroCal(experiment);
                experiment.Include = TaggedContent(section, Include) == "1";
                experiment.CalculateExperimentHeatDirection();
                experiments.Add(experiment);
            }

            if (experiments.Count == 0)
                throw new InvalidDataException("The original FTITC file did not contain an Experiment section.");
            return experiments.ToArray();
        }

        static InjectionData RestoreOriginalTaggedInjection(ExperimentData experiment, string row)
        {
            var values = SplitCsv(row);
            if (values.Length < 9)
                throw new InvalidDataException("Original FTITC injection rows must have at least nine columns.");
            var id = IParse(values[0]);
            var include = BParse(values[1]);
            var time = FParse(values[2]);
            var volume = DParse(values[3]);
            var delay = FParse(values[4]);
            var duration = FParse(values[5]);
            var temperature = DParse(values[6]);
            var integrationStart = FParse(values[7]);
            // The original tagged writer stored integration length, whereas the
            // later line-based FTITC dialect stores an end offset.
            var integrationEnd = integrationStart + FParse(values[8]);
            var injection = new InjectionData(experiment, id, volume, experiment.SyringeConcentration * volume, include);
            injection.RestoreState(include, time, volume, delay, duration, 0, temperature,
                integrationStart, integrationEnd, 0, 0, 0, false, PeakHeatDirection.Unknown,
                new FloatWithError(), new FloatWithError());
            return injection;
        }

        static async Task RestoreOriginalTaggedProcessor(ExperimentData experiment, string section, bool processProcessorData)
        {
            if (string.IsNullOrWhiteSpace(section)) return;
            var typeText = TaggedContent(section, ProcessorType);
            if (string.IsNullOrWhiteSpace(typeText)) return;
            var processor = new DataProcessor(experiment);
            processor.InitializeBaseline((BaselineInterpolatorTypes)IParse(typeText));

            if (processor.Interpolator is SplineInterpolator spline)
            {
                var algorithm = TaggedContent(section, SplineAlgorithm);
                if (!string.IsNullOrWhiteSpace(algorithm)) spline.Algorithm = (SplineInterpolator.SplineInterpolatorAlgorithm)IParse(algorithm);
                var handleMode = TaggedContent(section, SplineHandleMode);
                if (!string.IsNullOrWhiteSpace(handleMode)) spline.HandleMode = (SplineInterpolator.SplineHandleMode)IParse(handleMode);
                var splineRows = TaggedRows(section, SplinePointList).ToList();
                if (splineRows.Count != 0)
                    spline.SetSplinePoints(splineRows.Select(row =>
                    {
                        var values = SplitCsv(row);
                        if (values.Length < 4) throw new InvalidDataException("Original FTITC spline-point rows must have four columns.");
                        return new SplineInterpolator.SplinePoint(DParse(values[0]), DParse(values[1]), IParse(values[2]), DParse(values[3]));
                    }).ToList());
            }
            else if (processor.Interpolator is PolynomialLeastSquaresInterpolator polynomial)
            {
                polynomial.Degree = ParseTaggedInt(section, PolynomiumDegree, polynomial.Degree);
                polynomial.ZLimit = ParseTaggedDouble(section, PolynomiumLimit, polynomial.ZLimit);
            }

            experiment.SetProcessor(processor);
            if (processProcessorData) await processor.ProcessData(replace: false, invalidate: false, showProgress: false);
            if (TaggedContent(section, SplineLocked) == "1") processor.Lock();
        }

        static IEnumerable<string> TaggedSections(string text, string header)
        {
            var startTag = OldHeader(header);
            var endTag = OldEndHeader(header);
            var offset = 0;
            while (offset < text.Length)
            {
                var start = text.IndexOf(startTag, offset, StringComparison.Ordinal);
                if (start < 0) yield break;
                start += startTag.Length;
                var end = text.IndexOf(endTag, start, StringComparison.Ordinal);
                if (end < 0) throw new InvalidDataException($"Original FTITC section '{header}' is not closed.");
                yield return text.Substring(start, end - start);
                offset = end + endTag.Length;
            }
        }

        static string TaggedContent(string text, string header) => TaggedSections(text, header).FirstOrDefault();
        static IEnumerable<string> TaggedRows(string text, string header) =>
            (TaggedContent(text, header) ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(row => row.Trim()).Where(row => row.Length != 0);
        static double ParseTaggedDouble(string text, string header, double fallback = 0)
        {
            var value = TaggedContent(text, header);
            return string.IsNullOrWhiteSpace(value) ? fallback : DParse(value);
        }
        static int ParseTaggedInt(string text, string header, int fallback = 0)
        {
            var value = TaggedContent(text, header);
            return string.IsNullOrWhiteSpace(value) ? fallback : IParse(value);
        }
        static FloatWithError ParseTaggedFwe(string text, string header)
        {
            var value = TaggedContent(text, header);
            return string.IsNullOrWhiteSpace(value) ? new FloatWithError() : FWEParse(value);
        }

        static string ReadRequiredLine(StreamReader reader)
        {
            var line = reader.ReadLine();
            if (line == null)
                throw new InvalidDataException("The FTITC file ended before the current section was closed.");
            return line;
        }

        async Task<ExperimentData> ReadTandemExperimentDataFile(StreamReader reader, string firstline)
        {
            if (interactive) AppEventHandler.PrintAndLog("Loading Tandem Experiment Data...", 1);

            string[] a = firstline.Split(new[] { ':' }, 3);
            var exp = new ExperimentData(DecodeText(a[2]));

            if (interactive)
            {
                StatusBarManager.SetSecondaryStatus($"{exp.Name}", 0);
                await Task.Delay(1);
            }

            await ReadExperimentData(reader, exp);

            if (exp.Solution != null) exp.UpdateSolution(exp.Solution.Model);

            return exp;
        }

        async Task<ExperimentData> ReadExperimentDataFile(StreamReader reader, string firstline)
        {
            if (interactive) AppEventHandler.PrintAndLog("Loading Experiment Data...", 1);

            string[] a = firstline.Split(new[] { ':' }, 3);
            var exp = new ExperimentData(DecodeText(a[2]));

            if (interactive)
            {
                StatusBarManager.SetSecondaryStatus($"{exp.Name}", 0);
                await Task.Delay(1);
            }

            await ReadExperimentData(reader, exp);

            // Tandem experiments contain segment-aware concentrations calculated when
            // the runs were concatenated. Reapplying the ordinary single-run dilution
            // model here destroys those values at and after segment transitions.
            // Older files may still use FILE:Experiment while carrying SegmentList, so
            // use the parsed segment data rather than relying only on the file header.
            if (!exp.IsTandemExperiment)
            {
                if (interactive) RawDataReader.ProcessInjections(exp);
                else RawDataReader.ProcessInjectionsMicroCal(exp);
            }

            if (exp.Solution != null) exp.UpdateSolution(exp.Solution.Model);

            exp.CalculateExperimentHeatDirection();
            return exp;
        }

        async Task<ExperimentData> ReadExperimentData(StreamReader reader, ExperimentData exp)
        {
            SolutionInterface sol = null;

            string line;

            while ((line = ReadRequiredLine(reader)) != EndFileHeader)
            {
                string[] v = SplitKeyValue(line);
                string key = v[0];
                string value = v.Length > 1 ? v[1] : string.Empty;

                switch (key)
                {
                    case ID: exp.SetID(value); break;
                    case AssignedName: exp.Name = DecodeText(value); break;
                    case Date: exp.Date = DTParse(value); break;
                    case DateSource: exp.DateSource = (ExperimentDateSource)IParse(value); break;
                    case SourceFormat: exp.DataSourceFormat = ParseSourceFormat(value, exp.FileName); break;
                    case Comments: exp.Comments = DecodeText(value); break;
                    case SyringeConcentration: exp.SyringeConcentration = FWEParse(value); break;
                    case CellConcentration: exp.CellConcentration = FWEParse(value); break;
                    case CellVolume: exp.CellVolume = DParse(value); break;
                    case StirringSpeed: exp.StirringSpeed = DParse(value); break;
                    case TargetTemperature: exp.TargetTemperature = DParse(value); break;
                    case MeasuredTemperature: exp.MeasuredTemperature = DParse(value); break;
                    case InitialDelay: exp.InitialDelay = DParse(value); break;
                    case TargetPowerDiff: exp.TargetPowerDiff = DParse(value); break;
                    case FeedBackMode: exp.FeedBackMode = (FeedbackMode)IParse(value); break;
                    case Include: exp.Include = BParse(value); break;
                    case Instrument: exp.Instrument = (ITCInstrument)IParse(value); break;
                    case "LIST" when value == InjectionList:
                        ReadInjectionList(exp, reader); break;
                    case "LIST" when value == DataPointList:
                        ReadDataList(exp, reader); break;
                    case "LIST" when value == ExperimentAttributes:
                        ReadAttributes(exp, reader); break;
                    case "LIST" when value == SegmentList:
                        ReadSegmentList(exp, reader); break;
                    case "OBJECT" when value == Processor:
                        await ReadProcessor(exp, reader, processProcessorData); break;
                    case "OBJECT" when value == ExperimentSolutionHeader:
                        sol = ReadSolution(reader, ReadRequiredLine(reader), exp);
                        exp.UpdateSolution(sol.Model);
                        break;
                        //case "OBJECT" when v[1] == SolutionHeader: exp.UpdateSolution(ReadSolution(reader, line).Model); break; //Not certain about implementation
                }
            }

            return exp;
        }

        // FTITC historically persisted the ordinal value of ITCDataFormat. Adding
        // FTXTC before Unknown shifted every later value by one, even though the
        // on-disk FTITC representation had already escaped into existing projects.
        // Prefer the original FTITC ordinals and use the saved source extension to
        // disambiguate files produced briefly after the enum was extended.
        static ITCDataFormat ParseSourceFormat(string value, string fileName)
        {
            var wireValue = IParse(value);
            var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
            var isIntegratedHeatFile = extension == ".dat" || extension == ".aff" || extension == ".dh";

            switch (wireValue)
            {
                case 0: return ITCDataFormat.ITC200;
                case 2: return ITCDataFormat.FTITC;
                case 3: return extension == ".ftxtc" ? ITCDataFormat.FTXTC : ITCDataFormat.Unknown;
                case 4: return extension == ".ta" ? ITCDataFormat.TAITC : ITCDataFormat.Unknown;
                case 5: return extension == ".ta" ? ITCDataFormat.TAITC : ITCDataFormat.IntegratedHeats;
                case 6: return isIntegratedHeatFile ? ITCDataFormat.IntegratedHeats : ITCDataFormat.PEAQITCProject;
                case 7: return ITCDataFormat.PEAQITCProject;
                case 8: return ITCDataFormat.OriginProject;
                case 9: return ITCDataFormat.NanoITC;
                default: return ITCDataFormat.Unknown;
            }
        }

        private static async Task ReadProcessor(ExperimentData exp, StreamReader reader, bool processData)
        {
            var p = new DataProcessor(exp);
            var processorLocked = false;

            string line = ReadRequiredLine(reader);
            string[] v = SplitKeyValue(line);

            if (v[0] != ProcessorType) return;

            p.InitializeBaseline((BaselineInterpolatorTypes)int.Parse(v[1]));

            while ((line = ReadRequiredLine(reader)) != EndObjectHeader)
            {
                v = SplitKeyValue(line);

                switch (v[0])
                {
                    case SplineHandleMode: (p.Interpolator as SplineInterpolator).HandleMode = (SplineInterpolator.SplineHandleMode)IParse(v[1]); break;
                    case SplineAlgorithm: (p.Interpolator as SplineInterpolator).Algorithm = (SplineInterpolator.SplineInterpolatorAlgorithm)IParse(v[1]); break;
                    case SplineShowHandles: (p.Interpolator as SplineInterpolator).ShowHandles = BParse(v[1]); break;
                    case SplineAllowPointTimeDragging: (p.Interpolator as SplineInterpolator).AllowPointTimeDragging = BParse(v[1]); break;
                    case SplinePointDensity: (p.Interpolator as SplineInterpolator).PointDensity = (SplineInterpolator.SplinePointDensity)IParse(v[1]); break;
                    case SplinePointsPerInjection: (p.Interpolator as SplineInterpolator).PointsPerInjection = IParse(v[1]); break;
                    case SplineLocked: processorLocked = BParse(v[1]); break;
                    case "LIST" when v[1] == SplinePointList: ReadSplineList(p.Interpolator as SplineInterpolator, reader); break;
                    case PolynomiumDegree:
                        {
                            if (p.Interpolator is PolynomialLeastSquaresInterpolator polynomialInterpolator) polynomialInterpolator.Degree = IParse(v[1]);
                            else if (p.Interpolator is SegmentedBaselineInterpolator segmentedInterpolator) segmentedInterpolator.Degree = IParse(v[1]);
                            break;
                        }
                    case PolynomiumLimit:
                        {
                            if (p.Interpolator is PolynomialLeastSquaresInterpolator polynomialInterpolator) polynomialInterpolator.ZLimit = DParse(v[1]);
                            break;
                        }
                    case SegmentedBaselineDegree:
                        {
                            if (p.Interpolator is SegmentedBaselineInterpolator segmentedInterpolator) segmentedInterpolator.Degree = IParse(v[1]);
                            break;
                        }
                }
            }

            exp.SetProcessor(p);

            if (processData) await p.ProcessData(replace: false, invalidate: false, showProgress: false);
            if (processorLocked) p.Lock();
        }

        static void ReadSplineList(SplineInterpolator interpolator, StreamReader reader)
        {
            var splinepoints = new List<SplineInterpolator.SplinePoint>();

            string line;

            while ((line = ReadRequiredLine(reader)) != EndListHeader)
            {
                var _spdat = SplitCsv(line);
                var splinePoint = new SplineInterpolator.SplinePoint(DParse(_spdat[0]), DParse(_spdat[1]), IParse(_spdat[2]), DParse(_spdat[3]));
                if (_spdat.Length > 4) splinePoint.Locked = BParse(_spdat[4]);
                if (_spdat.Length > 5) splinePoint.UserDefined = BParse(_spdat[5]);
                if (_spdat.Length > 6) splinePoint.SlopeLocked = BParse(_spdat[6]);
                if (_spdat.Length > 7) splinePoint.Linear = BParse(_spdat[7]);

                splinepoints.Add(splinePoint);
            }

            interpolator.SetSplinePoints(splinepoints) ;
        }

        private void ReadAttributes(ExperimentData exp, StreamReader reader)
        {
            var attributes = ReadAttributeOptions(reader);

            foreach (var att in attributes)
                exp.Attributes.Add(att);
        }

        private List<ExperimentAttribute> ReadAttributeOptions(StreamReader reader)
        {
            var options = new List<ExperimentAttribute>();

            if (interactive) AppEventHandler.Print("Reading Attributes...", 1);

            string line;
            while ((line = ReadRequiredLine(reader)) != EndListHeader)
            {
                var dat = line.Split(';');

                var opt = ExperimentAttribute.FromKey((AttributeKey)IParse(dat[1]));

                for (int i = 2; i < dat.Length; i++)
                {
                    var d = dat[i].Split(new[] { ':' }, 2);
                    string type = d[0];
                    string val = d.Length > 1 ? d[1] : string.Empty;

                    switch (type)
                    {
                        case "B": opt.BoolValue = BParse(val); break;
                        case "I": opt.IntValue = IParse(val); break;
                        case "D": opt.DoubleValue = DParse(val); break;
                        case "FWE": opt.ParameterValue = FWEParse(val); break;
                        case "S": opt.StringValue = DecodeText(val); break;
                        case "name": opt.OptionName = DecodeText(val); break;
                    }
                }

                if (interactive) AppEventHandler.Print($"{opt.Key} {opt}", 2);

                options.Add(opt);
            }

            return options;
        }

        void ReadInjectionList(ExperimentData exp, StreamReader reader)
        {
            var injections = new List<InjectionData>();

            string line;

            if (interactive) AppEventHandler.Print("Reading Injections...", 1);
            while ((line = ReadRequiredLine(reader)) != EndListHeader)
            {
                var inj = InjectionData.FromFTITCLine(exp, line);
                if (interactive) AppEventHandler.Print(inj.ToString(), 2);
                injections.Add(inj);
            }

            exp.Injections = injections;
        }

        static void ReadDataList(ExperimentData exp, StreamReader reader)
        {
            var datapoints = new List<DataPoint>();

            string line;

            while ((line = ReadRequiredLine(reader)) != EndListHeader)
            {
                var dp = SplitCsv(line);
                if (dp.Length < 3) throw new InvalidDataException("FTITC data-point rows must have at least three columns.");
                datapoints.Add(new DataPoint(FParse(dp[0]), FParse(dp[1]), FParse(dp[2])));
            }

            exp.DataPoints = datapoints;
        }

        static void ReadSegmentList(ExperimentData exp, StreamReader reader)
        {
            string line;

            while ((line = ReadRequiredLine(reader)) != EndListHeader) exp.AddSegment(TandemExperimentSegment.FromFile(line));

            exp.InvalidateSegmentLookup();
        }

        async Task<AnalysisResult> ReadAnalysisResult(StreamReader reader, string firstline)
        {
            if (interactive) AppEventHandler.PrintAndLog("Loading Analysis Result...", 1);

            try
            {
                string line = firstline;
                string[] info = firstline.Split(new[] { ':' }, 3)[2].Split(',').Select(DecodeText).ToArray();
                string comments = "";
                string dateinfo = "";
                string name = "";
                DateTime date = DateTime.Now;
                AnalysisResultValiditySnapshot validitySnapshot = null;

                while (!(line = ReadRequiredLine(reader)).Contains(GlobalSolutionHeader))
                {
                    var dat = SplitKeyValue(line);
                    string value = dat.Length > 1 ? dat[1] : string.Empty;

                    switch (dat[0])
                    {
                        case Comments: comments = DecodeText(value); break;
                        case Date: dateinfo = value; break;
                        case AssignedName: name = DecodeText(value); break;
                        case AnalysisResultValiditySnapshotData:
                            validitySnapshot = AnalysisResultValiditySnapshot.FromJson(DecodeText(value));
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(dateinfo)) date = DTParse(dateinfo);

                var statusName = !string.IsNullOrWhiteSpace(name)
                    ? name
                    : info.Length > 1 && !string.IsNullOrWhiteSpace(info[1])
                        ? info[1]
                        : "Analysis Result";

                if (interactive)
                {
                    StatusBarManager.SetSecondaryStatus($"{statusName}", 0);
                    await Task.Delay(1);
                }

                var sol = ReadGlobalSolution(reader);
                
                string guid = info[0];
                string filename = info.Length > 1 ? info[1] : sol.SolutionName;
                AnalysisResult result = new AnalysisResult(sol, captureValiditySnapshot: false);
                result.SetID(guid);
                result.SetFileName(filename);
                result.Name = name;
                result.Comments = comments;
                result.SetDate(date);
                result.SetValiditySnapshot(validitySnapshot);

                return result;
            }
            catch (Exception ex)
            {
                if (!interactive) throw new InvalidDataException("The saved analysis result is malformed.", ex);
                AppEventHandler.PrintAndLog(ex.Message);
                AppEventHandler.PrintAndLog(ex.StackTrace);
                AppEventHandler.DisplayHandledException(new HandledException(HandledException.Severity.Error,"File Reading Error", $"Analysis Result reading error.\nFile: {firstline}"));
                return null;
            }
        }

        GlobalSolution ReadGlobalSolution(StreamReader reader)
        {
            bool useErrorWeightedFitting = false;

            string line = ReadRequiredLine(reader);
            var mdl = (AnalysisModel)IParse(SplitKeyValue(line)[1]);
            GlobalModelFactory factory = new GlobalModelFactory(mdl);
            var datas = new List<ExperimentData>();
            var solutions = new List<SolutionInterface>();
            SolverConvergence legacyConv = null;
            SolverConvergence snapshotConv = null;

            while ((line = ReadRequiredLine(reader)) != EndFileHeader)
            {
                var v = line.Split(':');
                switch (v[0])
                {
                    case SolWeightedError: useErrorWeightedFitting = BParse(v[1]); break;
                    case "LIST" when v[1] == DataRef:
                        {
                            string dref;
                            while ((dref = ReadRequiredLine(reader)) != EndListHeader)
                            {
                                datas.Add(data.Find(d => d.UniqueID == dref) as ExperimentData);
                            }

                            factory.InitializeModel(datas);
                        }
                        break;
                    case "LIST" when v[1] == SolConstraints:
                        {
                            string line2;
                            while ((line2 = ReadRequiredLine(reader)) != EndListHeader)
                            {
                                var dat = line2.Split(':');
                                var par = (ParameterType)int.Parse(dat[1]);
                                var con = (VariableConstraint)IParse(dat[2]);

                                factory.Model.Parameters.SetConstraintForParameter(par, con);
                            }
                        }
                        break;
                    case "LIST" when v[1] == SolParams:
                        {
                            string line2;
                            while ((line2 = ReadRequiredLine(reader)) != EndListHeader)
                            {
                                var dat = line2.Split(':');
                                var par = (ParameterType)int.Parse(dat[1]);
                                var val = DParse(dat[2]);
                                var locked = dat.Length > 3 && BParse(dat[3]);

                                factory.Model.Parameters.AddorUpdateGlobalParameter(par, val, locked);
                            }
                        }
                        break;
                    case "LIST" when v[1] == SolutionList:
                        {
                            string solline;
                            while ((solline = ReadRequiredLine(reader)) != EndListHeader)
                            {
                                var sol = ReadSolution(reader, solline);
                                if (sol == null) break;

                                var model = factory.Model.Models.Find(mdl => mdl.Data.UniqueID == sol.Data.UniqueID);
                                if (model == null) continue;

                                // The per-solution reader reconstructs its own model instance and restores that
                                // instance's saved options. Copy those options back to the GlobalModel-owned model
                                // so result views read the persisted option values instead of freshly initialized defaults.
                                foreach (var (key, opt) in sol.Model.ModelOptions)
                                {
                                    var restored = opt.Copy();
                                    if (model.ModelOptions.ContainsKey(key))
                                        restored.OptionName = model.ModelOptions[key].OptionName;

                                    model.ModelOptions[key] = restored;
                                }

                                model.Solution = sol;

                                solutions.Add(sol);
                            }
                        }
                        break;
                    case "OBJECT" when v[1] == SolConvergence:
                        {
                            legacyConv = ReadConvergenceObject(reader);
                            ReadRequiredLine(reader);

                            break;
                        }
                    case "OBJECT" when v[1] == SolConvergenceSnapshot:
                        snapshotConv = ReadConvergenceSnapshotObject(reader);
                        break;
                    case "OBJECT" when v[1] == MdlCloneOptions:
                        {
                            var mco = new ModelCloneOptions();
                            string mcoline;
                            while ((mcoline = ReadRequiredLine(reader)) != EndObjectHeader)
                            {
                                var vv = SplitKeyValue(mcoline);
                                switch (vv[0])
                                {
                                    case SolErrorMethod: mco.ErrorEstimationMethod = (ErrorEstimationMethod)IParse(vv[1]); break;
                                    case SolCloneIsGlobal: mco.IsGlobalClone = BParse(vv[1]); break;
                                    case SolCloneConcentrationVariance: mco.IncludeConcentrationErrorsInBootstrap = BParse(vv[1]); break;
                                    case SolCloneAutoVariance: mco.EnableAutoConcentrationVariance = BParse(vv[1]); break;
                                    case SolCloneAutoVarianceValue: mco.AutoConcentrationVariance = DParse(vv[1]); break;
                                    case SolCloneUnlockParameters: mco.UnlockBootstrapParameters = BParse(vv[1]); break;
                                }
                            }

                            factory.Model.ModelCloneOptions = mco;
                        }
                        break;
                }
            }

            if (solutions.Count > 0)
                factory.Model.Solution = new GlobalSolution(new GlobalSolver()
                {
                    Model = factory.Model, ErrorEstimationMethod = solutions[0].ErrorMethod
                }, solutions, snapshotConv ?? legacyConv);

            factory.Model.Solution.UseWeightedFitting = useErrorWeightedFitting;

            return factory.Model.Solution;
        }

        SolutionInterface ReadSolution(StreamReader reader, string firstline, ExperimentData experimentData = null)
        {
            try
            {
                SingleModelFactory factory = null;
                string guid = DecodeText(firstline.Split(new[] { ':' }, 3)[2]);
                string dataref = SplitKeyValue(ReadRequiredLine(reader))[1];
                string parentID = "";
                bool useErrorWeightedFitting = false;
                var mdltype = (AnalysisModel)IParse(SplitKeyValue(ReadRequiredLine(reader))[1]);

                factory = new SingleModelFactory(mdltype);
                if (experimentData == null)
                    factory.InitializeModel(data.Find(d => d.UniqueID == dataref) as ExperimentData);
                else factory.ConstructModel(experimentData);
                SolverConvergence legacyConv = null;
                SolverConvergence snapshotConv = null;
                double reference_loss_value = double.NaN;
                List<Parameter> parameters = null;
                List<SolutionInterface> legacyBootstrapSolutions = null;
                List<BootstrapModelSnapshot> bootstrapSnapshots = null;

                string line;
                while ((line = ReadRequiredLine(reader)) != EndFileHeader)
                {
                    var v = line.Split(':');
                    switch (v[0])
                    {
                        //case SolErrorMethod: factory.Model.MCO = (ErrorEstimationMethod)IParse(v[1]); break;
                        case SolWeightedError: useErrorWeightedFitting = BParse(v[1]); break;
                        case SolParent: parentID = DecodeText(v[1]); break;
                        case SolLoss: reference_loss_value = DParse(v[1]); break;
                        case "LIST" when v[1] == SolParams:
                            parameters = new List<Parameter>();
                            string line2;
                            while ((line2 = ReadRequiredLine(reader)) != EndListHeader)
                            {
                                var dat = line2.Split(':');
                                var par = (ParameterType)int.Parse(dat[1]);
                                var val = DParse(dat[2]);
                                var locked = dat.Length > 3 && BParse(dat[3]);

                                parameters.Add(new Parameter(par, val, locked));
                            }
                            // Make the saved primary values available while the
                            // following bootstrap list is being materialized. The
                            // experiment data itself may be read later in the file,
                            // so bootstrap models must not initialize guessed values
                            // from the (currently empty) injection list.
                            foreach (var parameter in parameters)
                                factory.Model.Parameters.AddOrUpdateParameter(parameter.Copy());
                            break;
                        case "LIST" when v[1] == SolBootstrapSolutions:
                            legacyBootstrapSolutions = new List<SolutionInterface>();
                            var bsol = "";
                            while ((bsol = ReadRequiredLine(reader)) != EndListHeader)
                            {
                                legacyBootstrapSolutions.Add(ReadSolution(reader, bsol, factory.Model.Data));
                            }
                            break;
                        case "LIST" when v[1] == SolBootstrapParameters:
                            legacyBootstrapSolutions = ReadBootstrapParameterList(factory.Model, reader);
                            break;
                        case "LIST" when v[1] == SolBootstrapSnapshots:
                            bootstrapSnapshots = ReadBootstrapSnapshots(reader);
                            break;
                        case "LIST" when v[1] == MdlOptions: ReadModelOptions(factory.Model, reader); break;
                        case "OBJECT" when v[1] == SolConvergence:
                            legacyConv = ReadConvergenceObject(reader);
                            ReadRequiredLine(reader);
                            break;
                        case "OBJECT" when v[1] == SolConvergenceSnapshot:
                            snapshotConv = ReadConvergenceSnapshotObject(reader);
                            break;

                    }
                }

                foreach (var par in parameters)
                    factory.Model.Parameters.AddOrUpdateParameter(par);

                var solution = SolutionInterface.FromModel(factory.Model, snapshotConv ?? legacyConv);
                solution.UseWeightedFitting = useErrorWeightedFitting;
                solution.SetID(guid);
                if (!string.IsNullOrWhiteSpace(parentID)) solution.ParentSolutionID = parentID;

                factory.Model.Solution = solution;

                // If a loss was stored and it does not correspond to the loss calculated for the model, something changed and we invalidate the solution 
                // var currloss = solution.Model.Loss();
                // if (!double.IsNaN(reference_loss_value))
                //    if (Math.Abs(reference_loss_value - currloss) > 0.0001)
                //        solution.Invalidate();

                if (bootstrapSnapshots != null)
                {
                    var restoredSnapshots = bootstrapSnapshots
                        .OrderBy(snapshot => snapshot.ReplicateIndex)
                        .Select(snapshot => snapshot.Restore(factory.Model))
                        .ToList();
                    solution.SetBootstrapSolutions(restoredSnapshots);
                }
                else if (legacyBootstrapSolutions != null)
                {
                    solution.SetBootstrapSolutions(legacyBootstrapSolutions);
                }

                return factory.Model.Solution;
            }
            catch (Exception ex)
            {
                ex.Source = "Solution Reading Error: " + firstline;
                if (!interactive) throw new InvalidDataException("A saved fit solution is malformed.", ex);
                AppEventHandler.DisplayHandledException(ex);
                return null;
            }
        }

        private List<BootstrapModelSnapshot> ReadBootstrapSnapshots(StreamReader reader)
        {
            var snapshots = new List<BootstrapModelSnapshot>();
            var replicateIndices = new HashSet<int>();

            string line;
            while ((line = ReadRequiredLine(reader)) != EndListHeader)
            {
                var header = SplitKeyValue(line);
                if (header.Length < 2 || header[0] != "OBJECT" || header[1] != BootSnapshot)
                    throw new InvalidDataException("BootSnapshots may contain only BootSnapshot objects.");

                var snapshot = ReadBootstrapSnapshot(reader);
                if (!replicateIndices.Add(snapshot.ReplicateIndex))
                    throw new InvalidDataException($"Duplicate bootstrap replicate index {snapshot.ReplicateIndex}.");
                snapshots.Add(snapshot);
            }

            return snapshots;
        }

        private BootstrapModelSnapshot ReadBootstrapSnapshot(StreamReader reader)
        {
            var snapshot = new BootstrapModelSnapshot();
            var requiredFields = new HashSet<string>();

            string line;
            while ((line = ReadRequiredLine(reader)) != EndObjectHeader)
            {
                var value = SplitKeyValue(line);
                switch (value[0])
                {
                    case BootSnapshotVersion:
                        snapshot.Version = IParse(RequiredValue(value, BootSnapshotVersion));
                        requiredFields.Add(BootSnapshotVersion);
                        break;
                    case BootReplicateIndex:
                        snapshot.ReplicateIndex = IParse(RequiredValue(value, BootReplicateIndex));
                        requiredFields.Add(BootReplicateIndex);
                        break;
                    case BootCellConcentration:
                        snapshot.CellConcentration = FWEParse(RequiredValue(value, BootCellConcentration));
                        requiredFields.Add(BootCellConcentration);
                        break;
                    case BootSyringeConcentration:
                        snapshot.SyringeConcentration = FWEParse(RequiredValue(value, BootSyringeConcentration));
                        requiredFields.Add(BootSyringeConcentration);
                        break;
                    case BootCellVolume:
                        snapshot.CellVolume = DParse(RequiredValue(value, BootCellVolume));
                        requiredFields.Add(BootCellVolume);
                        break;
                    case BootMeasuredTemperature:
                        snapshot.MeasuredTemperature = DParse(RequiredValue(value, BootMeasuredTemperature));
                        requiredFields.Add(BootMeasuredTemperature);
                        break;
                    case "LIST" when value.Length > 1 && value[1] == BootSnapshotParameters:
                        ReadBootstrapSnapshotParameters(snapshot, reader);
                        requiredFields.Add(BootSnapshotParameters);
                        break;
                    case "LIST" when value.Length > 1 && value[1] == BootSnapshotModelOptions:
                        snapshot.ModelOptions.AddRange(ReadAttributeOptions(reader));
                        requiredFields.Add(BootSnapshotModelOptions);
                        break;
                    case "LIST" when value.Length > 1 && value[1] == BootSnapshotInjections:
                        ReadBootstrapSnapshotInjections(snapshot, reader);
                        requiredFields.Add(BootSnapshotInjections);
                        break;
                    case "LIST" when value.Length > 1 && value[1] == BootSnapshotSegments:
                        ReadBootstrapSnapshotSegments(snapshot, reader);
                        requiredFields.Add(BootSnapshotSegments);
                        break;
                    default:
                        throw new InvalidDataException($"Unknown bootstrap snapshot field '{line}'.");
                }
            }

            var required = new[]
            {
                BootSnapshotVersion,
                BootReplicateIndex,
                BootCellConcentration,
                BootSyringeConcentration,
                BootCellVolume,
                BootMeasuredTemperature,
                BootSnapshotParameters,
                BootSnapshotModelOptions,
                BootSnapshotInjections,
                BootSnapshotSegments,
            };
            var missing = required.Where(field => !requiredFields.Contains(field)).ToList();
            if (missing.Count > 0)
                throw new InvalidDataException($"Bootstrap snapshot is missing: {string.Join(", ", missing)}.");
            if (snapshot.Version != BootstrapModelSnapshot.CurrentVersion)
                throw new InvalidDataException($"Unsupported bootstrap snapshot version {snapshot.Version}.");
            if (snapshot.ReplicateIndex < 0)
                throw new InvalidDataException("Bootstrap replicate indices cannot be negative.");
            if (snapshot.Parameters.Count == 0)
                throw new InvalidDataException("Bootstrap snapshot has no fitted parameters.");
            if (snapshot.Injections.Count == 0)
                throw new InvalidDataException("Bootstrap snapshot has no injections.");

            return snapshot;
        }

        private static string RequiredValue(string[] value, string field)
        {
            if (value.Length < 2)
                throw new InvalidDataException($"Bootstrap snapshot field '{field}' has no value.");
            return value[1];
        }

        private static void ReadBootstrapSnapshotParameters(BootstrapModelSnapshot snapshot, StreamReader reader)
        {
            string line;
            while ((line = ReadRequiredLine(reader)) != EndListHeader)
            {
                var data = line.Split(':');
                if (data.Length < 3)
                    throw new InvalidDataException($"Malformed bootstrap parameter '{line}'.");

                var key = (ParameterType)IParse(data[1]);
                var parameter = new Parameter(key, DParse(data[2]), data.Length > 3 && BParse(data[3]));
                if (snapshot.Parameters.Any(existing => existing.Key == key))
                    throw new InvalidDataException($"Duplicate bootstrap parameter '{key}'.");
                snapshot.Parameters.Add(parameter);
            }
        }

        private static void ReadBootstrapSnapshotInjections(BootstrapModelSnapshot snapshot, StreamReader reader)
        {
            string line;
            while ((line = ReadRequiredLine(reader)) != EndListHeader)
            {
                var data = SplitCsv(line);
                if (data.Length != 5)
                    throw new InvalidDataException($"Malformed bootstrap injection '{line}'.");

                var injection = new BootstrapInjectionSnapshot
                {
                    ID = IParse(data[0]),
                    Include = BParse(data[1]),
                    Volume = DParse(data[2]),
                    ActualCellConcentration = DParse(data[3]),
                    ActualTitrantConcentration = DParse(data[4]),
                };
                if (injection.ID != snapshot.Injections.Count)
                    throw new InvalidDataException("Bootstrap injection IDs must match their list positions.");
                snapshot.Injections.Add(injection);
            }
        }

        private static void ReadBootstrapSnapshotSegments(BootstrapModelSnapshot snapshot, StreamReader reader)
        {
            string line;
            while ((line = ReadRequiredLine(reader)) != EndListHeader)
            {
                var data = SplitCsv(line);
                if (data.Length != 3)
                    throw new InvalidDataException($"Malformed bootstrap segment '{line}'.");

                snapshot.Segments.Add(new BootstrapSegmentSnapshot
                {
                    FirstInjectionID = IParse(data[0]),
                    InitialCellConcentration = DParse(data[1]),
                    InitialTitrantConcentration = DParse(data[2]),
                });
            }
        }

        private static List<SolutionInterface> ReadBootstrapParameterList(Model mdl, StreamReader reader)
        {
            var solutions = new List<SolutionInterface>();

            string line;
            while ((line = ReadRequiredLine(reader)) != EndListHeader)
            {
                var parameters = new List<Parameter>();
                while (line != EndListHeader)
                {
                    var dat = line.Split(':');
                    var par = (ParameterType)int.Parse(dat[1]);
                    var val = DParse(dat[2]);

                    parameters.Add(new Parameter(par, val));

                    line = ReadRequiredLine(reader);
                }

                // Each serialized bootstrap parameter set must have its own model.
                // Reusing mdl here makes every bootstrap solution evaluate the same
                // final primary model, collapsing fitted-value confidence intervals.
                // Construct the same concrete model against the experiment data.  The
                // synthetic-clone path is intended for generating new bootstrap data
                // and can depend on desktop-only state; saved bootstrap parameters
                // only need an independent parameter/model object for evaluation.
                var modelFactory = new SingleModelFactory(mdl.ModelType);
                modelFactory.ConstructModel(mdl.Data);
                var bootstrapModel = modelFactory.Model;
                bootstrapModel.ModelCloneOptions = CopyCloneOptions(mdl.ModelCloneOptions);
                bootstrapModel.ReuseAttachedSolutionInitialValues = mdl.ReuseAttachedSolutionInitialValues;
                bootstrapModel.SetModelOptions(mdl.ModelOptions);
                foreach (var parameter in mdl.Parameters.Table.Values)
                    bootstrapModel.Parameters.AddOrUpdateParameter(parameter.Copy());
                foreach (var parameter in parameters)
                    bootstrapModel.Parameters.AddOrUpdateParameter(parameter);

                var solution = SolutionInterface.FromModel(bootstrapModel, null);
                bootstrapModel.Solution = solution;
                solutions.Add(solution);
            }

            return solutions;
        }

        private static ModelCloneOptions CopyCloneOptions(ModelCloneOptions source)
        {
            if (source == null) return null;

            return new ModelCloneOptions
            {
                IsGlobalClone = source.IsGlobalClone,
                ErrorEstimationMethod = source.ErrorEstimationMethod,
                IncludeConcentrationErrorsInBootstrap = source.IncludeConcentrationErrorsInBootstrap,
                EnableAutoConcentrationVariance = source.EnableAutoConcentrationVariance,
                AutoConcentrationVariance = source.AutoConcentrationVariance,
                DiscardedDataPoint = source.DiscardedDataPoint,
                UnlockBootstrapParameters = source.UnlockBootstrapParameters,
            };
        }

        private void ReadModelOptions(Model mdl, StreamReader reader)
        {
            List<ExperimentAttribute> options = ReadAttributeOptions(reader);

            foreach (var att in options)
            {
                if (mdl.ModelOptions.ContainsKey(att.Key))
                {
                    mdl.ModelOptions[att.Key] = att;
                }
                else mdl.ModelOptions.Add(att.DictionaryEntry);
            }
        }

        private static SolverConvergence ReadConvergenceObject(StreamReader reader)
        {
            SolverConvergence conv;
            var dat = ReadRequiredLine(reader).Split(';');
            var dict = new Dictionary<string, string>();
            foreach (var d in dat.Where(s => !string.IsNullOrEmpty(s)))
            {
                var parts = SplitKeyValue(d);
                dict.Add(parts[0], parts.Length > 1 ? parts[1] : string.Empty);
            }

            conv = SolverConvergence.FromSaveLegacy(
                IParse(dict[SolIterations]),
                DParse(dict[SolLoss]),
                TSParse(dict[SolConvTime]),
                TSParse(dict[SolConvBootstrapTime]),
                (SolverAlgorithm)IParse(dict[SolConvAlgorithm]),
                DecodeText(dict[SolConvMsg]),
                BParse(dict[SolConvFailed]));
            return conv;
        }

        private static SolverConvergence ReadConvergenceSnapshotObject(StreamReader reader)
        {
            var dict = ReadObjectDictionary(reader);

            var snapshot = new SolverConvergenceSnapshot()
            {
                SchemaVersion = dict.ContainsKey(SolConvSchemaVersion)
                    ? IParse(dict[SolConvSchemaVersion])
                    : SolverConvergenceSnapshot.CurrentSchemaVersion,
                Iterations = dict.ContainsKey(SolIterations) ? IParse(dict[SolIterations]) : 0,
                Loss = dict.ContainsKey(SolLoss) ? DParse(dict[SolLoss]) : 0,
                TimeSeconds = dict.ContainsKey(SolConvTime) ? DParse(dict[SolConvTime]) : 0,
                ErrorEstimationTimeSeconds = dict.ContainsKey(SolConvBootstrapTime) ? DParse(dict[SolConvBootstrapTime]) : 0,
                Algorithm = dict.ContainsKey(SolConvAlgorithm)
                    ? (SolverAlgorithm)IParse(dict[SolConvAlgorithm])
                    : default,
                Termination = dict.ContainsKey(SolConvTermination)
                    ? (SolverTermination)IParse(dict[SolConvTermination])
                    : SolverTermination.Unknown,
                ErrorEstimationOutcome = dict.ContainsKey(SolConvErrorOutcome)
                    ? (ErrorEstimationOutcome)IParse(dict[SolConvErrorOutcome])
                    : ErrorEstimationOutcome.None,
                FailureReason = dict.ContainsKey(SolConvFailureReason) ? DecodeText(dict[SolConvFailureReason]) : string.Empty,
                ErrorEstimationSummary = dict.ContainsKey(SolConvErrorSummary) ? DecodeText(dict[SolConvErrorSummary]) : string.Empty,
            };

            return SolverConvergence.FromSnapshot(snapshot);
        }

        private static Dictionary<string, string> ReadObjectDictionary(StreamReader reader)
        {
            var dict = new Dictionary<string, string>();

            string line;
            while ((line = ReadRequiredLine(reader)) != EndObjectHeader)
            {
                int idx = line.IndexOf(':');

                if (idx < 0)
                {
                    dict[line] = string.Empty;
                    continue;
                }

                var key = line.Substring(0, idx);
                var value = line.Substring(idx + 1);

                dict[key] = value;
            }

            return dict;
        }
    }
}
