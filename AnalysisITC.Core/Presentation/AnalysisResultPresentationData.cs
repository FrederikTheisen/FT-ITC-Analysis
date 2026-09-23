using System;
using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Presentation
{
    /// <summary>
    /// An immutable, display-oriented snapshot of a result.  Reading
    /// <see cref="SolutionInterface.ReportParameters"/> can evaluate linked
    /// uncertainties, so desktop views share this snapshot until the solution changes.
    /// </summary>
    public sealed class AnalysisResultPresentationData
    {
        readonly Lazy<List<AnalysisResultPresentationMember>> members;
        readonly Lazy<List<ParameterType>> parameters;
        readonly object cacheLock = new object();
        readonly Dictionary<int, BootstrapCorrelationResult> correlations = new();
        readonly Dictionary<PresentationEnvelopeKey, IReadOnlyList<FitEnvelopePoint>> envelopes = new();
        readonly Dictionary<PresentationSummaryKey, AggregateParameterSummary> summaries = new();

        public AnalysisResultPresentationData(AnalysisResult result)
            : this(result, deferMemberPreparation: false) { }

        internal AnalysisResultPresentationData(AnalysisResult result, bool deferMemberPreparation)
        {
            Result = result;
            var solution = result?.Solution;
            members = new Lazy<List<AnalysisResultPresentationMember>>(() =>
                (solution?.Solutions ?? new List<SolutionInterface>())
                    .Where(member => member != null)
                    .Select(member => new AnalysisResultPresentationMember(member)).ToList());
            parameters = new Lazy<List<ParameterType>>(() => Members
                .SelectMany(member => member.Parameters.Keys).Distinct().ToList());
            if (!deferMemberPreparation) _ = Members;
        }

        public AnalysisResult Result { get; }
        public IReadOnlyList<AnalysisResultPresentationMember> Members => members.Value;
        public IReadOnlyList<ParameterType> Parameters => parameters.Value;

        /// <summary>Computes correlations only when requested, once per member selection.</summary>
        public BootstrapCorrelationResult GetCorrelation(SolutionInterface selected = null)
        {
            var solutions = Result?.Solution?.Solutions ?? new List<SolutionInterface>();
            var index = selected == null ? -1 : solutions.IndexOf(selected);
            lock (cacheLock)
            {
                if (correlations.TryGetValue(index, out var cached)) return cached;
                var analyzer = new BootstrapCorrelationAnalyzer();
                var value = solutions.Count == 1 ? analyzer.Analyze(solutions[0])
                    : index >= 0 ? analyzer.Analyze(Result.Solution, solutions[index])
                    : analyzer.Analyze(Result);
                correlations[index] = value;
                return value;
            }
        }

        internal int CachedEnvelopeCount { get { lock (cacheLock) return envelopes.Count; } }
        internal int CachedCorrelationCount { get { lock (cacheLock) return correlations.Count; } }

        internal AggregateParameterSummary GetSummary(ParameterType parameter, double temperatureCelsius)
        {
            var key = new PresentationSummaryKey(parameter, temperatureCelsius);
            lock (cacheLock)
            {
                if (summaries.TryGetValue(key, out var cached)) return cached;
                var value = new AnalysisResultAggregateSummaryCalculator(Result, bypassPresentationCache: true)
                    .EvaluateSummaryParameter(parameter, temperatureCelsius);
                summaries[key] = value;
                return value;
            }
        }

        public IReadOnlyList<FitEnvelopePoint> GetTemperatureEnvelope(
            ParameterType parameter, double minimumCelsius, double maximumCelsius,
            int intervals = FitEnvelopeBuilder.DefaultSampleIntervals)
        {
            var key = new PresentationEnvelopeKey(parameter, minimumCelsius, maximumCelsius, intervals);
            lock (cacheLock)
            {
                if (envelopes.TryGetValue(key, out var cached)) return cached;
                var samples = FitEnvelopeBuilder.SampleDomain(minimumCelsius, maximumCelsius, intervals);
                var value = new AnalysisResultAggregateSummaryCalculator(Result, bypassPresentationCache: true)
                    .BuildEnvelope(parameter, samples).ToArray();
                envelopes[key] = value;
                return value;
            }
        }

        public EnergyUnit ResolveMolarEnergyUnit(EnergyUnitFamily family, EnergyUnit? overrideUnit = null)
            => EnergyUnitResolver.Resolve(family, overrideUnit, Members
                .SelectMany(member => member.Parameters)
                .Where(item => IsMolarEnergy(item.Key))
                .Select(item => item.Value.Value)
                .Concat(Result?.IsProtonationAnalysisEnabled == true
                    ? Members.Where(member => BufferAttribute.TryGetProtonationEnthalpy(member.Solution.Data, out _))
                        .Select(member => BufferAttribute.GetProtonationEnthalpy(member.Solution.Data).Value)
                    : Enumerable.Empty<double>()));

        public EnergyUnit ResolveHeatCapacityUnit(EnergyUnitFamily family, EnergyUnit? overrideUnit = null)
            => EnergyUnitResolver.Resolve(family, overrideUnit, Members
                .SelectMany(member => member.Parameters)
                .Where(item => IsHeatCapacity(item.Key))
                .Select(item => item.Value.Value)
                .Concat(Result?.Solution?.TemperatureDependence?.Values
                    .Select(dependence => dependence.Slope.Value) ?? Enumerable.Empty<double>()));

        public ConcentrationUnit ResolveAffinityUnit(ParameterType parameter)
        {
            var values = Members
                .Where(member => member.Parameters.TryGetValue(parameter, out _))
                .Select(member => Math.Abs(member.Parameters[parameter].Value))
                .Where(value => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0)
                .ToList();
            return values.Count == 0
                ? AppSettings.DefaultConcentrationUnit
                : ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(values.Average());
        }

        public static bool IsHeatCapacity(ParameterType parameter) =>
            parameter.GetProperties().ParentType == ParameterType.HeatCapacity1;

        public static bool IsMolarEnergy(ParameterType parameter) =>
            ParameterTypeAttribute.IsEnergyUnitParameter(parameter) && !IsHeatCapacity(parameter);
    }

    readonly struct PresentationSummaryKey : IEquatable<PresentationSummaryKey>
    {
        readonly ParameterType parameter;
        readonly double temperature;
        internal PresentationSummaryKey(ParameterType parameter, double temperature)
        { this.parameter = parameter; this.temperature = temperature; }
        public bool Equals(PresentationSummaryKey other) => parameter == other.parameter && temperature.Equals(other.temperature);
        public override bool Equals(object obj) => obj is PresentationSummaryKey other && Equals(other);
        public override int GetHashCode() => ((int)parameter * 397) ^ temperature.GetHashCode();
    }

    readonly struct PresentationEnvelopeKey : IEquatable<PresentationEnvelopeKey>
    {
        readonly ParameterType parameter;
        readonly double minimum;
        readonly double maximum;
        readonly int intervals;
        internal PresentationEnvelopeKey(ParameterType parameter, double minimum, double maximum, int intervals)
        { this.parameter = parameter; this.minimum = minimum; this.maximum = maximum; this.intervals = intervals; }
        public bool Equals(PresentationEnvelopeKey other) => parameter == other.parameter
            && minimum.Equals(other.minimum) && maximum.Equals(other.maximum) && intervals == other.intervals;
        public override bool Equals(object obj) => obj is PresentationEnvelopeKey other && Equals(other);
        public override int GetHashCode() => ((((int)parameter * 397) ^ minimum.GetHashCode()) * 397 ^ maximum.GetHashCode()) * 397 ^ intervals;
    }

    public sealed class AnalysisResultPresentationMember
    {
        public AnalysisResultPresentationMember(SolutionInterface solution)
        {
            Solution = solution;
            TemperatureCelsius = solution.Temp;
            var parameters = new Dictionary<ParameterType, FloatWithError>(solution.ReportParameters
                ?? new Dictionary<ParameterType, FloatWithError>());
            // Offset is intentionally not a model report parameter, but is shown
            // beside them in desktop result tables.
            if (solution.Parameters != null && solution.Parameters.TryGetValue(ParameterType.Offset, out var offset))
                parameters[ParameterType.Offset] = offset;
            Parameters = parameters;
        }

        public SolutionInterface Solution { get; }
        public double TemperatureCelsius { get; }
        public IReadOnlyDictionary<ParameterType, FloatWithError> Parameters { get; }
    }
}
