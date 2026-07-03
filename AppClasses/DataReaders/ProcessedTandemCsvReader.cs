using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AnalysisITC;
using AnalysisITC.Core.Utilities;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.DataReaders
{
    /// <summary>
    /// Reads processed tandem CSV exports containing the DP, baseline, fit and NDH
    /// XY series. Values are preserved in their exported units because this format
    /// does not contain unit or experiment metadata.
    /// </summary>
    public static class ProcessedTandemCsvReader
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const NumberStyles NumStyle = NumberStyles.Float | NumberStyles.AllowLeadingSign;

        /// <summary>
        /// Reads all series present in a processed tandem CSV export.
        /// </summary>
        public static ProcessedTandemCsvData ReadFile(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("File not found", path);

            using var reader = new StreamReader(path);
            var headerLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(headerLine))
                throw new FormatException("Processed tandem CSV file does not contain a header.");

            var columns = BuildColumnIndex(ParseCsvLine(headerLine));
            var dp = RequireSeries(columns, "DP_X", "DP_Y");
            var baseline = OptionalSeries(columns, "Baseline_X", "Baseline_Y");
            var fit = OptionalSeries(columns, "Fit_X", "Fit_Y");
            var ndh = RequireSeries(columns, "NDH_X", "NDH_Y");

            var differentialPower = new List<ProcessedTandemCsvPoint>();
            var baselinePoints = new List<ProcessedTandemCsvPoint>();
            var fitPoints = new List<ProcessedTandemCsvPoint>();
            var normalizedHeat = new List<ProcessedTandemCsvPoint>();

            string line;
            var lineNumber = 1;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var values = ParseCsvLine(line);
                AddPointIfPresent(values, dp, lineNumber, differentialPower);
                AddPointIfPresent(values, baseline, lineNumber, baselinePoints);
                AddPointIfPresent(values, fit, lineNumber, fitPoints);
                AddPointIfPresent(values, ndh, lineNumber, normalizedHeat);
            }

            if (differentialPower.Count == 0)
                throw new FormatException("Processed tandem CSV file contains no DP_X/DP_Y data.");
            if (normalizedHeat.Count == 0)
                throw new FormatException("Processed tandem CSV file contains no NDH_X/NDH_Y data.");

            return new ProcessedTandemCsvData(
                path,
                differentialPower,
                baselinePoints,
                fitPoints,
                normalizedHeat);
        }

        /// <summary>
        /// Reads multiple processed exports with the same parser. Each returned
        /// item retains its own source path and can be segmented independently.
        /// </summary>
        public static IReadOnlyList<ProcessedTandemCsvData> ReadFiles(IEnumerable<string> paths)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));

            return new ReadOnlyCollection<ProcessedTandemCsvData>(
                paths.Select(ReadFile).ToList());
        }

        /// <summary>
        /// Reconstructs the source experiments represented by a processed
        /// MicroCal tandem export. The result can be passed to a tandem
        /// concatenation implementation as ordinary experiment data.
        /// </summary>
        public static IReadOnlyList<ExperimentData> ReadExperiments(
            string path,
            ProcessedTandemCsvImportSettings settings = null)
        {
            return ReadFile(path).ReconstructExperiments(settings);
        }

        /// <summary>
        /// Reconstructs source experiments from several processed tandem
        /// exports, preserving the input-file order.
        /// </summary>
        public static IReadOnlyList<ExperimentData> ReadExperiments(
            IEnumerable<string> paths,
            ProcessedTandemCsvImportSettings settings = null)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));

            return new ReadOnlyCollection<ExperimentData>(
                paths.SelectMany(path => ReadExperiments(path, settings)).ToList());
        }

        /// <summary>
        /// Extracts the source injection series using fit points excluded from NDH
        /// as segment-start markers. MicroCal tandem exports normally exclude the
        /// first injection from every source run while retaining it in the fit.
        /// </summary>
        public static IReadOnlyList<ProcessedTandemCsvSegment> ReadPreConcatenationSegments(string path)
        {
            return ReadFile(path).ExtractPreConcatenationSegments();
        }

        /// <summary>
        /// Extracts automatically identified segments and controls whether repeated
        /// MicroCal injection sequences restore the measured NDH points to local X axes.
        /// </summary>
        public static IReadOnlyList<ProcessedTandemCsvSegment> ReadPreConcatenationSegments(
            string path,
            bool restoreLocalXAxis,
            double xTolerance = 1e-6)
        {
            return ReadFile(path).ExtractPreConcatenationSegments(restoreLocalXAxis, xTolerance);
        }

        /// <summary>
        /// Extracts caller-defined original segments, including thermogram ranges.
        /// Explicit ranges are needed for thermogram splitting because the CSV
        /// format does not store its pre-concatenation time boundaries.
        /// </summary>
        public static IReadOnlyList<ProcessedTandemCsvSegment> ReadPreConcatenationSegments(
            string path,
            IEnumerable<ProcessedTandemCsvSegmentDefinition> definitions)
        {
            return ReadFile(path).ExtractPreConcatenationSegments(definitions);
        }

        static Dictionary<string, int> BuildColumnIndex(IReadOnlyList<string> header)
        {
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < header.Count; i++)
            {
                var value = (header[i] ?? string.Empty).Trim().TrimStart('\uFEFF');
                if (!string.IsNullOrWhiteSpace(value)) columns[value] = i;
            }

            return columns;
        }

        static SeriesColumns RequireSeries(Dictionary<string, int> columns, string xName, string yName)
        {
            var series = OptionalSeries(columns, xName, yName);
            if (series == null)
                throw new FormatException($"Processed tandem CSV file is missing the required {xName}/{yName} columns.");

            return series;
        }

        static SeriesColumns OptionalSeries(Dictionary<string, int> columns, string xName, string yName)
        {
            var hasX = columns.TryGetValue(xName, out var x);
            var hasY = columns.TryGetValue(yName, out var y);

            if (hasX != hasY)
                throw new FormatException($"Processed tandem CSV file must contain both {xName} and {yName} columns.");

            return hasX ? new SeriesColumns(xName, yName, x, y) : null;
        }

        static void AddPointIfPresent(
            IReadOnlyList<string> values,
            SeriesColumns series,
            int lineNumber,
            List<ProcessedTandemCsvPoint> points)
        {
            if (series == null) return;

            var xText = GetValue(values, series.XIndex);
            var yText = GetValue(values, series.YIndex);
            var hasX = !string.IsNullOrWhiteSpace(xText);
            var hasY = !string.IsNullOrWhiteSpace(yText);

            if (!hasX && !hasY) return;
            if (hasX != hasY)
                throw new FormatException($"Line {lineNumber} contains only one value for {series.XName}/{series.YName}.");

            if (!double.TryParse(xText, NumStyle, Inv, out var x))
                throw new FormatException($"Line {lineNumber} contains invalid numeric data in {series.XName}: \"{xText}\".");
            if (!double.TryParse(yText, NumStyle, Inv, out var y))
                throw new FormatException($"Line {lineNumber} contains invalid numeric data in {series.YName}: \"{yText}\".");

            points.Add(new ProcessedTandemCsvPoint(x, y));
        }

        static string GetValue(IReadOnlyList<string> values, int index)
        {
            return index < values.Count ? values[index].Trim() : string.Empty;
        }

        static IReadOnlyList<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            var value = new StringBuilder();
            var quoted = false;

            for (var i = 0; i < line.Length; i++)
            {
                var character = line[i];

                if (character == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        value.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (character == ',' && !quoted)
                {
                    values.Add(value.ToString());
                    value.Clear();
                }
                else
                {
                    value.Append(character);
                }
            }

            if (quoted) throw new FormatException("Processed tandem CSV file contains an unterminated quoted field.");

            values.Add(value.ToString());
            return values;
        }

        sealed class SeriesColumns
        {
            public string XName { get; }
            public string YName { get; }
            public int XIndex { get; }
            public int YIndex { get; }

            public SeriesColumns(string xName, string yName, int xIndex, int yIndex)
            {
                XName = xName;
                YName = yName;
                XIndex = xIndex;
                YIndex = yIndex;
            }
        }
    }

    public sealed class ProcessedTandemCsvData
    {
        const double MinHeatForConcentrationInference = 0.3 * 4.184e-6;
        const double MinNormalizedHeatForConcentrationInference = 0.3;
        const double PeakSearchWindow = 18.0;
        const double InferenceIntegrationFraction = 0.8;

        public string SourcePath { get; }
        public IReadOnlyList<ProcessedTandemCsvPoint> DifferentialPower { get; }
        public IReadOnlyList<ProcessedTandemCsvPoint> Baseline { get; }
        public IReadOnlyList<ProcessedTandemCsvPoint> Fit { get; }
        public IReadOnlyList<ProcessedTandemCsvPoint> NormalizedHeat { get; }

        internal ProcessedTandemCsvData(
            string sourcePath,
            IList<ProcessedTandemCsvPoint> differentialPower,
            IList<ProcessedTandemCsvPoint> baseline,
            IList<ProcessedTandemCsvPoint> fit,
            IList<ProcessedTandemCsvPoint> normalizedHeat)
        {
            SourcePath = sourcePath;
            DifferentialPower = new ReadOnlyCollection<ProcessedTandemCsvPoint>(differentialPower);
            Baseline = new ReadOnlyCollection<ProcessedTandemCsvPoint>(baseline);
            Fit = new ReadOnlyCollection<ProcessedTandemCsvPoint>(fit);
            NormalizedHeat = new ReadOnlyCollection<ProcessedTandemCsvPoint>(normalizedHeat);
        }

        /// <summary>
        /// Converts this tandem export into its individual input experiments.
        /// DP_X is interpreted as minutes, DP_Y as microcalories/second, and
        /// NDH_Y as kilocalories/mole, as defined by the MicroCal plot export.
        /// </summary>
        public IReadOnlyList<ExperimentData> ReconstructExperiments(
            ProcessedTandemCsvImportSettings settings = null,
            double xTolerance = 1e-6)
        {
            settings ??= new ProcessedTandemCsvImportSettings();
            settings.Validate();

            if (Fit.Count == 0)
                throw new FormatException("Experiment reconstruction requires Fit_X/Fit_Y points.");
            if (xTolerance <= 0 || double.IsNaN(xTolerance))
                throw new ArgumentOutOfRangeException(nameof(xTolerance));

            var segments = ExtractPreConcatenationSegments(restoreLocalXAxis: false, xTolerance: xTolerance);
            var thermogramCounts = ResolveThermogramPointCounts(segments.Count, settings);
            var temperature = ResolveTargetTemperature(settings);
            var contexts = BuildSegmentContexts(segments, thermogramCounts, temperature, xTolerance);
            var peakOffset = settings.PeakTimeOffset
                ?? InferPeakOffset(contexts.FirstOrDefault(), settings);
            foreach (var context in contexts)
            {
                context.FirstInjectionTime = InferFirstInjectionTime(context, peakOffset, settings);
            }

            var metadata = ResolveImportMetadata(settings, contexts);
            var injectionVolumes = InferInjectionVolumes(metadata.CellVolume, metadata.ConcentrationRatio);
            var experiments = new List<ExperimentData>();

            foreach (var context in contexts)
            {
                var segment = context.Segment;
                var experiment = new ExperimentData(segment.Name + ".csv")
                {
                    Name = segment.Name,
                    Instrument = settings.Instrument,
                    DataSourceFormat = ITCDataFormat.ITC200,
                    SyringeConcentration = new FloatWithError(metadata.SyringeConcentration),
                    CellConcentration = new FloatWithError(metadata.CellConcentration),
                    CellVolume = metadata.CellVolume,
                    TargetTemperature = temperature,
                    InitialDelay = context.FirstInjectionTime,
                    DataPoints = context.LocalPoints,
                    BaseLineCorrectedDataPoints = context.LocalPoints.Select(point => point.Copy()).ToList(),
                    Date = File.GetCreationTime(SourcePath),
                    Comments = BuildImportComment(metadata),
                };

                for (var injectionIndex = 0; injectionIndex < segment.ConcatenatedFit.Count; injectionIndex++)
                {
                    var tandemRatio = segment.ConcatenatedFit[injectionIndex].X;
                    var hasMeasuredHeat = TryFindMeasuredHeat(
                        segment.ConcatenatedNormalizedHeat,
                        tandemRatio,
                        xTolerance,
                        out var normalizedHeat);
                    var injection = InjectionData.FromPEAQFile(
                        experiment,
                        injectionIndex,
                        hasMeasuredHeat,
                        context.FirstInjectionTime + settings.InjectionSpacing * injectionIndex,
                        injectionVolumes[context.FitStartIndex + injectionIndex],
                        settings.InjectionSpacing,
                        settings.InjectionDuration,
                        temperature);

                    injection.InitializeIntegrationTimes();
                    experiment.Injections.Add(injection);

                    var area = hasMeasuredHeat
                        ? Energy.ConvertToJoule(normalizedHeat, EnergyUnit.KCal) * injection.InjectionMass
                        : 0.0;
                    injection.SetPeakArea(new FloatWithError(area, 0));
                }

                RawDataReader.ProcessInjectionsMicroCal(experiment);
                RawDataReader.ProcessExperiment(experiment);
                experiment.CalculateExperimentHeatDirection();
                experiment.Processor.BaselineCompleted = settings.MarkAsBaselineCorrected;

                experiments.Add(experiment);
            }

            return new ReadOnlyCollection<ExperimentData>(experiments);
        }

        List<ReconstructionSegmentContext> BuildSegmentContexts(
            IReadOnlyList<ProcessedTandemCsvSegment> segments,
            IReadOnlyList<int> thermogramCounts,
            double temperature,
            double xTolerance)
        {
            var contexts = new List<ReconstructionSegmentContext>();
            var fitIndex = 0;
            var dpIndex = 0;

            for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                var segment = segments[segmentIndex];
                var pointCount = thermogramCounts[segmentIndex];
                var localPoints = BuildLocalThermogram(dpIndex, pointCount, temperature);
                var measuredHeatByInjection = BuildMeasuredHeatLookup(segment, xTolerance);

                contexts.Add(new ReconstructionSegmentContext(
                    segment,
                    fitIndex,
                    localPoints,
                    measuredHeatByInjection));

                fitIndex += segment.ConcatenatedFit.Count;
                dpIndex += pointCount;
            }

            return contexts;
        }

        List<DataPoint> BuildLocalThermogram(int startIndex, int count, double temperature)
        {
            if (startIndex < 0 || count <= 0 || startIndex + count > DifferentialPower.Count)
                throw new FormatException("A reconstructed thermogram segment falls outside the imported DP series.");

            var sourceStartMinutes = DifferentialPower[0].X;
            var segmentStartMinutes = DifferentialPower[startIndex].X;

            return DifferentialPower
                .Skip(startIndex)
                .Take(count)
                .Select(point => new DataPoint(
                    time: (float)((point.X - segmentStartMinutes + sourceStartMinutes) * 60.0),
                    power: (float)Energy.ConvertToJoule(point.Y, EnergyUnit.MicroCal),
                    temp: (float)temperature))
                .ToList();
        }

        IReadOnlyList<int> ResolveThermogramPointCounts(
            int segmentCount,
            ProcessedTandemCsvImportSettings settings)
        {
            if (settings.ThermogramPointCounts != null)
            {
                var requested = settings.ThermogramPointCounts.ToList();
                if (requested.Count != segmentCount || requested.Any(count => count <= 0))
                    throw new ArgumentException("Thermogram point counts must contain one positive count per source segment.");
                if (requested.Sum() != DifferentialPower.Count)
                    throw new ArgumentException("Thermogram point counts must cover every imported DP point.");

                return requested;
            }

            return Enumerable.Range(0, segmentCount)
                .Select(index =>
                    ((index + 1) * DifferentialPower.Count / segmentCount)
                    - (index * DifferentialPower.Count / segmentCount))
                .ToList();
        }

        IReadOnlyList<double> InferInjectionVolumes(double cellVolume, double concentrationRatio)
        {
            var previousFractionalVolume = 0.0;
            var volumes = new List<double>();

            foreach (var point in Fit)
            {
                // MicroCal ratio = Cs/Cm * d * (1 + d/2), where d is total delivered volume / cell volume.
                var fractionalVolume = FractionalVolume(point.X, concentrationRatio);
                var injectionVolume = cellVolume * (fractionalVolume - previousFractionalVolume);

                if (injectionVolume <= 0 || double.IsNaN(injectionVolume))
                    throw new FormatException("Fit_X does not describe a strictly increasing MicroCal injection sequence.");

                volumes.Add(RoundMicroliters(injectionVolume, 2));
                previousFractionalVolume = fractionalVolume;
            }

            return volumes;
        }

        static double RoundMicroliters(double liters, int digits)
        {
            return Math.Round(liters * 1e6, digits) / 1e6;
        }

        ResolvedImportMetadata ResolveImportMetadata(
            ProcessedTandemCsvImportSettings settings,
            IReadOnlyList<ReconstructionSegmentContext> contexts)
        {
            var knownConcentrationRatio = ResolveKnownConcentrationRatio(settings);
            var preliminaryCellVolume = settings.CellVolume
                ?? (knownConcentrationRatio.HasValue
                    ? ResolveCellVolume(settings, contexts, knownConcentrationRatio.Value)
                    : ResolveInstrumentCellVolume(settings));
            var observations = BuildConcentrationObservations(settings, contexts);
            var concentrationRatio = knownConcentrationRatio
                ?? ResolveConcentrationRatio(settings, observations, preliminaryCellVolume);
            var cellVolume = settings.CellVolume
                ?? ResolveCellVolume(settings, contexts, concentrationRatio);
            var syringeConcentration = settings.SyringeConcentration;
            var cellConcentration = settings.CellConcentration;

            if (!syringeConcentration.HasValue && observations.Count >= 4)
            {
                var volumes = InferInjectionVolumes(cellVolume, concentrationRatio);
                var estimates = observations
                    .Select(observation => observation.InjectionMass / volumes[observation.FitIndex])
                    .Where(value => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value))
                    .ToList();

                if (estimates.Count >= 4)
                    syringeConcentration = Median(estimates);
            }

            if (!syringeConcentration.HasValue && cellConcentration.HasValue)
                syringeConcentration = cellConcentration.Value * concentrationRatio;
            if (!cellConcentration.HasValue && syringeConcentration.HasValue)
                cellConcentration = syringeConcentration.Value / concentrationRatio;

            if (!syringeConcentration.HasValue || !cellConcentration.HasValue)
                throw new FormatException("Could not infer stock concentrations from the processed tandem CSV.");

            return new ResolvedImportMetadata(
                settings.Instrument,
                cellVolume,
                cellConcentration.Value,
                syringeConcentration.Value,
                concentrationRatio);
        }

        static double? ResolveKnownConcentrationRatio(ProcessedTandemCsvImportSettings settings)
        {
            if (settings.SyringeConcentration.HasValue && settings.CellConcentration.HasValue)
                return settings.SyringeConcentration.Value / settings.CellConcentration.Value;

            return null;
        }

        static double ResolveInstrumentCellVolume(ProcessedTandemCsvImportSettings settings)
        {
            if (settings.Instrument != ITCInstrument.Unknown)
                return settings.Instrument.GetProperties().StandardCellVolume;

            throw new FormatException("Cell volume is not present in the CSV. Set an instrument or provide CellVolume.");
        }

        static double ResolveCellVolume(
            ProcessedTandemCsvImportSettings settings,
            IReadOnlyList<ReconstructionSegmentContext> contexts,
            double concentrationRatio)
        {
            if (settings.RegularInjectionVolume.HasValue)
                return InferCellVolumeFromRegularInjectionVolume(
                    contexts,
                    concentrationRatio,
                    settings.RegularInjectionVolume.Value);

            return ResolveInstrumentCellVolume(settings);
        }

        static double InferCellVolumeFromRegularInjectionVolume(
            IReadOnlyList<ReconstructionSegmentContext> contexts,
            double concentrationRatio,
            double regularInjectionVolume)
        {
            var estimates = new List<double>();

            foreach (var context in contexts)
            {
                for (var injectionIndex = 1; injectionIndex < context.Segment.ConcatenatedFit.Count; injectionIndex++)
                {
                    var previous = FractionalVolume(
                        context.Segment.ConcatenatedFit[injectionIndex - 1].X,
                        concentrationRatio);
                    var current = FractionalVolume(
                        context.Segment.ConcatenatedFit[injectionIndex].X,
                        concentrationRatio);
                    var delta = current - previous;

                    if (delta > 0 && !double.IsNaN(delta) && !double.IsInfinity(delta))
                        estimates.Add(regularInjectionVolume / delta);
                }
            }

            if (estimates.Count == 0)
                throw new FormatException("Could not infer cell volume from the regular injection volume.");

            return Median(estimates);
        }

        static double FractionalVolume(double fitX, double concentrationRatio)
        {
            return Math.Sqrt(1.0 + 2.0 * fitX / concentrationRatio) - 1.0;
        }

        double ResolveConcentrationRatio(
            ProcessedTandemCsvImportSettings settings,
            IReadOnlyList<ConcentrationObservation> observations,
            double cellVolume)
        {
            var knownConcentrationRatio = ResolveKnownConcentrationRatio(settings);
            if (knownConcentrationRatio.HasValue) return knownConcentrationRatio.Value;

            if (observations.Count < 4)
                return settings.FallbackConcentrationRatio;

            var bestRatio = settings.FallbackConcentrationRatio;
            var bestScore = double.PositiveInfinity;
            var minLog = Math.Log(settings.MinimumConcentrationRatio);
            var maxLog = Math.Log(settings.MaximumConcentrationRatio);

            for (var logRatio = minLog; logRatio <= maxLog; logRatio += 0.002)
            {
                var ratio = Math.Exp(logRatio);
                var score = ScoreConcentrationRatio(observations, cellVolume, ratio);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestRatio = ratio;
                }
            }

            return bestRatio;
        }

        double ScoreConcentrationRatio(
            IReadOnlyList<ConcentrationObservation> observations,
            double cellVolume,
            double concentrationRatio)
        {
            var volumes = InferInjectionVolumes(cellVolume, concentrationRatio);
            var logConcentrations = new List<double>();

            foreach (var observation in observations)
            {
                var volume = volumes[observation.FitIndex];
                var concentration = observation.InjectionMass / volume;
                if (concentration > 0 && !double.IsNaN(concentration) && !double.IsInfinity(concentration))
                    logConcentrations.Add(Math.Log(concentration));
            }

            if (logConcentrations.Count < 4) return double.PositiveInfinity;

            var mean = logConcentrations.Average();
            return logConcentrations.Sum(value => Math.Pow(value - mean, 2)) / logConcentrations.Count;
        }

        List<ConcentrationObservation> BuildConcentrationObservations(
            ProcessedTandemCsvImportSettings settings,
            IReadOnlyList<ReconstructionSegmentContext> contexts)
        {
            var observations = new List<ConcentrationObservation>();

            foreach (var context in contexts)
            {
                foreach (var item in context.MeasuredHeatByInjection)
                {
                    var injectionIndex = item.Key;
                    var normalizedHeatKCalPerMol = item.Value;
                    var start = context.FirstInjectionTime + settings.InjectionSpacing * injectionIndex;
                    var end = start + settings.InjectionSpacing * InferenceIntegrationFraction;
                    var heat = IntegratePower(context.LocalPoints, start, end);
                    var enthalpy = Energy.ConvertToJoule(normalizedHeatKCalPerMol, EnergyUnit.KCal);

                    if (Math.Abs(heat) < MinHeatForConcentrationInference) continue;
                    if (Math.Abs(normalizedHeatKCalPerMol) < MinNormalizedHeatForConcentrationInference) continue;
                    if (Math.Abs(enthalpy) <= double.Epsilon) continue;

                    var injectionMass = heat / enthalpy;
                    if (injectionMass <= 0 || double.IsNaN(injectionMass) || double.IsInfinity(injectionMass)) continue;

                    observations.Add(new ConcentrationObservation(
                        context.FitStartIndex + injectionIndex,
                        injectionMass));
                }
            }

            return observations;
        }

        static double IntegratePower(IReadOnlyList<DataPoint> points, double start, double end)
        {
            var area = 0.0;
            DataPoint? previous = null;

            foreach (var point in points)
            {
                if (point.Time <= start || point.Time >= end) continue;

                if (previous.HasValue)
                    area += point.Power * (point.Time - previous.Value.Time);

                previous = point;
            }

            return area;
        }

        Dictionary<int, double> BuildMeasuredHeatLookup(
            ProcessedTandemCsvSegment segment,
            double xTolerance)
        {
            var lookup = new Dictionary<int, double>();

            foreach (var point in segment.ConcatenatedNormalizedHeat)
            {
                var injectionIndex = FindMatchingXIndex(segment.ConcatenatedFit, point.X, xTolerance);
                if (injectionIndex >= 0) lookup[injectionIndex] = point.Y;
            }

            return lookup;
        }

        double InferPeakOffset(
            ReconstructionSegmentContext context,
            ProcessedTandemCsvImportSettings settings)
        {
            if (context == null || context.MeasuredHeatByInjection.Count == 0)
                return 12.0;

            return InferPeakPhase(context, settings) - settings.FirstInjectionTime;
        }

        double InferFirstInjectionTime(
            ReconstructionSegmentContext context,
            double peakOffset,
            ProcessedTandemCsvImportSettings settings)
        {
            var peakPhase = InferPeakPhase(context, settings);
            return Math.Max(0.0, peakPhase - peakOffset);
        }

        double InferPeakPhase(
            ReconstructionSegmentContext context,
            ProcessedTandemCsvImportSettings settings)
        {
            var direction = HeatDirection(context);
            var bestScore = double.NegativeInfinity;
            var bestPhase = settings.FirstInjectionTime + (settings.PeakTimeOffset ?? 12.0);

            for (var phase = 0.0; phase < settings.InjectionSpacing; phase += 1.0)
            {
                var score = 0.0;
                foreach (var injectionIndex in context.MeasuredHeatByInjection.Keys)
                {
                    var expectedPeakTime = phase + settings.InjectionSpacing * injectionIndex;
                    var peakPower = MaxSignedPowerNear(context.LocalPoints, expectedPeakTime, direction);
                    if (peakPower > 0) score += peakPower;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestPhase = phase;
                }
            }

            if (bestScore <= 0) return bestPhase;

            return RefinePeakPhase(context, bestPhase, direction, settings);
        }

        double RefinePeakPhase(
            ReconstructionSegmentContext context,
            double roughPhase,
            double direction,
            ProcessedTandemCsvImportSettings settings)
        {
            var estimates = new List<double>();

            foreach (var injectionIndex in context.MeasuredHeatByInjection.Keys)
            {
                var expectedPeakTime = roughPhase + settings.InjectionSpacing * injectionIndex;
                var peakTime = FindPeakTimeNear(context.LocalPoints, expectedPeakTime, direction);
                if (peakTime.HasValue)
                    estimates.Add(peakTime.Value - settings.InjectionSpacing * injectionIndex);
            }

            return estimates.Count > 0 ? Median(estimates) : roughPhase;
        }

        static double MaxSignedPowerNear(
            IReadOnlyList<DataPoint> points,
            double expectedTime,
            double direction)
        {
            var best = double.NegativeInfinity;
            foreach (var point in points)
            {
                if (Math.Abs(point.Time - expectedTime) > PeakSearchWindow) continue;
                best = Math.Max(best, direction * point.Power);
            }

            return best;
        }

        static double? FindPeakTimeNear(
            IReadOnlyList<DataPoint> points,
            double expectedTime,
            double direction)
        {
            double? bestTime = null;
            var bestPower = double.NegativeInfinity;
            var window = Math.Max(30.0, PeakSearchWindow);

            foreach (var point in points)
            {
                if (Math.Abs(point.Time - expectedTime) > window) continue;

                var signedPower = direction * point.Power;
                if (signedPower > bestPower)
                {
                    bestPower = signedPower;
                    bestTime = point.Time;
                }
            }

            return bestPower > 0 ? bestTime : null;
        }

        static double HeatDirection(ReconstructionSegmentContext context)
        {
            var values = context.MeasuredHeatByInjection.Values.ToList();
            if (values.Count == 0) return 1.0;

            return values.Average() >= 0 ? 1.0 : -1.0;
        }

        static double Median(List<double> values)
        {
            if (values == null || values.Count == 0)
                throw new ArgumentException("Cannot calculate a median from an empty list.", nameof(values));

            values.Sort();
            var middle = values.Count / 2;

            if (values.Count % 2 == 1) return values[middle];
            return 0.5 * (values[middle - 1] + values[middle]);
        }

        double ResolveTargetTemperature(ProcessedTandemCsvImportSettings settings)
        {
            if (settings.TargetTemperature.HasValue) return settings.TargetTemperature.Value;

            var directoryName = Directory.GetParent(SourcePath)?.Name ?? string.Empty;
            var match = Regex.Match(directoryName, @"(?<temperature>[+-]?\d+(?:[.,]\d+)?)\s*C\b", RegexOptions.IgnoreCase);
            if (match.Success
                && double.TryParse(
                    match.Groups["temperature"].Value.Replace(',', '.'),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed;
            }

            return AppSettings.ReferenceTemperature;
        }

        string BuildImportComment(ResolvedImportMetadata metadata)
        {
            return "Reconstructed from: " + Path.GetFileName(SourcePath);
        }

        static bool TryFindMeasuredHeat(
            IReadOnlyList<ProcessedTandemCsvPoint> points,
            double ratio,
            double tolerance,
            out double normalizedHeat)
        {
            foreach (var point in points)
            {
                if (Math.Abs(point.X - ratio) <= tolerance)
                {
                    normalizedHeat = point.Y;
                    return true;
                }
            }

            normalizedHeat = 0.0;
            return false;
        }

        /// <summary>
        /// Extracts raw and integrated points belonging to each original segment.
        /// The caller supplies thermogram ranges because they are not represented
        /// explicitly in the exported CSV.
        /// </summary>
        public IReadOnlyList<ProcessedTandemCsvSegment> ExtractPreConcatenationSegments(
            IEnumerable<ProcessedTandemCsvSegmentDefinition> definitions)
        {
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));

            var segments = new List<ProcessedTandemCsvSegment>();
            foreach (var definition in definitions)
            {
                if (definition == null) throw new ArgumentException("A segment definition cannot be null.", nameof(definitions));
                if (definition.NormalizedHeatStartIndex + definition.NormalizedHeatCount > NormalizedHeat.Count)
                    throw new ArgumentOutOfRangeException(nameof(definitions), "A segment requests normalized-heat points outside the source data.");

                var differentialPower = DifferentialPower
                    .Where(definition.ContainsThermogramX)
                    .ToList();
                var baseline = Baseline
                    .Where(definition.ContainsThermogramX)
                    .ToList();
                var normalizedHeat = NormalizedHeat
                    .Skip(definition.NormalizedHeatStartIndex)
                    .Take(definition.NormalizedHeatCount)
                    .ToList();

                segments.Add(new ProcessedTandemCsvSegment(
                    definition.Name,
                    differentialPower,
                    baseline,
                    new List<ProcessedTandemCsvPoint>(),
                    normalizedHeat,
                    new List<ProcessedTandemCsvPoint>(),
                    normalizedHeat));
            }

            return new ReadOnlyCollection<ProcessedTandemCsvSegment>(segments);
        }

        /// <summary>
        /// Automatically separates the injection series of MicroCal tandem
        /// exports. The returned segments intentionally contain no thermogram
        /// points, as the export provides no unambiguous time boundary metadata.
        /// By default, the measured NDH X axis is restored from the repeated
        /// first-segment injection sequence; this requires identical source-run
        /// injection protocols and concentrations. Fit remains the exported tandem
        /// fit and is not reinterpreted as an independent source-run fit.
        /// </summary>
        public IReadOnlyList<ProcessedTandemCsvSegment> ExtractPreConcatenationSegments(
            bool restoreLocalXAxis = true,
            double xTolerance = 1e-6)
        {
            if (Fit.Count == 0)
                throw new FormatException("Automatic tandem separation requires Fit_X/Fit_Y points.");
            if (xTolerance <= 0 || double.IsNaN(xTolerance))
                throw new ArgumentOutOfRangeException(nameof(xTolerance));

            var starts = DetectSegmentStartIndices(xTolerance);
            var names = InferSegmentNames(starts.Count);
            var segments = new List<ProcessedTandemCsvSegment>();
            var referenceFit = Fit.Take(starts.Count > 1 ? starts[1] : Fit.Count).ToList();

            for (var segmentIndex = 0; segmentIndex < starts.Count; segmentIndex++)
            {
                var start = starts[segmentIndex];
                var end = segmentIndex + 1 < starts.Count ? starts[segmentIndex + 1] : Fit.Count;
                var concatenatedFit = Fit.Skip(start).Take(end - start).ToList();
                var firstX = concatenatedFit.First().X;
                var endX = end < Fit.Count ? Fit[end].X : double.PositiveInfinity;
                var concatenatedNormalizedHeat = NormalizedHeat
                    .Where(point => point.X >= firstX - xTolerance && point.X < endX - xTolerance)
                    .ToList();
                var normalizedHeat = concatenatedNormalizedHeat;

                if (restoreLocalXAxis)
                {
                    if (concatenatedFit.Count != referenceFit.Count)
                        throw new FormatException("Cannot restore local X axes because source segments do not use the same injection count.");

                    normalizedHeat = concatenatedNormalizedHeat
                        .Select(point =>
                        {
                            var ordinal = FindMatchingXIndex(concatenatedFit, point.X, xTolerance);
                            if (ordinal < 0)
                                throw new FormatException("An NDH_X point could not be matched to its segment fit series.");

                            return new ProcessedTandemCsvPoint(referenceFit[ordinal].X, point.Y);
                        })
                        .ToList();
                }

                segments.Add(new ProcessedTandemCsvSegment(
                    names[segmentIndex],
                    new List<ProcessedTandemCsvPoint>(),
                    new List<ProcessedTandemCsvPoint>(),
                    concatenatedFit,
                    normalizedHeat,
                    concatenatedFit,
                    concatenatedNormalizedHeat));
            }

            return new ReadOnlyCollection<ProcessedTandemCsvSegment>(segments);
        }

        List<int> DetectSegmentStartIndices(double xTolerance)
        {
            var excludedFitIndices = Fit
                .Select((point, index) => new { point, index })
                .Where(fitPoint => !NormalizedHeat.Any(observed => Math.Abs(observed.X - fitPoint.point.X) <= xTolerance))
                .Select(fitPoint => fitPoint.index)
                .ToList();

            if (!excludedFitIndices.Contains(0))
                throw new FormatException("Could not identify the excluded first injection of the first source run.");

            var positiveSteps = Fit
                .Zip(Fit.Skip(1), (left, right) => right.X - left.X)
                .Where(step => step > xTolerance)
                .OrderBy(step => step)
                .ToList();
            if (positiveSteps.Count == 0)
                throw new FormatException("Fit_X values do not contain a usable injection progression.");

            var medianStep = positiveSteps[positiveSteps.Count / 2];
            var starts = excludedFitIndices
                .Where(index => index == 0 || Fit[index].X - Fit[index - 1].X < medianStep * 0.5)
                .ToList();

            if (starts.Count == 0)
                throw new FormatException("Could not identify any pre-concatenation segment starts.");

            return starts;
        }

        static int FindMatchingXIndex(IReadOnlyList<ProcessedTandemCsvPoint> points, double x, double tolerance)
        {
            for (var index = 0; index < points.Count; index++)
            {
                if (Math.Abs(points[index].X - x) <= tolerance) return index;
            }

            return -1;
        }

        List<string> InferSegmentNames(int segmentCount)
        {
            var sourceName = Path.GetFileNameWithoutExtension(SourcePath);
            var experimentPart = sourceName.Split('_').FirstOrDefault() ?? sourceName;
            var names = experimentPart.Split('+').ToList();

            if (names.Count == segmentCount)
            {
                var prefix = new string(names[0].TakeWhile(character => !char.IsDigit(character)).ToArray());
                var firstNumberWidth = names[0].Skip(prefix.Length).TakeWhile(char.IsDigit).Count();
                if (!string.IsNullOrEmpty(prefix))
                {
                    for (var index = 1; index < names.Count; index++)
                    {
                        if (names[index].All(char.IsDigit))
                            names[index] = prefix + names[index].PadLeft(firstNumberWidth, '0');
                    }
                }

                return names;
            }

            return Enumerable.Range(1, segmentCount)
                .Select(index => $"Segment {index}")
                .ToList();
        }

        sealed class ReconstructionSegmentContext
        {
            public ProcessedTandemCsvSegment Segment { get; }
            public int FitStartIndex { get; }
            public List<DataPoint> LocalPoints { get; }
            public Dictionary<int, double> MeasuredHeatByInjection { get; }
            public double FirstInjectionTime { get; set; }

            public ReconstructionSegmentContext(
                ProcessedTandemCsvSegment segment,
                int fitStartIndex,
                List<DataPoint> localPoints,
                Dictionary<int, double> measuredHeatByInjection)
            {
                Segment = segment;
                FitStartIndex = fitStartIndex;
                LocalPoints = localPoints;
                MeasuredHeatByInjection = measuredHeatByInjection;
            }
        }

        readonly struct ConcentrationObservation
        {
            public int FitIndex { get; }
            public double InjectionMass { get; }

            public ConcentrationObservation(int fitIndex, double injectionMass)
            {
                FitIndex = fitIndex;
                InjectionMass = injectionMass;
            }
        }

        readonly struct ResolvedImportMetadata
        {
            public ITCInstrument Instrument { get; }
            public double CellVolume { get; }
            public double CellConcentration { get; }
            public double SyringeConcentration { get; }
            public double ConcentrationRatio { get; }

            public ResolvedImportMetadata(
                ITCInstrument instrument,
                double cellVolume,
                double cellConcentration,
                double syringeConcentration,
                double concentrationRatio)
            {
                Instrument = instrument;
                CellVolume = cellVolume;
                CellConcentration = cellConcentration;
                SyringeConcentration = syringeConcentration;
                ConcentrationRatio = concentrationRatio;
            }
        }
    }

    /// <summary>
    /// Settings for reconstructing source experiments from a processed tandem
    /// CSV. The export does not retain metadata directly, so the reader infers
    /// concentrations from the processed thermogram unless values are supplied
    /// here. When the regular injection volume is supplied, cell volume is
    /// inferred from the MicroCal fit X progression.
    /// </summary>
    public sealed class ProcessedTandemCsvImportSettings
    {
        public ITCInstrument Instrument { get; set; } = ITCInstrument.MalvernITC200;
        public double? CellVolume { get; set; } = 204.7e-6;
        public double? CellConcentration { get; set; }
        public double? SyringeConcentration { get; set; }
        public double? RegularInjectionVolume { get; set; }
        public double? TargetTemperature { get; set; }
        public double FirstInjectionTime { get; set; } = 60.0;
        public double InjectionSpacing { get; set; } = 180.0;
        public double InjectionDuration { get; set; } = 4.0;
        public double? PeakTimeOffset { get; set; }
        public double FallbackConcentrationRatio { get; set; } = 8.0;
        public double MinimumConcentrationRatio { get; set; } = 0.5;
        public double MaximumConcentrationRatio { get; set; } = 50.0;
        public bool MarkAsBaselineCorrected { get; set; } = true;

        /// <summary>
        /// Optional DP point count for each source run. When omitted, the
        /// concatenated thermogram is divided into equally timed source runs.
        /// </summary>
        public IReadOnlyList<int> ThermogramPointCounts { get; set; }

        internal void Validate()
        {
            if (CellVolume.HasValue && (CellVolume.Value <= 0 || double.IsNaN(CellVolume.Value)))
                throw new ArgumentOutOfRangeException(nameof(CellVolume));
            if (CellConcentration.HasValue && (CellConcentration.Value <= 0 || double.IsNaN(CellConcentration.Value)))
                throw new ArgumentOutOfRangeException(nameof(CellConcentration));
            if (SyringeConcentration.HasValue && (SyringeConcentration.Value <= 0 || double.IsNaN(SyringeConcentration.Value)))
                throw new ArgumentOutOfRangeException(nameof(SyringeConcentration));
            if (RegularInjectionVolume.HasValue && (RegularInjectionVolume.Value <= 0 || double.IsNaN(RegularInjectionVolume.Value)))
                throw new ArgumentOutOfRangeException(nameof(RegularInjectionVolume));
            if (FirstInjectionTime < 0 || double.IsNaN(FirstInjectionTime))
                throw new ArgumentOutOfRangeException(nameof(FirstInjectionTime));
            if (InjectionSpacing <= 0 || double.IsNaN(InjectionSpacing))
                throw new ArgumentOutOfRangeException(nameof(InjectionSpacing));
            if (InjectionDuration < 0 || double.IsNaN(InjectionDuration))
                throw new ArgumentOutOfRangeException(nameof(InjectionDuration));
            if (PeakTimeOffset.HasValue && double.IsNaN(PeakTimeOffset.Value))
                throw new ArgumentOutOfRangeException(nameof(PeakTimeOffset));
            if (FallbackConcentrationRatio <= 0 || double.IsNaN(FallbackConcentrationRatio))
                throw new ArgumentOutOfRangeException(nameof(FallbackConcentrationRatio));
            if (MinimumConcentrationRatio <= 0 || double.IsNaN(MinimumConcentrationRatio))
                throw new ArgumentOutOfRangeException(nameof(MinimumConcentrationRatio));
            if (MaximumConcentrationRatio <= MinimumConcentrationRatio || double.IsNaN(MaximumConcentrationRatio))
                throw new ArgumentOutOfRangeException(nameof(MaximumConcentrationRatio));
        }
    }

    public sealed class ProcessedTandemCsvSegmentDefinition
    {
        public string Name { get; }
        public double ThermogramStartX { get; }
        public double ThermogramEndX { get; }
        public int NormalizedHeatStartIndex { get; }
        public int NormalizedHeatCount { get; }

        /// <param name="thermogramStartX">Inclusive lower bound in the exported DP_X unit.</param>
        /// <param name="thermogramEndX">Exclusive upper bound in the exported DP_X unit.</param>
        /// <param name="normalizedHeatStartIndex">Zero-based starting NDH row.</param>
        public ProcessedTandemCsvSegmentDefinition(
            string name,
            double thermogramStartX,
            double thermogramEndX,
            int normalizedHeatStartIndex,
            int normalizedHeatCount)
        {
            if (double.IsNaN(thermogramStartX) || double.IsNaN(thermogramEndX) || thermogramEndX <= thermogramStartX)
                throw new ArgumentOutOfRangeException(nameof(thermogramEndX), "Thermogram end must be greater than its start.");
            if (normalizedHeatStartIndex < 0) throw new ArgumentOutOfRangeException(nameof(normalizedHeatStartIndex));
            if (normalizedHeatCount < 0) throw new ArgumentOutOfRangeException(nameof(normalizedHeatCount));

            Name = name ?? string.Empty;
            ThermogramStartX = thermogramStartX;
            ThermogramEndX = thermogramEndX;
            NormalizedHeatStartIndex = normalizedHeatStartIndex;
            NormalizedHeatCount = normalizedHeatCount;
        }

        internal bool ContainsThermogramX(ProcessedTandemCsvPoint point)
        {
            return point.X >= ThermogramStartX && point.X < ThermogramEndX;
        }
    }

    public sealed class ProcessedTandemCsvSegment
    {
        public string Name { get; }
        public IReadOnlyList<ProcessedTandemCsvPoint> DifferentialPower { get; }
        public IReadOnlyList<ProcessedTandemCsvPoint> Baseline { get; }
        /// <summary>Exported tandem fit points; these retain the cumulative tandem X axis.</summary>
        public IReadOnlyList<ProcessedTandemCsvPoint> Fit { get; }
        /// <summary>Measured NDH points, restored to local X when automatic restoration is enabled.</summary>
        public IReadOnlyList<ProcessedTandemCsvPoint> NormalizedHeat { get; }
        public IReadOnlyList<ProcessedTandemCsvPoint> ConcatenatedFit { get; }
        /// <summary>Measured NDH points exactly as they appeared in the imported tandem export.</summary>
        public IReadOnlyList<ProcessedTandemCsvPoint> ConcatenatedNormalizedHeat { get; }

        internal ProcessedTandemCsvSegment(
            string name,
            IList<ProcessedTandemCsvPoint> differentialPower,
            IList<ProcessedTandemCsvPoint> baseline,
            IList<ProcessedTandemCsvPoint> fit,
            IList<ProcessedTandemCsvPoint> normalizedHeat,
            IList<ProcessedTandemCsvPoint> concatenatedFit,
            IList<ProcessedTandemCsvPoint> concatenatedNormalizedHeat)
        {
            Name = name ?? string.Empty;
            DifferentialPower = new ReadOnlyCollection<ProcessedTandemCsvPoint>(differentialPower);
            Baseline = new ReadOnlyCollection<ProcessedTandemCsvPoint>(baseline);
            Fit = new ReadOnlyCollection<ProcessedTandemCsvPoint>(fit);
            NormalizedHeat = new ReadOnlyCollection<ProcessedTandemCsvPoint>(normalizedHeat);
            ConcatenatedFit = new ReadOnlyCollection<ProcessedTandemCsvPoint>(concatenatedFit);
            ConcatenatedNormalizedHeat = new ReadOnlyCollection<ProcessedTandemCsvPoint>(concatenatedNormalizedHeat);
        }
    }

    public readonly struct ProcessedTandemCsvPoint
    {
        public double X { get; }
        public double Y { get; }

        public ProcessedTandemCsvPoint(double x, double y)
        {
            X = x;
            Y = y;
        }
    }
}
