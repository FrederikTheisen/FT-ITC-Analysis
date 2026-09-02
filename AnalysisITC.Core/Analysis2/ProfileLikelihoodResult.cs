using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Analysis
{
    public sealed class ProfileLikelihoodProgress
    {
        public int CompletedSides { get; }
        public int TotalSides { get; }
        public int EndpointsFound { get; }
        public int AttemptedSolverCalls { get; }

        public ProfileLikelihoodProgress(int completedSides, int totalSides, int endpointsFound, int attemptedSolverCalls)
        {
            CompletedSides = Math.Max(0, completedSides);
            TotalSides = Math.Max(0, totalSides);
            EndpointsFound = Math.Max(0, endpointsFound);
            AttemptedSolverCalls = Math.Max(0, attemptedSolverCalls);
        }
    }

    public sealed class ProfileLikelihoodSummary
    {
        public ErrorEstimationOutcome Outcome { get; }
        public int EndpointsFound { get; }
        public int TotalSides { get; }
        public TimeSpan Elapsed { get; }
        public string Diagnostics { get; }

        internal ProfileLikelihoodSummary(ErrorEstimationOutcome outcome, int endpointsFound, int totalSides,
            TimeSpan elapsed, string diagnostics)
        {
            Outcome = outcome;
            EndpointsFound = endpointsFound;
            TotalSides = totalSides;
            Elapsed = elapsed;
            Diagnostics = diagnostics ?? string.Empty;
        }
    }

    public enum ProfileLikelihoodCalibration
    {
        UnweightedFCalibratedRss,
        WeightedChiSquared,
    }

    public enum ProfileSideOutcome
    {
        EndpointFound,
        BoundReachedBeforeCrossing,
        SearchExhausted,
        OptimizerFailure,
        NonFiniteCandidate,
        Cancelled,
        PrimaryMinimumImproved,
    }

    public sealed class ProfileCoordinateId : IEquatable<ProfileCoordinateId>
    {
        public ParameterType Parameter { get; }
        public ParameterType ParameterKey => Parameter;
        public ParameterBoundaryScope Scope { get; }
        public bool IsShared => Scope == ParameterBoundaryScope.Shared;
        public string ExperimentIdentity { get; }
        public string ExperimentId => ExperimentIdentity;
        public int PrimaryOptimizerIndex { get; }

        public ProfileCoordinateId(ParameterType parameter, ParameterBoundaryScope scope,
            string experimentIdentity = null, int primaryOptimizerIndex = -1)
        {
            Parameter = parameter;
            Scope = scope;
            ExperimentIdentity = experimentIdentity;
            PrimaryOptimizerIndex = primaryOptimizerIndex;
        }

        public bool Equals(ProfileCoordinateId other)
            => other != null && Parameter == other.Parameter && Scope == other.Scope
                && string.Equals(ExperimentIdentity, other.ExperimentIdentity, StringComparison.Ordinal);
        public override bool Equals(object obj) => Equals(obj as ProfileCoordinateId);
        public override int GetHashCode() => (Parameter, Scope, ExperimentIdentity ?? string.Empty).GetHashCode();
        public override string ToString() => IsShared ? Parameter.ToString() : $"{ExperimentIdentity}:{Parameter}";
    }

    public sealed class ProfileSideResult
    {
        public ProfileSideOutcome Outcome { get; }
        public double Endpoint { get; }
        public double CrossingValue => Endpoint;
        public double CrossingG { get; }
        public int EvaluationCount { get; }
        public int AttemptedSolverCalls { get; }
        public IReadOnlyList<string> Warnings { get; }
        public bool IsEndpointFound => Outcome == ProfileSideOutcome.EndpointFound;

        public ProfileSideResult(ProfileSideOutcome outcome, double endpoint = double.NaN,
            double crossingG = double.NaN, int evaluationCount = 0, int attemptedSolverCalls = 0,
            IEnumerable<string> warnings = null)
        {
            Outcome = outcome;
            Endpoint = endpoint;
            CrossingG = crossingG;
            EvaluationCount = Math.Max(0, evaluationCount);
            AttemptedSolverCalls = Math.Max(0, attemptedSolverCalls);
            Warnings = new ReadOnlyCollection<string>((warnings ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList());
        }
    }

    public sealed class ProfileCoordinateResult
    {
        public ProfileCoordinateId Id { get; }
        public ProfileCoordinateId Coordinate => Id;
        public double BestValue { get; }
        public IReadOnlyList<double> EffectiveBounds { get; }
        public double LowerBound => EffectiveBounds[0];
        public double UpperBound => EffectiveBounds[1];
        public ProfileSideResult Lower { get; }
        public ProfileSideResult LowerSide => Lower;
        public ProfileSideResult LowerSideResult => Lower;
        public ProfileSideResult Upper { get; }
        public ProfileSideResult UpperSide => Upper;
        public ProfileSideResult UpperSideResult => Upper;
        public bool HasCompleteInterval => Lower.IsEndpointFound && Upper.IsEndpointFound;
        public double[] Interval => HasCompleteInterval ? new[] { Lower.Endpoint, Upper.Endpoint } : null;
        public IReadOnlyList<string> ShapeWarnings { get; }

        public ProfileCoordinateResult(ProfileCoordinateId id, double bestValue, double lowerBound, double upperBound,
            ProfileSideResult lower, ProfileSideResult upper, IEnumerable<string> shapeWarnings = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            BestValue = bestValue;
            EffectiveBounds = new ReadOnlyCollection<double>(new[] { lowerBound, upperBound });
            Lower = lower ?? throw new ArgumentNullException(nameof(lower));
            Upper = upper ?? throw new ArgumentNullException(nameof(upper));
            ShapeWarnings = new ReadOnlyCollection<string>((shapeWarnings ?? Enumerable.Empty<string>()).ToList());
        }

        public FloatWithError ToFloatWithError()
        {
            if (!HasCompleteInterval) return new FloatWithError(BestValue);
            const double z = 1.959963984540054;
            var lower = Lower.Endpoint;
            var upper = Upper.Endpoint;
            var sL = (BestValue - lower) / z;
            var sU = (upper - BestValue) / z;
            var sd = Math.Sqrt(Math.Max(0, sL * sL - sL * sU + sU * sU));
            return new FloatWithError(BestValue, sd, lower, upper);
        }

        public FloatWithError Transform(Func<double, double> transform)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            if (!HasCompleteInterval) return new FloatWithError(transform(BestValue));
            var value = transform(BestValue);
            var a = transform(Lower.Endpoint);
            var b = transform(Upper.Endpoint);
            var lower = Math.Min(a, b);
            var upper = Math.Max(a, b);
            const double z = 1.959963984540054;
            var left = (value - lower) / z;
            var right = (upper - value) / z;
            var sd = Math.Sqrt(Math.Max(0, left * left - left * right + right * right));
            return new FloatWithError(value, sd, lower, upper);
        }
    }

    public sealed class ProfileLikelihoodRunResult
    {
        public double ConfidenceLevel { get; }
        public ProfileLikelihoodCalibration Calibration { get; }
        public int ObservationCount { get; }
        public int ParameterCount { get; }
        public int N => ObservationCount;
        public int P => ParameterCount;
        public int Q { get; } = 1;
        public int DegreesOfFreedom { get; }
        public int Df => DegreesOfFreedom;
        public double BaselineObjective { get; }
        public double TargetIncrement { get; }
        public SolverAlgorithm Algorithm { get; }
        public bool UseWeightedFitting { get; }
        public double Tolerance { get; }
        public double OptimizerToleranceSetting { get; }
        public int CandidateIterationLimit { get; }
        public int ExpansionLimit { get; }
        public int RefinementLimit { get; }
        public TimeSpan Elapsed { get; }
        public TimeSpan ElapsedTime => Elapsed;
        public ErrorEstimationOutcome Outcome { get; }
        public IReadOnlyList<ProfileCoordinateResult> Coordinates { get; }
        public IReadOnlyList<ProfileCoordinateResult> CoordinateResults => Coordinates;
        public int AttemptedSolverCalls { get; }
        public string CalibrationDescription { get; }
        public bool HasCompleteIntervals => Coordinates.Any(c => c.HasCompleteInterval);

        public ProfileLikelihoodRunResult(double confidenceLevel, ProfileLikelihoodCalibration calibration,
            int n, int p, int q, int df, double baselineObjective, double targetIncrement,
            SolverAlgorithm algorithm, bool weighted, double tolerance, int candidateIterationLimit,
            int expansionLimit, int refinementLimit, TimeSpan elapsed, ErrorEstimationOutcome outcome,
            IEnumerable<ProfileCoordinateResult> coordinates, int attemptedSolverCalls = 0)
            : this(confidenceLevel, calibration, n, p, q, df, baselineObjective, targetIncrement,
                algorithm, weighted, tolerance, candidateIterationLimit, expansionLimit, refinementLimit,
                elapsed, outcome, coordinates, attemptedSolverCalls, AppSettings.OptimizerTolerance)
        {
        }

        public ProfileLikelihoodRunResult(double confidenceLevel, ProfileLikelihoodCalibration calibration,
            int n, int p, int q, int df, double baselineObjective, double targetIncrement,
            SolverAlgorithm algorithm, bool weighted, double tolerance, int candidateIterationLimit,
            int expansionLimit, int refinementLimit, TimeSpan elapsed, ErrorEstimationOutcome outcome,
            IEnumerable<ProfileCoordinateResult> coordinates, int attemptedSolverCalls,
            double optimizerToleranceSetting)
        {
            ConfidenceLevel = confidenceLevel;
            Calibration = calibration;
            ObservationCount = n;
            ParameterCount = p;
            Q = q;
            DegreesOfFreedom = df;
            BaselineObjective = baselineObjective;
            TargetIncrement = targetIncrement;
            Algorithm = algorithm;
            UseWeightedFitting = weighted;
            Tolerance = tolerance;
            OptimizerToleranceSetting = optimizerToleranceSetting;
            CandidateIterationLimit = candidateIterationLimit;
            ExpansionLimit = expansionLimit;
            RefinementLimit = refinementLimit;
            Elapsed = elapsed;
            Outcome = outcome;
            Coordinates = new ReadOnlyCollection<ProfileCoordinateResult>((coordinates ?? Enumerable.Empty<ProfileCoordinateResult>()).ToList());
            AttemptedSolverCalls = attemptedSolverCalls;
            CalibrationDescription = calibration == ProfileLikelihoodCalibration.WeightedChiSquared
                ? "Conditional on supplied peak-area SDs."
                : "F-calibrated RSS interval under independent Gaussian residual assumptions.";
        }
    }
}
