using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.AppClasses.AnalysisClasses.Models;

namespace AnalysisITC
{
    public sealed class TandemMixingScanSource
    {
        public string UniqueID { get; }
        public string Name { get; }
        public string FileName { get; }

        public TandemMixingScanSource(ExperimentData experiment)
        {
            UniqueID = experiment.UniqueID;
            Name = experiment.Name;
            FileName = experiment.FileName;
        }
    }

    public sealed class TandemMixingScanPoint
    {
        public IReadOnlyList<double> TransitionMixingFractions { get; }
        public double FirstTransitionMixingFraction => TransitionMixingFractions.ElementAtOrDefault(0);
        public double SecondTransitionMixingFraction => TransitionMixingFractions.Count > 1
            ? TransitionMixingFractions[1]
            : double.NaN;
        public double Rmsd { get; }
        public double N { get; }
        public double LogK { get; }
        public double Enthalpy { get; }
        public double Offset { get; }
        public string Termination { get; }
        public int Iterations { get; }

        public bool IsValid => IsFinite(Rmsd);

        public TandemMixingScanPoint(
            IReadOnlyList<double> transitionMixingFractions,
            double rmsd,
            double n,
            double logK,
            double enthalpy,
            double offset,
            string termination,
            int iterations)
        {
            TransitionMixingFractions = transitionMixingFractions?.ToList()
                ?? throw new ArgumentNullException(nameof(transitionMixingFractions));
            Rmsd = rmsd;
            N = n;
            LogK = logK;
            Enthalpy = enthalpy;
            Offset = offset;
            Termination = termination ?? "";
            Iterations = iterations;
        }

        internal static TandemMixingScanPoint Failed(IReadOnlyList<double> transitionMixingFractions, string termination)
        {
            return new TandemMixingScanPoint(
                transitionMixingFractions,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                termination,
                0);
        }

        internal static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public sealed class TandemMixingScanResult : ITCDataContainer
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public IReadOnlyList<TandemMixingScanSource> Sources { get; }
        public IReadOnlyList<double> MixingFractions { get; }
        public IReadOnlyList<TandemMixingScanPoint> Points { get; }
        public double[,] RmsdMatrix { get; }
        public double DeadVolume { get; }
        public bool DidRemoveOverflow { get; }
        public TandemMixingScanPoint BestPoint { get; }
        public TandemMixingScanPoint BestSharedMixingPoint { get; }

        internal TandemMixingScanResult(
            IReadOnlyList<ExperimentData> sources,
            IReadOnlyList<double> mixingFractions,
            IReadOnlyList<TandemMixingScanPoint> points,
            double[,] rmsdMatrix,
            TandemConcatenation.BackMixingSettings settings)
        {
            Sources = sources.Select(source => new TandemMixingScanSource(source)).ToList();
            MixingFractions = mixingFractions.ToList();
            Points = points.ToList();
            RmsdMatrix = rmsdMatrix;
            DeadVolume = settings.DeadVolume;
            DidRemoveOverflow = settings.DidRemoveOverflow;
            BestPoint = Points.Where(point => point.IsValid).OrderBy(point => point.Rmsd).FirstOrDefault();
            BestSharedMixingPoint = sources.Count == 3
                ? Points
                    .Where(point => point.IsValid
                        && point.TransitionMixingFractions.Skip(1)
                            .All(fraction => Math.Abs(fraction - point.FirstTransitionMixingFraction) < 1e-12))
                    .OrderBy(point => point.Rmsd)
                    .FirstOrDefault()
                : null;

            SetFileName("tandem_mixing_scan.csv");
            Date = DateTime.Now;
        }

        public string BuildMatrixCsv()
        {
            var builder = new StringBuilder();

            if (Sources.Count == 2)
            {
                builder.AppendLine("transition_1_percent,rmsd");

                for (var row = 0; row < MixingFractions.Count; row++)
                {
                    builder.Append(FormatPercent(MixingFractions[row]));
                    builder.Append(',');
                    builder.Append(FormatNumber(RmsdMatrix[row, 0]));
                    builder.AppendLine();
                }

                return builder.ToString();
            }

            builder.Append("transition_1_percent\\transition_2_percent");

            foreach (var fraction in MixingFractions)
            {
                builder.Append(',');
                builder.Append(FormatPercent(fraction));
            }

            builder.AppendLine();

            for (var row = 0; row < MixingFractions.Count; row++)
            {
                builder.Append(FormatPercent(MixingFractions[row]));

                for (var column = 0; column < MixingFractions.Count; column++)
                {
                    builder.Append(',');
                    builder.Append(FormatNumber(RmsdMatrix[row, column]));
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        public string BuildSummaryCsv()
        {
            var builder = new StringBuilder();
            builder.AppendLine("field,value");

            for (var i = 0; i < Sources.Count; i++)
            {
                AppendSummaryValue(builder, $"source_{i + 1}_name", Sources[i].Name);
                AppendSummaryValue(builder, $"source_{i + 1}_file", Sources[i].FileName);
                AppendSummaryValue(builder, $"source_{i + 1}_id", Sources[i].UniqueID);
            }

            AppendSummaryValue(builder, "dead_volume_uL", FormatNumber(DeadVolume * 1e6));
            AppendSummaryValue(builder, "did_remove_overflow", DidRemoveOverflow.ToString(Invariant));
            AppendSummaryValue(builder, "grid_min_percent", FormatPercent(MixingFractions.First()));
            AppendSummaryValue(builder, "grid_max_percent", FormatPercent(MixingFractions.Last()));
            AppendSummaryValue(builder, "grid_step_percent", MixingFractions.Count > 1
                ? FormatPercent(MixingFractions[1] - MixingFractions[0])
                : "0");
            AppendSummaryValue(builder, "failed_fit_count", Points.Count(point => !point.IsValid).ToString(Invariant));

            AppendPoint(builder, Sources.Count == 2 ? "best_1d" : "best_2d", BestPoint);
            if (Sources.Count == 3) AppendPoint(builder, "best_shared", BestSharedMixingPoint);

            return builder.ToString();
        }

        static void AppendPoint(StringBuilder builder, string prefix, TandemMixingScanPoint point)
        {
            if (point == null)
            {
                AppendSummaryValue(builder, prefix + "_rmsd", "NaN");
                return;
            }

            for (var i = 0; i < point.TransitionMixingFractions.Count; i++)
            {
                AppendSummaryValue(
                    builder,
                    prefix + $"_transition_{i + 1}_percent",
                    FormatPercent(point.TransitionMixingFractions[i]));
            }

            AppendSummaryValue(builder, prefix + "_rmsd", FormatNumber(point.Rmsd));
            AppendSummaryValue(builder, prefix + "_n", FormatNumber(point.N));
            AppendSummaryValue(builder, prefix + "_log_k", FormatNumber(point.LogK));
            AppendSummaryValue(builder, prefix + "_enthalpy_j_per_mol", FormatNumber(point.Enthalpy));
            AppendSummaryValue(builder, prefix + "_offset_j_per_mol", FormatNumber(point.Offset));
            AppendSummaryValue(builder, prefix + "_termination", point.Termination);
            AppendSummaryValue(builder, prefix + "_iterations", point.Iterations.ToString(Invariant));
        }

        static void AppendSummaryValue(StringBuilder builder, string field, string value)
        {
            builder.Append(Csv(field));
            builder.Append(',');
            builder.Append(Csv(value));
            builder.AppendLine();
        }

        static string FormatPercent(double fraction)
        {
            return (100.0 * fraction).ToString("0.##", Invariant);
        }

        static string FormatNumber(double value)
        {
            return TandemMixingScanPoint.IsFinite(value) ? value.ToString("G17", Invariant) : "NaN";
        }

        static string Csv(string value)
        {
            value ??= "";
            return value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r")
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }
    }

    public static class TandemMixingScanner
    {
        public const double DefaultMinimumMixingFraction = 0.0;
        public const double DefaultMaximumMixingFraction = 0.5;
        public const double DefaultMixingFractionStep = 0.02;
        public const double AdaptiveRefinementStep = 0.002;
        public const double AdaptiveRefinementRadius = 0.02;
        public static bool ReportPercentage { get; set; } = true;

        public static IReadOnlyList<double> DefaultMixingFractions =>
            MixingFractionsForStep(DefaultMixingFractionStep);

        public static IReadOnlyList<double> MixingFractionsForStep(double step) =>
            MixingFractionsForStep(DefaultMinimumMixingFraction, DefaultMaximumMixingFraction, step);

        public static IReadOnlyList<double> MixingFractionsForStep(double minimum, double maximum, double step) =>
            Enumerable.Range(
                    0,
                    (int)Math.Round((maximum - minimum) / step) + 1)
                .Select(index => minimum + index * step)
                .ToList();

        public static TandemMixingScanResult Run(
            IReadOnlyList<ExperimentData> sources,
            TandemConcatenation.BackMixingSettings settings,
            Action<int, int> reportProgress = null,
            IReadOnlyList<double> mixingFractions = null)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (sources.Count < 2 || sources.Count > 3)
                throw new ArgumentException("The tandem mixing scan requires two or three source experiments.", nameof(sources));
            if (sources.Any(source => source == null)) throw new ArgumentException("The scan cannot use null source experiments.", nameof(sources));
            if (sources.Any(source => source.IsTandemExperiment))
                throw new ArgumentException("Concatenated tandem experiments cannot be used as tandem mixing scan sources.", nameof(sources));
            if (sources.Any(source => source.Injections == null || source.Injections.Count == 0))
                throw new ArgumentException("Every tandem mixing scan source must contain injections.", nameof(sources));
            if (sources.Any(source => source.Injections.Where(injection => injection.Include).Any(injection => !injection.IsIntegrated)))
                throw new ArgumentException("All included injections must be integrated before running a tandem mixing scan.", nameof(sources));

            var fractions = (mixingFractions ?? DefaultMixingFractions).ToList();
            var (scanExperiment, segments) = BuildScanExperiment(sources);
            var secondTransitionCount = sources.Count == 3 ? fractions.Count : 1;
            var matrix = new double[fractions.Count, secondTransitionCount];
            var points = new List<TandemMixingScanPoint>(fractions.Count * secondTransitionCount);
            var total = fractions.Count * secondTransitionCount;
            var completed = 0;

            LogScanStart("grid", sources, settings, fractions, total);
            SolverInterface.TerminateAnalysisFlag.Lower();

            for (var firstIndex = 0; firstIndex < fractions.Count; firstIndex++)
            {
                for (var secondIndex = 0; secondIndex < secondTransitionCount; secondIndex++)
                {
                    var transitionMixingFractions = sources.Count == 3
                        ? new[] { fractions[firstIndex], fractions[secondIndex] }
                        : new[] { fractions[firstIndex] };
                    TandemMixingScanPoint point;

                    try
                    {
                        point = FitPoint(
                            scanExperiment,
                            segments,
                            settings,
                            transitionMixingFractions);
                    }
                    catch (Exception ex)
                    {
                        AppEventHandler.PrintAndLog(
                            $"Tandem mixing scan fit failed at {string.Join(" / ", transitionMixingFractions.Select(fraction => $"{100 * fraction:G4}%"))}:\n{ex}");
                        point = TandemMixingScanPoint.Failed(transitionMixingFractions, ex.GetType().Name);
                    }

                    matrix[firstIndex, secondIndex] = point.Rmsd;
                    points.Add(point);
                    completed++;
                    reportProgress?.Invoke(completed, total);
                }
            }

            var result = new TandemMixingScanResult(sources, fractions, points, matrix, settings.Copy());
            AppEventHandler.PrintAndLog(
                $"Tandem mixing scan complete: {FormatPoint(result.BestPoint)}, failed={result.Points.Count(point => !point.IsValid)}/{result.Points.Count}");

            return result;
        }

        public static TandemMixingScanPoint FindBestAdaptive(
            IReadOnlyList<ExperimentData> sources,
            TandemConcatenation.BackMixingSettings settings,
            Action<int, int> reportProgress = null)
        {
            var transitionCount = Math.Max(0, sources?.Count - 1 ?? 0);
            var coarsePointCount = (int)Math.Pow(DefaultMixingFractions.Count, transitionCount);
            var refinementPointCount = (int)Math.Pow(
                (int)Math.Round((2 * AdaptiveRefinementRadius) / AdaptiveRefinementStep) + 1,
                transitionCount);
            var progressTotal = coarsePointCount + refinementPointCount;

            AppEventHandler.PrintAndLog(
                $"Adaptive tandem mixing scan: coarseStep={FormatMixingFraction(DefaultMixingFractionStep)}, " +
                $"refineRadius={FormatMixingFraction(AdaptiveRefinementRadius)}, refineStep={FormatMixingFraction(AdaptiveRefinementStep)}, " +
                $"points={progressTotal}");
            var coarseResult = Run(
                sources,
                settings,
                (completed, total) => reportProgress?.Invoke(completed, progressTotal));
            var bestPoint = coarseResult.BestPoint;
            if (bestPoint == null) return null;

            var coarseBestPoint = bestPoint;
            AppEventHandler.PrintAndLog($"Adaptive tandem mixing scan coarse best: {FormatPoint(coarseBestPoint)}", 1);

            var localFractions = bestPoint.TransitionMixingFractions
                .Select(fraction => MixingFractionsAround(fraction, AdaptiveRefinementRadius, AdaptiveRefinementStep))
                .ToList();
            AppEventHandler.PrintAndLog(
                "Adaptive tandem mixing scan refinement windows: " +
                string.Join(", ", localFractions.Select((fractions, index) =>
                    $"T{index + 1}={FormatMixingFraction(fractions.First())}-{FormatMixingFraction(fractions.Last())} ({fractions.Count})")),
                1);
            var (scanExperiment, segments) = BuildScanExperiment(sources);
            var completedFine = 0;

            foreach (var transitionFractions in EnumerateTransitionFractionGrid(localFractions))
            {
                TandemMixingScanPoint point;

                try
                {
                    point = FitPoint(scanExperiment, segments, settings, transitionFractions);
                }
                catch (Exception ex)
                {
                    AppEventHandler.PrintAndLog(
                        $"Tandem mixing refinement fit failed at {string.Join(" / ", transitionFractions.Select(fraction => $"{100 * fraction:G4}%"))}:\n{ex}");
                    point = TandemMixingScanPoint.Failed(transitionFractions, ex.GetType().Name);
                }

                if (point.IsValid && point.Rmsd < bestPoint.Rmsd)
                    bestPoint = point;

                completedFine++;
                reportProgress?.Invoke(Math.Min(coarsePointCount + completedFine, progressTotal), progressTotal);
            }

            reportProgress?.Invoke(progressTotal, progressTotal);
            AppEventHandler.PrintAndLog(
                $"Adaptive tandem mixing scan final best: {FormatPoint(bestPoint)} " +
                $"(coarse={FormatPoint(coarseBestPoint)})",
                1);

            return bestPoint;
        }

        static void LogScanStart(
            string mode,
            IReadOnlyList<ExperimentData> sources,
            TandemConcatenation.BackMixingSettings settings,
            IReadOnlyList<double> fractions,
            int total)
        {
            AppEventHandler.PrintAndLog(
                $"Tandem mixing {mode} scan start: sources={sources.Count}, transitions={sources.Count - 1}, " +
                $"deadVolume={FormatMicroliters(settings.DeadVolume)}, removeOverflow={settings.DidRemoveOverflow}, " +
                $"grid={FormatMixingFraction(fractions.First())}-{FormatMixingFraction(fractions.Last())} ({fractions.Count}), points={total}");
            AppEventHandler.PrintAndLog(
                "Source order: " + string.Join(" -> ", sources.Select((source, index) =>
                    $"{index + 1}:{source.Name} [{source.Injections.Count(injection => injection.Include)}/{source.Injections.Count} included]")),
                1);
        }

        static string FormatPoint(TandemMixingScanPoint point)
        {
            if (point == null) return "none";

            return $"mix={FormatTransitionFractions(point.TransitionMixingFractions)}, " +
                   $"rmsd={FormatNumber(point.Rmsd)}, n={FormatNumber(point.N)}, logK={FormatNumber(point.LogK)}, " +
                   $"H={FormatNumber(point.Enthalpy)}, offset={FormatNumber(point.Offset)}, " +
                   $"termination={point.Termination}, iterations={point.Iterations}";
        }

        static string FormatTransitionFractions(IReadOnlyList<double> fractions)
        {
            return string.Join("/", fractions.Select(FormatMixingFraction));
        }

        static string FormatMixingFraction(double fraction)
        {
            return $"{(100 * fraction).ToString("0.###", CultureInfo.InvariantCulture)}%";
        }

        static string FormatMicroliters(double volume)
        {
            return $"{(1000000 * volume).ToString("G6", CultureInfo.InvariantCulture)} uL";
        }

        static string FormatNumber(double value)
        {
            return TandemMixingScanPoint.IsFinite(value)
                ? value.ToString("G6", CultureInfo.InvariantCulture)
                : "NaN";
        }

        static IReadOnlyList<double> MixingFractionsAround(double center, double radius, double step)
        {
            var min = Math.Max(DefaultMinimumMixingFraction, center - radius);
            var max = Math.Min(DefaultMaximumMixingFraction, center + radius);
            var count = (int)Math.Round((max - min) / step) + 1;

            return Enumerable.Range(0, count)
                .Select(index => Math.Round(min + index * step, 10))
                .ToList();
        }

        static IEnumerable<IReadOnlyList<double>> EnumerateTransitionFractionGrid(
            IReadOnlyList<IReadOnlyList<double>> transitionFractions)
        {
            if (transitionFractions.Count == 1)
            {
                foreach (var first in transitionFractions[0])
                    yield return new[] { first };
            }
            else if (transitionFractions.Count == 2)
            {
                foreach (var first in transitionFractions[0])
                    foreach (var second in transitionFractions[1])
                        yield return new[] { first, second };
            }
        }

        static TandemMixingScanPoint FitPoint(
            ExperimentData experiment,
            IList<TandemConcatenation.TandemInjectionSegment> segments,
            TandemConcatenation.BackMixingSettings settings,
            IReadOnlyList<double> transitionMixingFractions)
        {
            TandemConcatenation.ProcessInjectionsWithBackMixing(
                experiment,
                segments,
                settings,
                transitionMixingFractions);

            if (!AnalysisBuilder.IsAnalysisReady(experiment))
                throw new InvalidOperationException("The temporary tandem scan experiment is not ready for one-site analysis.");

            var model = AnalysisBuilder.ConstructModel(AnalysisModel.OneSetOfSites, experiment);
            model.ReuseAttachedSolutionInitialValues = true;
            model.InitializeParameters(experiment);
            model.Parameters.Table[ParameterType.Nvalue1].Update(1.0);
            model.SetModelOptions();
            model.ModelCloneOptions = ModelCloneOptions.DefaultOptions;

            var solver = SolverInterface.Initialize(model);
            solver.Silent = true;
            solver.CanCreateAnalysisResult = false;
            solver.CanReportAnalysisStepFinished = false;
            solver.ErrorEstimationMethod = ErrorEstimationMethod.None;
            solver.UseErrorWeightedFitting = false;

            var convergence = solver.Solve();
            if (convergence == null || convergence.Failed || convergence.Stopped || !TandemMixingScanPoint.IsFinite(convergence.Loss))
            {
                return TandemMixingScanPoint.Failed(
                    transitionMixingFractions,
                    convergence?.Termination.ToString() ?? "No convergence result");
            }

            return new TandemMixingScanPoint(
                transitionMixingFractions,
                convergence.Loss,
                ParameterValue(model, ParameterType.Nvalue1),
                ParameterValue(model, ParameterType.Affinity1),
                ParameterValue(model, ParameterType.Enthalpy1),
                ParameterValue(model, ParameterType.Offset),
                convergence.Termination.ToString(),
                convergence.Iterations);
        }

        static double ParameterValue(Model model, ParameterType key)
        {
            return model.Parameters.Table.TryGetValue(key, out var parameter)
                ? parameter.Value
                : double.NaN;
        }

        static (ExperimentData experiment, List<TandemConcatenation.TandemInjectionSegment> segments) BuildScanExperiment(
            IReadOnlyList<ExperimentData> sources)
        {
            var first = sources[0];
            var experiment = new ExperimentData("TandemMixingScan")
            {
                Instrument = first.Instrument,
                DataSourceFormat = first.DataSourceFormat,
                SyringeConcentration = first.SyringeConcentration,
                CellConcentration = first.CellConcentration,
                CellVolume = first.CellVolume,
                StirringSpeed = first.StirringSpeed,
                FeedBackMode = first.FeedBackMode,
                TargetTemperature = first.TargetTemperature,
                MeasuredTemperature = first.MeasuredTemperature,
                InitialDelay = first.InitialDelay,
                TargetPowerDiff = first.TargetPowerDiff,
                Date = DateTime.Now,
                Comments = "Temporary integrated-heat experiment for a tandem mixing scan.",
            };
            var segments = new List<TandemConcatenation.TandemInjectionSegment>();
            var injections = new List<InjectionData>();

            foreach (var source in sources)
            {
                var segmentStart = injections.Count;

                foreach (var sourceInjection in source.Injections)
                {
                    var injection = InjectionData.FromPEAQFile(
                        experiment,
                        injections.Count,
                        sourceInjection.Include,
                        sourceInjection.Time,
                        sourceInjection.Volume,
                        sourceInjection.Delay,
                        sourceInjection.Duration,
                        sourceInjection.Temperature);

                    if (sourceInjection.IsIntegrated)
                    {
                        injection.SetPeakArea(new FloatWithError(sourceInjection.PeakArea.Value, sourceInjection.PeakArea.SD));
                    }

                    injections.Add(injection);
                }

                segments.Add(new TandemConcatenation.TandemInjectionSegment(
                    segmentStart,
                    injections.Count - segmentStart,
                    source.Name));
            }

            experiment.Injections = injections;

            return (experiment, segments);
        }
    }
}
