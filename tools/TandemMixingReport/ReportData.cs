using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using MathNet.Numerics;

namespace TandemMixingReport
{
    sealed class TitrationPoint
    {
        public double X { get; init; }
        public double Y { get; init; }
        public int Run { get; init; }
        public bool Included { get; init; }
    }

    /// <summary>The window the model-free score uses at one transition, for one set of fractions.</summary>
    sealed class TransitionWindow
    {
        public double Fraction { get; init; }
        public List<(double x, double y)> Pre { get; init; }
        public List<(double x, double y)> Post { get; init; }
        public double[] Quadratic { get; init; }
        public double Rss { get; init; }
        public double Score { get; init; }

        public IEnumerable<(double x, double y)> All => Pre.Concat(Post);
    }

    sealed class TandemState
    {
        public List<TitrationPoint> Points { get; init; }
        public List<TransitionWindow> Windows { get; init; }
    }

    sealed class ReportCase
    {
        public string Group { get; init; }
        public string Title { get; init; }
        public double? KdMicromolar { get; init; }
        public int RunCount { get; set; }
        public IReadOnlyList<double> TrueFractions { get; init; }
        public IReadOnlyList<double> ChosenFractions { get; set; }
        public string Failure { get; set; }
        public TandemState Chosen { get; set; }
        public TandemState Truth { get; set; }

        public bool IsSynthetic => TrueFractions != null;
    }

    static class ReportData
    {
        public const string FixtureRelativePath = "AnalysisITC.Tests/Tandem/280-430-D2mut-1p6mM-JNK-200uM-1.ftxtc";

        public static readonly double[] KdMicromolar = { 25, 50, 100, 200, 500 };

        // Syringe concentration and true fractions per synthetic case.
        public static readonly (double syringe, double[] fractions)[] SyntheticDesigns =
        {
            (150e-6, new[] { 0.15 }),
            (100e-6, new[] { 0.10, 0.25 }),
            (75e-6, new[] { 0.05, 0.15, 0.30 }),
        };

        public const double CellVolume = 200e-6;
        public const double CellConcentration = 30e-6;
        public const int InjectionsPerRun = 19;
        public const double NoiseFraction = 0.002;

        public static TandemConcatenation.BackMixingSettings Settings() => new TandemConcatenation.BackMixingSettings
        {
            UseBackMixingMethod = true,
            DidRemoveOverflow = true,
            DeadVolume = 80e-6,
        };

        public static IEnumerable<ReportCase> SyntheticCases()
        {
            for (var kdIndex = 0; kdIndex < KdMicromolar.Length; kdIndex++)
            {
                var kd = KdMicromolar[kdIndex];
                var logK = -Math.Log10(kd * 1e-6);

                for (var designIndex = 0; designIndex < SyntheticDesigns.Length; designIndex++)
                {
                    var (syringe, fractions) = SyntheticDesigns[designIndex];
                    var sources = CreateSyntheticTandem(syringe, logK, fractions, NoiseFraction, seed: 100 * (kdIndex + 1) + designIndex);
                    var report = new ReportCase
                    {
                        Group = "Synthetic",
                        Title = $"Kd {kd:0} µM, {fractions.Length + 1} runs, syringe {syringe * 1e6:0} µM",
                        KdMicromolar = kd,
                        RunCount = sources.Count,
                        TrueFractions = fractions,
                    };
                    Analyse(report, sources);
                    yield return report;
                }
            }
        }

        public static async Task<ReportCase> RealCase(string path)
        {
            var report = new ReportCase { Group = "Real", Title = Path.GetFileNameWithoutExtension(path) };

            List<ExperimentData> sources;
            try
            {
                await using var stream = File.OpenRead(path);
                sources = (await FTXTCReader.ReadStream(stream))
                    .OfType<ExperimentData>()
                    .Where(experiment => !experiment.IsTandemExperiment)
                    .ToList();
            }
            catch (Exception ex)
            {
                report.Failure = $"could not be read: {ex.Message}";
                return report;
            }

            report.RunCount = sources.Count;
            if (sources.Count < 2 || sources.Count > 5)
            {
                report.Failure = $"{sources.Count} non-tandem experiment(s); the model-free search needs 2 to 5 runs";
                return report;
            }

            try
            {
                foreach (var source in sources)
                    await source.Processor.ProcessData(replace: false, invalidate: false, showProgress: false);
            }
            catch (Exception ex)
            {
                report.Failure = $"processing failed: {ex.Message}";
                return report;
            }

            Analyse(report, sources);
            return report;
        }

        static void Analyse(ReportCase report, List<ExperimentData> sources)
        {
            TandemMixingScanPoint point;
            try
            {
                point = TandemMixingScanner.FindBestAdaptive(sources, Settings(), criterion: TandemMixingCriterion.ModelFree);
            }
            catch (Exception ex)
            {
                report.Failure = $"scan failed: {ex.Message}";
                return;
            }

            if (point == null)
            {
                report.Failure = "scan returned no result (too few included injections at a transition)";
                return;
            }

            report.ChosenFractions = point.TransitionMixingFractions.ToList();
            var (experiment, segments) = TandemMixingScanner.BuildScanExperiment(sources);
            report.Chosen = Capture(experiment, segments, report.ChosenFractions);
            if (report.TrueFractions != null)
                report.Truth = Capture(experiment, segments, report.TrueFractions);
        }

        static TandemState Capture(
            ExperimentData experiment,
            IList<TandemConcatenation.TandemInjectionSegment> segments,
            IReadOnlyList<double> fractions)
        {
            TandemConcatenation.ProcessInjectionsWithBackMixingModel(
                experiment, segments, Settings(), fractions, AppSettings.DilutionCalculationMethod);

            var points = new List<TitrationPoint>();
            for (var run = 0; run < segments.Count; run++)
            {
                var segment = segments[run];
                for (var index = segment.InjectionNumStart; index < segment.InjectionNumStart + segment.InjectionCount; index++)
                {
                    var injection = experiment.Injections[index];
                    points.Add(new TitrationPoint
                    {
                        X = TandemContinuityScanner.MidpointMolarRatio(experiment, index),
                        Y = injection.Enthalpy,
                        Run = run,
                        Included = injection.Include,
                    });
                }
            }

            var windows = new List<TransitionWindow>();
            for (var transition = 0; transition < segments.Count - 1; transition++)
            {
                var (pre, post) = TandemContinuityScanner.TransitionWindow(experiment, segments, transition);
                var joint = pre.Concat(post).ToList();
                var rss = TandemContinuityScanner.ResidualSumOfSquares(joint);
                windows.Add(new TransitionWindow
                {
                    Fraction = fractions[transition],
                    Pre = pre,
                    Post = post,
                    Quadratic = Fit.Polynomial(
                        joint.Select(p => p.x).ToArray(),
                        joint.Select(p => p.y).ToArray(),
                        TandemContinuityScanner.PolynomialOrder),
                    Rss = rss,
                    Score = TandemContinuityScanner.Bias.Apply(rss, fractions[transition]),
                });
            }

            return new TandemState { Points = points, Windows = windows };
        }

        /// <summary>
        /// Same recipe as TandemMixingContinuityTests.CreateSyntheticTandem: one-site heats evaluated
        /// on the concentrations from the real back-mixing bookkeeping at the true fractions, plus
        /// Gaussian noise proportional to the largest heat.
        /// </summary>
        static List<ExperimentData> CreateSyntheticTandem(
            double syringeConcentration,
            double logK,
            IReadOnlyList<double> transitionMixingFractions,
            double noiseFraction,
            int seed)
        {
            var sources = Enumerable.Range(0, transitionMixingFractions.Count + 1)
                .Select(run =>
                {
                    var experiment = new ExperimentData($"synthetic-run-{run + 1}")
                    {
                        CellConcentration = new FloatWithError(CellConcentration),
                        SyringeConcentration = new FloatWithError(syringeConcentration),
                        CellVolume = CellVolume,
                        MeasuredTemperature = 25,
                        TargetTemperature = 25,
                    };
                    experiment.Injections = Enumerable.Range(0, InjectionsPerRun)
                        .Select(index => InjectionData.FromPEAQFile(
                            experiment, index, include: index != 0, time: 60 + 150 * index,
                            volume: index == 0 ? 0.4e-6 : 2e-6, delay: 150, duration: 4, temperature: 25))
                        .ToList();
                    return experiment;
                })
                .ToList();

            var (truth, segments) = TandemMixingScanner.BuildScanExperiment(sources);
            TandemConcatenation.ProcessInjectionsWithBackMixingModel(
                truth, segments, Settings(), transitionMixingFractions, AppSettings.DilutionCalculationMethod);

            var model = new OneSetOfSites(truth);
            model.InitializeParameters(truth);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1.0);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -40000);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, logK);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, -500);

            var heats = truth.Injections.Select(injection => model.Evaluate(injection.ID)).ToArray();
            var noise = noiseFraction * heats.Max(Math.Abs);
            var random = new Random(seed);
            var globalIndex = 0;
            foreach (var injection in sources.SelectMany(source => source.Injections))
            {
                var heat = heats[globalIndex++];
                injection.SetPeakArea(new FloatWithError(heat + noise * Gaussian(random), noise));
            }

            return sources;
        }

        static double Gaussian(Random random)
        {
            var u1 = 1.0 - random.NextDouble();
            var u2 = random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
