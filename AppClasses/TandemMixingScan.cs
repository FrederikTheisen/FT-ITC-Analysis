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
        public double FirstTransitionMixingFraction { get; }
        public double SecondTransitionMixingFraction { get; }
        public double Rmsd { get; }
        public double N { get; }
        public double LogK { get; }
        public double Enthalpy { get; }
        public double Offset { get; }
        public string Termination { get; }
        public int Iterations { get; }

        public bool IsValid => IsFinite(Rmsd);

        public TandemMixingScanPoint(
            double firstTransitionMixingFraction,
            double secondTransitionMixingFraction,
            double rmsd,
            double n,
            double logK,
            double enthalpy,
            double offset,
            string termination,
            int iterations)
        {
            FirstTransitionMixingFraction = firstTransitionMixingFraction;
            SecondTransitionMixingFraction = secondTransitionMixingFraction;
            Rmsd = rmsd;
            N = n;
            LogK = logK;
            Enthalpy = enthalpy;
            Offset = offset;
            Termination = termination ?? "";
            Iterations = iterations;
        }

        internal static TandemMixingScanPoint Failed(double firstFraction, double secondFraction, string termination)
        {
            return new TandemMixingScanPoint(
                firstFraction,
                secondFraction,
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
            BestSharedMixingPoint = Points
                .Where(point => point.IsValid
                    && Math.Abs(point.FirstTransitionMixingFraction - point.SecondTransitionMixingFraction) < 1e-12)
                .OrderBy(point => point.Rmsd)
                .FirstOrDefault();

            SetFileName("tandem_mixing_scan.csv");
            Date = DateTime.Now;
        }

        public string BuildMatrixCsv()
        {
            var builder = new StringBuilder();
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

            AppendPoint(builder, "best_2d", BestPoint);
            AppendPoint(builder, "best_shared", BestSharedMixingPoint);

            return builder.ToString();
        }

        static void AppendPoint(StringBuilder builder, string prefix, TandemMixingScanPoint point)
        {
            if (point == null)
            {
                AppendSummaryValue(builder, prefix + "_rmsd", "NaN");
                return;
            }

            AppendSummaryValue(builder, prefix + "_transition_1_percent", FormatPercent(point.FirstTransitionMixingFraction));
            AppendSummaryValue(builder, prefix + "_transition_2_percent", FormatPercent(point.SecondTransitionMixingFraction));
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

        public static IReadOnlyList<double> DefaultMixingFractions =>
            Enumerable.Range(
                    0,
                    (int)Math.Round((DefaultMaximumMixingFraction - DefaultMinimumMixingFraction) / DefaultMixingFractionStep) + 1)
                .Select(index => DefaultMinimumMixingFraction + index * DefaultMixingFractionStep)
                .ToList();

        public static TandemMixingScanResult Run(
            IReadOnlyList<ExperimentData> sources,
            TandemConcatenation.BackMixingSettings settings,
            Action<int, int> reportProgress = null)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (sources.Count != 3) throw new ArgumentException("The two-dimensional tandem mixing scan requires exactly three source experiments.", nameof(sources));
            if (sources.Any(source => source == null)) throw new ArgumentException("The scan cannot use null source experiments.", nameof(sources));
            if (sources.Any(source => source.IsTandemExperiment))
                throw new ArgumentException("Concatenated tandem experiments cannot be used as tandem mixing scan sources.", nameof(sources));
            if (sources.Any(source => source.Injections == null || source.Injections.Count == 0))
                throw new ArgumentException("Every tandem mixing scan source must contain injections.", nameof(sources));
            if (sources.Any(source => source.Injections.Where(injection => injection.Include).Any(injection => !injection.IsIntegrated)))
                throw new ArgumentException("All included injections must be integrated before running a tandem mixing scan.", nameof(sources));

            var fractions = DefaultMixingFractions;
            var (scanExperiment, segments) = BuildScanExperiment(sources);
            var matrix = new double[fractions.Count, fractions.Count];
            var points = new List<TandemMixingScanPoint>(fractions.Count * fractions.Count);
            var total = fractions.Count * fractions.Count;
            var completed = 0;

            SolverInterface.TerminateAnalysisFlag.Lower();

            for (var firstIndex = 0; firstIndex < fractions.Count; firstIndex++)
            {
                for (var secondIndex = 0; secondIndex < fractions.Count; secondIndex++)
                {
                    var firstFraction = fractions[firstIndex];
                    var secondFraction = fractions[secondIndex];
                    TandemMixingScanPoint point;

                    try
                    {
                        point = FitPoint(
                            scanExperiment,
                            segments,
                            settings,
                            firstFraction,
                            secondFraction);
                    }
                    catch (Exception ex)
                    {
                        AppEventHandler.PrintAndLog(
                            $"Tandem mixing scan fit failed at {100 * firstFraction:G4}% / {100 * secondFraction:G4}%:\n{ex}");
                        point = TandemMixingScanPoint.Failed(firstFraction, secondFraction, ex.GetType().Name);
                    }

                    matrix[firstIndex, secondIndex] = point.Rmsd;
                    points.Add(point);
                    completed++;
                    reportProgress?.Invoke(completed, total);
                }
            }

            return new TandemMixingScanResult(sources, fractions, points, matrix, settings.Copy());
        }

        static TandemMixingScanPoint FitPoint(
            ExperimentData experiment,
            IList<TandemConcatenation.TandemInjectionSegment> segments,
            TandemConcatenation.BackMixingSettings settings,
            double firstFraction,
            double secondFraction)
        {
            TandemConcatenation.ProcessInjectionsWithBackMixing(
                experiment,
                segments,
                settings,
                new[] { firstFraction, secondFraction });

            if (!AnalysisBuilder.IsAnalysisReady(experiment))
                throw new InvalidOperationException("The temporary tandem scan experiment is not ready for one-site analysis.");

            var model = AnalysisBuilder.ConstructModel(AnalysisModel.OneSetOfSites, experiment);
            model.ReuseAttachedSolutionInitialValues = true;
            model.InitializeParameters(experiment);
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
                    firstFraction,
                    secondFraction,
                    convergence?.Termination.ToString() ?? "No convergence result");
            }

            return new TandemMixingScanPoint(
                firstFraction,
                secondFraction,
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
                Comments = "Temporary integrated-heat experiment for a two-dimensional tandem mixing scan.",
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
