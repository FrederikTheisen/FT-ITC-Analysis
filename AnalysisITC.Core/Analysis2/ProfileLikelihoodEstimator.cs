using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Accord.Math.Optimization;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;
using MathNet.Numerics.Distributions;

namespace AnalysisITC.Core.Analysis
{
    internal enum ProfileLikelihoodTracePhase
    {
        BestFit,
        Expansion,
        Refinement,
    }

    internal sealed class ProfileLikelihoodTracePoint
    {
        public ProfileCoordinateId Coordinate { get; }
        public int Direction { get; }
        public ProfileLikelihoodTracePhase Phase { get; }
        public double ParameterValue { get; }
        public double ObjectiveIncrement { get; }
        public double TargetIncrement { get; }
        public double CenteredValue => ObjectiveIncrement - TargetIncrement;
        public bool IsUsable { get; }

        public ProfileLikelihoodTracePoint(ProfileCoordinateId coordinate, int direction,
            ProfileLikelihoodTracePhase phase, double parameterValue, double objectiveIncrement,
            double targetIncrement, bool isUsable)
        {
            Coordinate = coordinate;
            Direction = direction < 0 ? -1 : 1;
            Phase = phase;
            ParameterValue = parameterValue;
            ObjectiveIncrement = objectiveIncrement;
            TargetIncrement = targetIncrement;
            IsUsable = isUsable;
        }
    }

    /// <summary>Deterministic, conditional profile-likelihood search.</summary>
    public static class ProfileLikelihoodEstimator
    {
        public const double ConfidenceLevel = .95;
        public const double CrossingTolerance = 1e-4;
        public const int MaxExpansionLocations = 24;
        public const int MaxLocationsPerSide = 64;
        public const int MaxBisectionLocations = 40;
        internal const int PostCrossingExpansionLocations = 3;
        const double ImprovedPrimaryTolerance = 1e-4;
        const double Z95 = 1.959963984540054;

        public static ProfileLikelihoodRunResult Estimate(Model model, SolverAlgorithm algorithm, bool weighted, int candidateIterationLimit,
            double toleranceModifier = SolverInterface.ErrorEstimationToleranceModifier, CancellationToken cancellationToken = default(CancellationToken),
            Action<ProfileLikelihoodProgress> progress = null)
            => Run(model, algorithm, weighted, candidateIterationLimit, toleranceModifier, cancellationToken, progress);

        public static ProfileLikelihoodRunResult Estimate(GlobalModel model, SolverAlgorithm algorithm, bool weighted, int candidateIterationLimit,
            double toleranceModifier = SolverInterface.ErrorEstimationToleranceModifier, CancellationToken cancellationToken = default(CancellationToken),
            Action<ProfileLikelihoodProgress> progress = null)
            => Run(model, algorithm, weighted, candidateIterationLimit, toleranceModifier, cancellationToken, progress);

        public static double CalculateUnweightedTarget(int n, int df, double confidenceLevel = ConfidenceLevel)
        {
            if (n <= 0 || df <= 0 || !FWEMath.IsFinite(confidenceLevel) || confidenceLevel <= 0 || confidenceLevel >= 1) return double.NaN;
            return n * Math.Log(1 + FisherSnedecor.InvCDF(1, df, confidenceLevel) / df);
        }

        public static double CalculateWeightedTarget(double confidenceLevel = ConfidenceLevel)
            => !FWEMath.IsFinite(confidenceLevel) || confidenceLevel <= 0 || confidenceLevel >= 1
                ? double.NaN : ChiSquared.InvCDF(1, confidenceLevel);

        public static double EquivalentStandardDeviation(double value, double lower95, double upper95)
        {
            var sL = (value - lower95) / Z95;
            var sU = (upper95 - value) / Z95;
            return Math.Sqrt(Math.Max(0, sL * sL - sL * sU + sU * sU));
        }

        public static string Describe(ProfileLikelihoodRunResult run)
        {
            if (run == null) return string.Empty;
            var sides = run.Coordinates.SelectMany(c => new[] { c.Lower, c.Upper }).ToList();
            var counts = sides.GroupBy(side => side.Outcome).ToDictionary(group => group.Key, group => group.Count());
            var warnings = run.Coordinates.SelectMany(c => c.ShapeWarnings).GroupBy(warning => warning).ToDictionary(group => group.Key, group => group.Count());
            var summary = $"endpoints found={sides.Count(side => side.IsEndpointFound)}/{2 * run.ParameterCount}, attempted solver calls={run.AttemptedSolverCalls}";
            foreach (var outcome in Enum.GetValues(typeof(ProfileSideOutcome)).Cast<ProfileSideOutcome>())
                if (counts.TryGetValue(outcome, out var count) && count > 0 && outcome != ProfileSideOutcome.EndpointFound)
                    summary += $", {outcome}={count}";
            foreach (var warning in warnings)
                summary += $", {warning.Key}={warning.Value}";
            return summary;
        }

        public static ProfileLikelihoodSummary Summarize(GlobalSolution solution)
        {
            var runs = solution?.ProfileLikelihoodRun != null
                ? new[] { solution.ProfileLikelihoodRun }
                : (solution?.Solutions ?? new List<SolutionInterface>()).Select(member => member?.ProfileLikelihoodRun)
                    .OfType<ProfileLikelihoodRunResult>().ToArray();
            var members = solution?.Solutions ?? new List<SolutionInterface>();
            var totalSides = solution?.ProfileLikelihoodRun != null
                ? Math.Max(0, solution.ProfileLikelihoodRun.ParameterCount) * 2
                : members.Sum(member =>
                    member?.Model == null ? 0 : member.Model.Parameters.GetFittedParameters().Length * 2);
            var endpoints = runs.Sum(run => run.Coordinates.Sum(c =>
                (c.Lower.IsEndpointFound ? 1 : 0) + (c.Upper.IsEndpointFound ? 1 : 0)));
            totalSides = Math.Max(totalSides, runs.Sum(run => Math.Max(0, run.ParameterCount) * 2));
            var expectedFittedCoordinates = totalSides / 2;
            var missing = solution?.ProfileLikelihoodRun == null && solution?.Solutions != null
                && members.Where(member => member?.Model?.Parameters.GetFittedParameters().Length > 0).Count(member => member?.ProfileLikelihoodRun == null) > 0;
            var completeIntervals = runs.Sum(run => run.Coordinates.Count(c => c.HasCompleteInterval));
            // A represented zero-coordinate NotRun is the expected result for a
            // member with nothing to profile. It must not downgrade completed
            // fitted members in an independent global analysis.
            var effectiveRuns = runs.Where(run => run.ParameterCount > 0).ToArray();
            var allKnownNotRun = effectiveRuns.Length == 0 || effectiveRuns.All(run => run.Outcome == ErrorEstimationOutcome.NotRun);
            var allExpectedMembersRepresented = solution?.ProfileLikelihoodRun != null
                || (members.Count != 0 && runs.Length == members.Count);
            var outcome = effectiveRuns.Any(run => run.Outcome == ErrorEstimationOutcome.Cancelled) ? ErrorEstimationOutcome.Cancelled
                : expectedFittedCoordinates == 0 ? ErrorEstimationOutcome.NotRun
                : allKnownNotRun && (missing || runs.Length == 0 || allExpectedMembersRepresented) ? ErrorEstimationOutcome.NotRun
                : completeIntervals == 0 ? ErrorEstimationOutcome.CompleteFailure
                : !missing && effectiveRuns.Length > 0 && completeIntervals >= expectedFittedCoordinates
                    && effectiveRuns.All(run => run.Outcome == ErrorEstimationOutcome.Completed) ? ErrorEstimationOutcome.Completed
                : ErrorEstimationOutcome.PartialFailure;
            var diagnostics = string.Join("; ", effectiveRuns.Select(Describe));
            if (missing)
                diagnostics = string.IsNullOrWhiteSpace(diagnostics)
                    ? "missing profile member run(s)"
                    : diagnostics + "; missing profile member run(s)";
            return new ProfileLikelihoodSummary(outcome, endpoints, totalSides,
                TimeSpan.FromTicks(runs.Sum(run => run.Elapsed.Ticks)), diagnostics);
        }

        public static ProfileLikelihoodRunResult Run(
            Model model, SolverAlgorithm algorithm, bool weighted, int candidateIterationLimit,
            double toleranceModifier = SolverInterface.ErrorEstimationToleranceModifier,
            CancellationToken cancellationToken = default(CancellationToken), Action<ProfileLikelihoodProgress> progress = null)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var coordinates = model.Parameters.GetFittedParameters()
                .Select((p, i) => new Coordinate(p.Key, ParameterBoundaryScope.Local, model.Data?.UniqueID, i))
                .ToList();
            return RunLocal(model, coordinates, algorithm, weighted, candidateIterationLimit, toleranceModifier, cancellationToken, progress);
        }

        /// <summary>Test-only entry point that exposes the evaluated profile locations for diagnostic plots.</summary>
        internal static ProfileLikelihoodRunResult RunWithTraceForTesting(
            Model model, SolverAlgorithm algorithm, bool weighted, int candidateIterationLimit,
            Action<ProfileLikelihoodTracePoint> trace,
            double toleranceModifier = SolverInterface.ErrorEstimationToleranceModifier,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (trace == null) throw new ArgumentNullException(nameof(trace));
            var coordinates = model.Parameters.GetFittedParameters()
                .Select((p, i) => new Coordinate(p.Key, ParameterBoundaryScope.Local, model.Data?.UniqueID, i))
                .ToList();
            return RunLocal(model, coordinates, algorithm, weighted, candidateIterationLimit,
                toleranceModifier, cancellationToken, null, trace);
        }

        public static ProfileLikelihoodRunResult Run(
            GlobalModel model, SolverAlgorithm algorithm, bool weighted, int candidateIterationLimit,
            double toleranceModifier = SolverInterface.ErrorEstimationToleranceModifier,
            CancellationToken cancellationToken = default(CancellationToken), Action<ProfileLikelihoodProgress> progress = null)
            => RunGlobal(model, algorithm, weighted, candidateIterationLimit, toleranceModifier,
                cancellationToken, progress, null);

        /// <summary>Test-only global entry point that exposes evaluated profile locations.</summary>
        internal static ProfileLikelihoodRunResult RunWithTraceForTesting(
            GlobalModel model, SolverAlgorithm algorithm, bool weighted, int candidateIterationLimit,
            Action<ProfileLikelihoodTracePoint> trace,
            double toleranceModifier = SolverInterface.ErrorEstimationToleranceModifier,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (trace == null) throw new ArgumentNullException(nameof(trace));
            return RunGlobal(model, algorithm, weighted, candidateIterationLimit, toleranceModifier,
                cancellationToken, null, trace);
        }

        static ProfileLikelihoodRunResult RunGlobal(
            GlobalModel model, SolverAlgorithm algorithm, bool weighted, int candidateIterationLimit,
            double toleranceModifier, CancellationToken cancellationToken,
            Action<ProfileLikelihoodProgress> progress, Action<ProfileLikelihoodTracePoint> trace)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (cancellationToken.IsCancellationRequested)
            {
                var n = model.GetNumberOfPoints();
                var p = model.Parameters.GlobalTable.Values.Count(value => value.IsFitted)
                    + model.Models.SelectMany(member => member.Parameters.GetFittedParameters()).Count();
                var df = n - p;
                var calibration = weighted ? ProfileLikelihoodCalibration.WeightedChiSquared : ProfileLikelihoodCalibration.UnweightedFCalibratedRss;
                var baseline = Evaluate(model, weighted);
                return Build(calibration, n, p, df, baseline, Target(n, df, weighted), algorithm, weighted,
                    toleranceModifier, candidateIterationLimit, TimeSpan.Zero, ErrorEstimationOutcome.Cancelled,
                    Array.Empty<ProfileCoordinateResult>(), 0);
            }
            using var cancellationRegistration = cancellationToken.Register(() => SolverInterface.TerminateAnalysisFlag.Raise());
            try
            {
                if (model.ShouldFitIndividually)
                    throw new InvalidOperationException("A global profile requires shared coordinates; profile members independently.");

            var coords = new List<Coordinate>();
            var index = 0;
            foreach (var p in model.Parameters.GlobalTable.Values.Where(p => p.IsFitted))
                coords.Add(new Coordinate(p.Key, ParameterBoundaryScope.Shared, null, index++));
            for (var m = 0; m < model.Models.Count; m++)
                foreach (var p in model.Parameters.IndividualModelParameterList[m].GetFittedParameters())
                    coords.Add(new Coordinate(p.Key, ParameterBoundaryScope.Local, model.Models[m].Data?.UniqueID, index++));

            var start = DateTime.UtcNow;
            var baseline = Evaluate(model, weighted);
            var n = model.GetNumberOfPoints();
            var pcount = coords.Count;
            var calibration = weighted ? ProfileLikelihoodCalibration.WeightedChiSquared : ProfileLikelihoodCalibration.UnweightedFCalibratedRss;
            var df = n - pcount;
            var target = Target(n, df, weighted);
            if (coords.Count == 0)
                return Build(calibration, n, pcount, df, baseline, target, algorithm, weighted, toleranceModifier, candidateIterationLimit, TimeSpan.Zero, ErrorEstimationOutcome.NotRun, Array.Empty<ProfileCoordinateResult>(), 0);
            if (!ValidBaseline(baseline, weighted) || (!weighted && df <= 0))
                return Build(calibration, n, pcount, df, baseline, target, algorithm, weighted, toleranceModifier, candidateIterationLimit, DateTime.UtcNow - start, ErrorEstimationOutcome.CompleteFailure, Array.Empty<ProfileCoordinateResult>(), 0);

                var run = RunCoordinates(model, coords, baseline, target, algorithm, weighted, candidateIterationLimit, toleranceModifier, cancellationToken, true, progress, trace);
                return Build(calibration, n, pcount, df, baseline, target, algorithm, weighted, toleranceModifier, candidateIterationLimit,
                    DateTime.UtcNow - start, Outcome(run.Results, cancellationToken.IsCancellationRequested || SolverInterface.TerminateAnalysisFlag.Up, run.PrimaryImproved), run.Results, run.Attempted);
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested)
                    SolverInterface.TerminateAnalysisFlag.Lower();
            }
        }

        static ProfileLikelihoodRunResult RunLocal(Model model, List<Coordinate> coordinates, SolverAlgorithm algorithm, bool weighted,
            int candidateIterationLimit, double toleranceModifier, CancellationToken token, Action<ProfileLikelihoodProgress> progress,
            Action<ProfileLikelihoodTracePoint> trace = null)
        {
            var start = DateTime.UtcNow;
            // Avoid touching the process-wide optimizer stop flag for a request
            // that was already cancelled before profiling began.
            if (token.IsCancellationRequested)
            {
                var n = model.NumberOfPoints;
                var p = coordinates.Count;
                var df = n - p;
                var calibration = weighted ? ProfileLikelihoodCalibration.WeightedChiSquared : ProfileLikelihoodCalibration.UnweightedFCalibratedRss;
                var baseline = Evaluate(model, weighted);
                return Build(calibration, n, p, df, baseline, Target(n, df, weighted), algorithm, weighted,
                    toleranceModifier, candidateIterationLimit, TimeSpan.Zero, ErrorEstimationOutcome.Cancelled,
                    Array.Empty<ProfileCoordinateResult>(), 0);
            }
            using var cancellationRegistration = token.Register(() => SolverInterface.TerminateAnalysisFlag.Raise());
            try
            {
                var baseline = Evaluate(model, weighted);
                var n = model.NumberOfPoints;
                var p = coordinates.Count;
                var df = n - p;
                var target = Target(n, df, weighted);
                var calibration = weighted ? ProfileLikelihoodCalibration.WeightedChiSquared : ProfileLikelihoodCalibration.UnweightedFCalibratedRss;
                if (p == 0)
                    return Build(calibration, n, p, df, baseline, target, algorithm, weighted, toleranceModifier, candidateIterationLimit, DateTime.UtcNow - start, ErrorEstimationOutcome.NotRun, Array.Empty<ProfileCoordinateResult>(), 0);
                if (!ValidBaseline(baseline, weighted) || (!weighted && df <= 0))
                    return Build(calibration, n, p, df, baseline, target, algorithm, weighted, toleranceModifier, candidateIterationLimit, DateTime.UtcNow - start, ErrorEstimationOutcome.CompleteFailure, Array.Empty<ProfileCoordinateResult>(), 0);

                var run = RunCoordinates(model, coordinates, baseline, target, algorithm, weighted, candidateIterationLimit, toleranceModifier, token, false, progress, trace);
                return Build(calibration, n, p, df, baseline, target, algorithm, weighted, toleranceModifier, candidateIterationLimit,
                    DateTime.UtcNow - start, Outcome(run.Results, token.IsCancellationRequested || SolverInterface.TerminateAnalysisFlag.Up, run.PrimaryImproved), run.Results, run.Attempted);
            }
            finally
            {
                if (token.IsCancellationRequested)
                    SolverInterface.TerminateAnalysisFlag.Lower();
            }
        }

        static ProfileLikelihoodRunResult Build(ProfileLikelihoodCalibration calibration, int n, int p, int df, double baseline,
            double target, SolverAlgorithm algorithm, bool weighted, double tolerance, int cap, TimeSpan elapsed,
            ErrorEstimationOutcome outcome, IEnumerable<ProfileCoordinateResult> coordinates, int attempted)
            => new ProfileLikelihoodRunResult(ConfidenceLevel, calibration, n, p, 1, df, baseline, target, algorithm,
                weighted, tolerance, cap, MaxExpansionLocations, MaxBisectionLocations, elapsed, outcome, coordinates, attempted);

        static ErrorEstimationOutcome Outcome(IReadOnlyList<ProfileCoordinateResult> results, bool cancelled, bool improved)
        {
            if (cancelled) return ErrorEstimationOutcome.Cancelled;
            if (improved) return ErrorEstimationOutcome.CompleteFailure;
            if (results.Count == 0) return ErrorEstimationOutcome.CompleteFailure;
            var complete = results.Count(r => r.HasCompleteInterval);
            return complete == results.Count ? ErrorEstimationOutcome.Completed : complete > 0 ? ErrorEstimationOutcome.PartialFailure : ErrorEstimationOutcome.CompleteFailure;
        }

        sealed class Coordinate
        {
            public readonly ParameterType Key;
            public readonly ParameterBoundaryScope Scope;
            public readonly string Experiment;
            public readonly int Index;
            public Coordinate(ParameterType key, ParameterBoundaryScope scope, string experiment, int index) { Key = key; Scope = scope; Experiment = experiment; Index = index; }
            public ProfileCoordinateId Id => new ProfileCoordinateId(Key, Scope, Experiment, Index);
        }

        sealed class RunCoordinatesResult
        {
            public readonly List<ProfileCoordinateResult> Results = new List<ProfileCoordinateResult>();
            public int Attempted;
            public bool PrimaryImproved;
        }

        sealed class ProgressState
        {
            readonly int total;
            readonly Action<ProfileLikelihoodProgress> callback;
            readonly object sync = new object();
            int completed;
            int endpoints;
            int attempted;

            public ProgressState(int total, Action<ProfileLikelihoodProgress> callback)
            {
                this.total = total;
                this.callback = callback;
            }

            public void Report(ProfileSideResult side)
            {
                lock (sync)
                {
                    var done = ++completed;
                    if (side.IsEndpointFound) ++endpoints;
                    var calls = attempted += side.AttemptedSolverCalls;
                    if (callback == null) return;
                    try { callback(new ProfileLikelihoodProgress(done, total, endpoints, calls)); }
                    catch { /* progress observers must not affect deterministic fitting */ }
                }
            }
        }

        static RunCoordinatesResult RunCoordinates(object source, List<Coordinate> coords, double baseline, double target,
            SolverAlgorithm algorithm, bool weighted, int maxIterations, double toleranceModifier, CancellationToken token, bool global,
            Action<ProfileLikelihoodProgress> progress, Action<ProfileLikelihoodTracePoint> trace = null)
        {
            var output = new RunCoordinatesResult();
            var staged = new ProfileCoordinateResult[coords.Count];
            var progressState = new ProgressState(coords.Count * 2, progress);
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, AppSettings.MaxDegreeOfParallelism),
                CancellationToken = token,
            };
            try
            {
                Parallel.For(0, coords.Count, parallelOptions, (coordinateIndex, state) =>
                {
                    if (output.PrimaryImproved || token.IsCancellationRequested) { state.Stop(); return; }
                    var coordinate = coords[coordinateIndex];
                    var value = global ? GetValue((GlobalModel)source, coordinate) : GetValue((Model)source, coordinate);
                    var bounds = global ? GetBounds((GlobalModel)source, coordinate) : GetBounds((Model)source, coordinate);
                    if (!FWEMath.IsFinite(value) || bounds == null || bounds.Length < 2 || bounds[1] < bounds[0])
                    {
                        var invalidLower = new ProfileSideResult(ProfileSideOutcome.NonFiniteCandidate);
                        var invalidUpper = new ProfileSideResult(ProfileSideOutcome.NonFiniteCandidate);
                        progressState.Report(invalidLower);
                        progressState.Report(invalidUpper);
                        staged[coordinateIndex] = new ProfileCoordinateResult(coordinate.Id, value, double.NaN, double.NaN, invalidLower, invalidUpper);
                        return;
                    }
                    var step = global ? GetStep((GlobalModel)source, coordinate) : GetStep((Model)source, coordinate);
                    if (!FWEMath.IsFinite(step) || step <= 0) step = Math.Max(.05 * Math.Abs(value), 1e-6);
                    var localAttempted = 0;
                    var localImproved = false;
                    var lower = Search(source, coordinate, value, bounds, -1, step, baseline, target, algorithm, weighted, maxIterations, toleranceModifier, token, global, ref localAttempted, ref localImproved, trace: trace);
                    progressState.Report(lower);
                    if (localImproved)
                    {
                        var cancelledUpper = new ProfileSideResult(ProfileSideOutcome.Cancelled);
                        progressState.Report(cancelledUpper);
                        staged[coordinateIndex] = new ProfileCoordinateResult(coordinate.Id, value, bounds[0], bounds[1], lower, cancelledUpper);
                        lock (output) { output.Attempted += localAttempted; output.PrimaryImproved = true; }
                        state.Stop(); return;
                    }
                    var upper = Search(source, coordinate, value, bounds, +1, step, baseline, target, algorithm, weighted, maxIterations, toleranceModifier, token, global, ref localAttempted, ref localImproved, trace: trace);
                    progressState.Report(upper);
                    lock (output)
                    {
                        output.Attempted += localAttempted;
                        if (localImproved) output.PrimaryImproved = true;
                    }
                    if (localImproved)
                    {
                        staged[coordinateIndex] = new ProfileCoordinateResult(coordinate.Id, value, bounds[0], bounds[1], lower, upper);
                        state.Stop(); return;
                    }
                    staged[coordinateIndex] = new ProfileCoordinateResult(coordinate.Id, value, bounds[0], bounds[1], lower, upper,
                        lower.Warnings.Concat(upper.Warnings).ToArray());
                });
            }
            catch (OperationCanceledException)
            {
                // Preserve coordinate results that completed before cancellation;
                // the caller marks the run cancelled without manufacturing intervals.
            }
            output.Results.AddRange(staged.Where(result => result != null));
            return output;
        }

        static ProfileSideResult Search(object source, Coordinate c, double best, double[] bounds, int direction, double step,
            double baseline, double target, SolverAlgorithm algorithm, bool weighted, int maxIterations, double toleranceModifier,
            CancellationToken token, bool global, ref int attempted, ref bool improved,
            Func<double, IReadOnlyDictionary<string, double>, Candidate> testEvaluator = null,
            Action<ProfileLikelihoodTracePoint> trace = null)
        {
            var warnings = new List<string>();
            var points = new List<Tuple<double, double>>();
            // The fitted point is the seed of the connected profile. Its
            // likelihood difference is exactly -target, rather than zero.
            points.Add(Tuple.Create(best, -target));
            trace?.Invoke(new ProfileLikelihoodTracePoint(c?.Id, direction, ProfileLikelihoodTracePhase.BestFit,
                best, 0, target, true));
            var sideBound = bounds[direction < 0 ? 0 : 1];
            if (best == sideBound)
                return new ProfileSideResult(ProfileSideOutcome.BoundReachedBeforeCrossing,
                    evaluationCount: 0, attemptedSolverCalls: 0, warnings: warnings);
            var previous = best;
            var locations = 0;
            var sideAttempts = attempted;
            var hadNonFinite = false;
            var hadOptimizerFailure = false;
            var frontierUnusable = false;
            double? firstEndpoint = null;
            bool crossed = false;
            bool reentered = false;
            var postCrossingLocations = 0;
            double firstCrossingG = double.NaN;
            var successfulPoints = 0;
            Dictionary<string, double> nearestWarm = null;
            for (var k = 0; k < MaxExpansionLocations && locations < MaxLocationsPerSide; k++)
            {
                if (token.IsCancellationRequested || SolverInterface.TerminateAnalysisFlag.Up) return new ProfileSideResult(ProfileSideOutcome.Cancelled, evaluationCount: locations, attemptedSolverCalls: attempted - sideAttempts, warnings: warnings);
                var displacement = step * Math.Pow(2, k);
                var candidate = direction < 0 ? best - displacement : best + displacement;
                candidate = Math.Max(bounds[0], Math.Min(bounds[1], candidate));
                if (candidate == previous)
                {
                    if (candidate == sideBound && !crossed && !frontierUnusable)
                        return new ProfileSideResult(ProfileSideOutcome.BoundReachedBeforeCrossing,
                            evaluationCount: locations, attemptedSolverCalls: attempted - sideAttempts, warnings: warnings);
                    break;
                }
                previous = candidate;
                var crossedBeforeEvaluation = crossed;
                if (crossedBeforeEvaluation) postCrossingLocations++;
                var evaluated = testEvaluator != null
                    ? testEvaluator(candidate, nearestWarm)
                    : EvaluateCandidate(source, c, candidate, algorithm, weighted, maxIterations, toleranceModifier, global, nearestWarm, ref attempted);
                locations++;
                if (!evaluated.Usable)
                {
                    // A failed warm-start is retried once from the primary fit;
                    // candidate construction is deterministic, so this also
                    // provides the required cold-start fallback.
                    evaluated = testEvaluator != null
                        ? testEvaluator(candidate, null)
                        : EvaluateCandidate(source, c, candidate, algorithm, weighted, maxIterations, toleranceModifier, global, null, ref attempted);
                    hadNonFinite |= evaluated.NonFinite;
                    hadOptimizerFailure |= evaluated.OptimizerFailure;
                    if (!evaluated.Usable)
                    {
                        trace?.Invoke(new ProfileLikelihoodTracePoint(c?.Id, direction, ProfileLikelihoodTracePhase.Expansion,
                            candidate, double.NaN, target, false));
                        frontierUnusable = true;
                        if (postCrossingLocations >= PostCrossingExpansionLocations) break;
                        continue;
                    }
                }
                frontierUnusable = false;
                successfulPoints++;
                nearestWarm = evaluated.NuisanceValues;
                var difference = Difference(evaluated.Objective, baseline, evaluated.ObservationCount, weighted);
                var g = difference - target;
                trace?.Invoke(new ProfileLikelihoodTracePoint(c?.Id, direction, ProfileLikelihoodTracePhase.Expansion,
                    candidate, difference, target, true));
                if (difference < -ImprovedPrimaryTolerance)
                {
                    improved = true;
                    return new ProfileSideResult(ProfileSideOutcome.PrimaryMinimumImproved, evaluationCount: locations, attemptedSolverCalls: attempted - sideAttempts, warnings: warnings);
                }
                points.Add(Tuple.Create(candidate, g));
                if (crossed && g < 0) reentered = true;
                if (points.Count > 1 && g < points[points.Count - 2].Item2)
                    warnings.Add("NonMonotonicObserved");
                if (!crossed && Math.Abs(g) <= CrossingTolerance)
                {
                    firstEndpoint = candidate;
                    firstCrossingG = g;
                    crossed = true;
                }
                if (!crossed && points.Count > 1 && points[points.Count - 2].Item2 < 0 && g >= 0)
                {
                    var refined = Refine(source, c, best, direction, points[points.Count - 2], points[points.Count - 1], baseline, target, step, algorithm, weighted, maxIterations, toleranceModifier, token, global, nearestWarm, ref attempted, ref improved, warnings, ref locations, testEvaluator, trace);
                    if (refined.Outcome != null)
                    {
                        if (refined.Outcome == ProfileSideOutcome.PrimaryMinimumImproved) { improved = true; return new ProfileSideResult(refined.Outcome.Value, evaluationCount: locations, attemptedSolverCalls: attempted - sideAttempts, warnings: warnings); }
                        if (refined.Outcome != ProfileSideOutcome.EndpointFound) return new ProfileSideResult(refined.Outcome.Value, evaluationCount: locations, attemptedSolverCalls: attempted - sideAttempts, warnings: warnings);
                        firstEndpoint = refined.Endpoint;
                        firstCrossingG = refined.CrossingG;
                        if (refined.NuisanceValues != null) nearestWarm = refined.NuisanceValues;
                        crossed = true;
                    }
                }
                if (candidate == bounds[direction < 0 ? 0 : 1])
                {
                    if (!crossed) return new ProfileSideResult(ProfileSideOutcome.BoundReachedBeforeCrossing, evaluationCount: locations, attemptedSolverCalls: attempted - sideAttempts, warnings: warnings);
                    break;
                }
                if (postCrossingLocations >= PostCrossingExpansionLocations)
                    break;
            }
            if (crossed)
            {
                // A short tail beyond the first endpoint can reveal immediate
                // re-entry without driving nuisance refits toward remote bounds.
                if (reentered) warnings.Add("DisconnectedBeyondEndpoint");
                return new ProfileSideResult(ProfileSideOutcome.EndpointFound, firstEndpoint.Value, firstCrossingG, locations, attempted - sideAttempts, warnings);
            }
            var outcome = frontierUnusable && hadOptimizerFailure ? ProfileSideOutcome.OptimizerFailure
                : frontierUnusable && hadNonFinite ? ProfileSideOutcome.NonFiniteCandidate
                : SearchExhaustedOutcome(hadOptimizerFailure, hadNonFinite, successfulPoints);
            return new ProfileSideResult(outcome, evaluationCount: locations, attemptedSolverCalls: attempted - sideAttempts, warnings: warnings);
        }

        static ProfileSideOutcome SearchExhaustedOutcome(bool hadOptimizerFailure, bool hadNonFinite, int successfulPoints)
            => successfulPoints == 0 && hadOptimizerFailure ? ProfileSideOutcome.OptimizerFailure
                : successfulPoints == 0 && hadNonFinite ? ProfileSideOutcome.NonFiniteCandidate
                : ProfileSideOutcome.SearchExhausted;

        struct RefineResult { public ProfileSideOutcome? Outcome; public double Endpoint; public double CrossingG; public Dictionary<string, double> NuisanceValues; }

        static RefineResult Refine(object source, Coordinate c, double best, int direction, Tuple<double, double> a, Tuple<double, double> b, double baseline, double target, double step,
            SolverAlgorithm algorithm, bool weighted, int maxIterations, double toleranceModifier, CancellationToken token, bool global,
            IReadOnlyDictionary<string, double> warmStart, ref int attempted, ref bool improved, List<string> warnings, ref int locations,
            Func<double, IReadOnlyDictionary<string, double>, Candidate> testEvaluator = null,
            Action<ProfileLikelihoodTracePoint> trace = null)
        {
            var lo = a; var hi = b;
            var currentWarmStart = warmStart;
            for (var i = 0; i < MaxBisectionLocations && locations < MaxLocationsPerSide; i++)
            {
                if (token.IsCancellationRequested || SolverInterface.TerminateAnalysisFlag.Up) return new RefineResult { Outcome = ProfileSideOutcome.Cancelled };
                if (Math.Abs(hi.Item1 - lo.Item1) <= Math.Max(1e-10, 1e-6 * Math.Max(step, Math.Abs(best))))
                {
                    // The bracket itself is the numerical result.  Report one
                    // of its evaluated endpoints, rather than inventing a
                    // midpoint likelihood (or a synthetic g=0 value).
                    var endpoint = Math.Abs(lo.Item2) <= Math.Abs(hi.Item2) ? lo : hi;
                    return new RefineResult { Outcome = ProfileSideOutcome.EndpointFound, Endpoint = endpoint.Item1, CrossingG = endpoint.Item2 };
                }
                var x = (lo.Item1 + hi.Item1) * .5;
                var e = testEvaluator != null
                    ? testEvaluator(x, currentWarmStart)
                    : EvaluateCandidate(source, c, x, algorithm, weighted, maxIterations, toleranceModifier, global, currentWarmStart, ref attempted);
                locations++;
                if (!e.Usable)
                {
                    e = testEvaluator != null
                        ? testEvaluator(x, null)
                        : EvaluateCandidate(source, c, x, algorithm, weighted, maxIterations, toleranceModifier, global, null, ref attempted);
                    if (!e.Usable)
                    {
                        trace?.Invoke(new ProfileLikelihoodTracePoint(c?.Id, direction, ProfileLikelihoodTracePhase.Refinement,
                            x, double.NaN, target, false));
                        return new RefineResult { Outcome = e.NonFinite ? ProfileSideOutcome.NonFiniteCandidate : ProfileSideOutcome.OptimizerFailure };
                    }
                }
                var difference = Difference(e.Objective, baseline, e.ObservationCount, weighted);
                var g = difference - target;
                trace?.Invoke(new ProfileLikelihoodTracePoint(c?.Id, direction, ProfileLikelihoodTracePhase.Refinement,
                    x, difference, target, true));
                if (difference < -ImprovedPrimaryTolerance) { improved = true; return new RefineResult { Outcome = ProfileSideOutcome.PrimaryMinimumImproved }; }
                if (Math.Abs(g) <= CrossingTolerance) return new RefineResult { Outcome = ProfileSideOutcome.EndpointFound, Endpoint = x, CrossingG = g, NuisanceValues = e.NuisanceValues };
                if (e.NuisanceValues != null) currentWarmStart = e.NuisanceValues;
                if (g < 0) lo = Tuple.Create(x, g); else hi = Tuple.Create(x, g);
            }
            return new RefineResult { Outcome = ProfileSideOutcome.SearchExhausted };
        }

        internal struct Candidate
        {
            public bool Usable, NonFinite;
            public bool OptimizerFailure;
            public double Objective;
            public int ObservationCount;
            public Dictionary<string, double> NuisanceValues;
        }

        /// <summary>Test-only seam for deterministic side-search failure handling.</summary>
        internal static ProfileSideResult SearchForTesting(
            Func<double, IReadOnlyDictionary<string, double>, Candidate> evaluator,
            double best, double[] bounds, int direction, double step, double baseline, double target)
        {
            if (evaluator == null) throw new ArgumentNullException(nameof(evaluator));
            var attempted = 0;
            var improved = false;
            return Search(null, null, best, bounds, direction, step, baseline, target,
                SolverAlgorithm.NelderMead, false, 1, 1, CancellationToken.None, false,
                ref attempted, ref improved, evaluator);
        }

        static Candidate EvaluateCandidate(object source, Coordinate c, double value, SolverAlgorithm algorithm, bool weighted,
            int maxIterations, double toleranceModifier, bool global, IReadOnlyDictionary<string, double> warmStart, ref int attempted)
        {
            try
            {
                if (global)
                {
                    var original = (GlobalModel)source;
                    var profileOptions = CopyDeterministicOptions(original.ModelCloneOptions, true);
                    var candidate = original.GenerateSyntheticModel(new Random(17), profileOptions);
                    candidate.ModelCloneOptions = ModelCloneOptions.DefaultGlobalOptions;
                    candidate.ModelCloneOptions.ConfigureForRun(ErrorEstimationMethod.None);
                    ApplyWarmStart(candidate, warmStart);
                    var parameter = c.Scope == ParameterBoundaryScope.Shared
                        ? candidate.Parameters.GlobalTable[c.Key]
                        : candidate.Models[((GlobalModel)source).Models.FindIndex(m => m.Data.UniqueID == c.Experiment)].Parameters.Table[c.Key];
                    parameter.Update(value, true);
                    // A fixed shared coordinate must be copied into every
                    // member before evaluating a zero-dimensional candidate;
                    // no optimizer call will perform that synchronization.
                    if (c.Scope == ParameterBoundaryScope.Shared)
                        candidate.Parameters.SetIndividualFromGlobal();
                    var solver = new GlobalSolver { Model = candidate, SolverAlgorithm = algorithm, ErrorEstimationMethod = ErrorEstimationMethod.None,
                        UseErrorWeightedFitting = weighted, MaxOptimizerIterations = maxIterations, SolverToleranceModifier = toleranceModifier,
                        CanCreateAnalysisResult = false, EnableSolverDiagnostics = false, Silent = true };
                    if (candidate.NumberOfParameters > 0) { attempted++; var conv = solver.Solve(); if (conv?.IsUsableForErrorEstimation != true) return new Candidate { OptimizerFailure = true }; }
                    var eval = GaussianLikelihoodEvaluator.Evaluate(candidate, weighted ? GaussianLikelihoodMode.KnownObservationSigmas : GaussianLikelihoodMode.EstimatedCommonVariance);
                    return new Candidate { Usable = eval.IsLikelihoodAvailable || (!weighted && eval.HasFiniteResidualStatistics && FWEMath.IsFinite(eval.RawResidualSumOfSquares) && eval.RawResidualSumOfSquares >= 0), NonFinite = !eval.HasFiniteResidualStatistics, Objective = weighted ? eval.StandardizedResidualSumOfSquares : eval.RawResidualSumOfSquares, ObservationCount = eval.ObservationCount, NuisanceValues = CaptureValues(candidate) };
                }
                else
                {
                    var original = (Model)source;
                    Model candidate;
                    candidate = original.GenerateSyntheticModel(new Random(17), CopyDeterministicOptions(original.ModelCloneOptions, false));
                    candidate.ModelCloneOptions = ModelCloneOptions.DefaultOptions;
                    candidate.ModelCloneOptions.ConfigureForRun(ErrorEstimationMethod.None);
                    ApplyWarmStart(candidate, warmStart);
                    var parameter = candidate.Parameters.Table[c.Key];
                    parameter.Update(value, true);
                    var solver = new Solver { Model = candidate, SolverAlgorithm = algorithm, ErrorEstimationMethod = ErrorEstimationMethod.None,
                        UseErrorWeightedFitting = weighted, MaxOptimizerIterations = maxIterations, SolverToleranceModifier = toleranceModifier,
                        CanCreateAnalysisResult = false, EnableSolverDiagnostics = false, Silent = true };
                    if (candidate.NumberOfParameters > 0) { attempted++; var conv = solver.Solve(); if (conv?.IsUsableForErrorEstimation != true) return new Candidate { OptimizerFailure = true }; }
                    var eval = GaussianLikelihoodEvaluator.Evaluate(candidate, weighted ? GaussianLikelihoodMode.KnownObservationSigmas : GaussianLikelihoodMode.EstimatedCommonVariance);
                    return new Candidate { Usable = eval.IsLikelihoodAvailable || (!weighted && eval.HasFiniteResidualStatistics && FWEMath.IsFinite(eval.RawResidualSumOfSquares) && eval.RawResidualSumOfSquares >= 0), NonFinite = !eval.HasFiniteResidualStatistics, Objective = weighted ? eval.StandardizedResidualSumOfSquares : eval.RawResidualSumOfSquares, ObservationCount = eval.ObservationCount, NuisanceValues = CaptureValues(candidate) };
                }
            }
            catch (OptimizerStopException) { return new Candidate { OptimizerFailure = true }; }
            catch { return new Candidate { OptimizerFailure = true }; }
        }

        static ModelCloneOptions CopyDeterministicOptions(ModelCloneOptions source, bool global)
            => new ModelCloneOptions
            {
                IsGlobalClone = global,
                ErrorEstimationMethod = ErrorEstimationMethod.ProfileLikelihood,
                IncludeConcentrationErrorsInBootstrap = false,
                EnableAutoConcentrationVariance = source?.EnableAutoConcentrationVariance ?? false,
                AutoConcentrationVariance = source?.AutoConcentrationVariance ?? 0.05,
                DiscardedDataPoint = source?.DiscardedDataPoint ?? 0,
                UnlockBootstrapParameters = false,
            };

        static string LocalKey(string experiment, ParameterType parameter) => $"L|{experiment ?? string.Empty}|{parameter}";
        static string SharedKey(ParameterType parameter) => $"S||{parameter}";

        static Dictionary<string, double> CaptureValues(Model model)
            => model.Parameters.Table.ToDictionary(x => LocalKey(model.Data?.UniqueID, x.Key), x => x.Value.Value);

        static Dictionary<string, double> CaptureValues(GlobalModel model)
        {
            var values = model.Parameters.GlobalTable.ToDictionary(x => SharedKey(x.Key), x => x.Value.Value);
            foreach (var member in model.Models)
                foreach (var p in member.Parameters.Table)
                    values[LocalKey(member.Data?.UniqueID, p.Key)] = p.Value.Value;
            return values;
        }

        static void ApplyWarmStart(Model model, IReadOnlyDictionary<string, double> warm)
        {
            if (warm == null) return;
            foreach (var p in model.Parameters.Table)
                if (warm.TryGetValue(LocalKey(model.Data?.UniqueID, p.Key), out var value) && p.Value.IsFitted) p.Value.Update(value);
        }

        static void ApplyWarmStart(GlobalModel model, IReadOnlyDictionary<string, double> warm)
        {
            if (warm == null) return;
            foreach (var p in model.Parameters.GlobalTable)
                if (warm.TryGetValue(SharedKey(p.Key), out var value) && p.Value.IsFitted) p.Value.Update(value);
            foreach (var member in model.Models) ApplyWarmStart(member, warm);
            model.Parameters.SetIndividualFromGlobal();
        }

        static double Evaluate(Model model, bool weighted)
        {
            var e = GaussianLikelihoodEvaluator.Evaluate(model, weighted ? GaussianLikelihoodMode.KnownObservationSigmas : GaussianLikelihoodMode.EstimatedCommonVariance);
            return weighted ? e.StandardizedResidualSumOfSquares : e.RawResidualSumOfSquares;
        }
        static double Evaluate(GlobalModel model, bool weighted)
        {
            var e = GaussianLikelihoodEvaluator.Evaluate(model, weighted ? GaussianLikelihoodMode.KnownObservationSigmas : GaussianLikelihoodMode.EstimatedCommonVariance);
            return weighted ? e.StandardizedResidualSumOfSquares : e.RawResidualSumOfSquares;
        }
        static double Difference(double candidate, double baseline, int n, bool weighted)
            => weighted ? candidate - baseline : n * (Math.Log(candidate) - Math.Log(baseline));
        static bool ValidBaseline(double value, bool weighted) => FWEMath.IsFinite(value) && (weighted ? value >= 0 : value > 0);
        static double Target(int n, int df, bool weighted)
        {
            if (weighted) return CalculateWeightedTarget(ConfidenceLevel);
            return CalculateUnweightedTarget(n, df, ConfidenceLevel);
        }
        static double GetValue(Model m, Coordinate c) => m.Parameters.Table[c.Key].Value;
        static double GetValue(GlobalModel m, Coordinate c) => c.Scope == ParameterBoundaryScope.Shared ? m.Parameters.GlobalTable[c.Key].Value : m.Models[m.Models.FindIndex(x => x.Data.UniqueID == c.Experiment)].Parameters.Table[c.Key].Value;
        static double[] GetBounds(Model m, Coordinate c) => m.Parameters.Table[c.Key].Limits;
        static double[] GetBounds(GlobalModel m, Coordinate c) => c.Scope == ParameterBoundaryScope.Shared ? m.Parameters.GlobalTable[c.Key].Limits : m.Models[m.Models.FindIndex(x => x.Data.UniqueID == c.Experiment)].Parameters.Table[c.Key].Limits;
        static double GetStep(Model m, Coordinate c) => m.Parameters.Table[c.Key].StepSize;
        static double GetStep(GlobalModel m, Coordinate c) => c.Scope == ParameterBoundaryScope.Shared ? m.Parameters.GlobalTable[c.Key].StepSize : m.Models[m.Models.FindIndex(x => x.Data.UniqueID == c.Experiment)].Parameters.Table[c.Key].StepSize;
    }
}
