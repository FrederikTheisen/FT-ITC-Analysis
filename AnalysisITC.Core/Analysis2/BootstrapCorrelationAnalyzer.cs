using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Analysis
{
    /// <summary>Identifies whether a bootstrap correlation result can be used.</summary>
    public enum BootstrapCorrelationAvailabilityStatus
    {
        Available,
        NoResidualBootstrap,
        NoBootstrapReplicates,
        TooFewCompleteReplicates,
        InsufficientReplicates = TooFewCompleteReplicates,
        TooFewVaryingParameters,
        InsufficientVaryingParameters = TooFewVaryingParameters,
    }

    /// <summary>
    /// Availability and diagnostics for a bootstrap correlation calculation.
    /// </summary>
    public sealed class BootstrapCorrelationAvailability
    {
        public BootstrapCorrelationAvailabilityStatus Status { get; private set; }
        public bool IsAvailable => Status == BootstrapCorrelationAvailabilityStatus.Available;
        public bool CanCalculate => IsAvailable;
        public string Reason { get; private set; }
        public string Message => Reason;
        public int CompleteReplicateCount { get; private set; }
        public int RequiredReplicateCount { get; private set; }
        public int VaryingParameterCount { get; private set; }

        internal BootstrapCorrelationAvailability(
            BootstrapCorrelationAvailabilityStatus status,
            string reason,
            int completeReplicateCount,
            int requiredReplicateCount,
            int varyingParameterCount)
        {
            Status = status;
            Reason = reason ?? string.Empty;
            CompleteReplicateCount = completeReplicateCount;
            RequiredReplicateCount = requiredReplicateCount;
            VaryingParameterCount = varyingParameterCount;
        }
    }

    public enum BootstrapCorrelationParameterScope
    {
        Single,
        Shared,
        Member,
    }

    public sealed class BootstrapCorrelationCellDiagnostic
    {
        public double PearsonR { get; private set; }
        public int CompleteReplicateCount { get; private set; }
        public bool IsSelfCorrelation { get; private set; }
        public double? MonteCarloPrecisionLower { get; private set; }
        public double? MonteCarloPrecisionUpper { get; private set; }
        public bool HasMonteCarloPrecisionInterval => MonteCarloPrecisionLower.HasValue && MonteCarloPrecisionUpper.HasValue;
        public bool IsSignUncertain { get; private set; }

        internal BootstrapCorrelationCellDiagnostic(
            double pearsonR,
            int completeReplicateCount,
            bool isSelfCorrelation,
            double? lower,
            double? upper)
        {
            PearsonR = pearsonR;
            CompleteReplicateCount = completeReplicateCount;
            IsSelfCorrelation = isSelfCorrelation;
            MonteCarloPrecisionLower = lower;
            MonteCarloPrecisionUpper = upper;
            IsSignUncertain = !isSelfCorrelation && lower.HasValue && upper.HasValue
                && lower.Value <= 0 && upper.Value >= 0;
        }
    }

    public sealed class BootstrapCorrelationReliability
    {
        public const int RecommendedCompleteReplicates = 100;
        public const double FrequentFailureFraction = 0.20;

        public int? AttemptedRefitCount { get; private set; }
        public int UsableRefitCount { get; private set; }
        public int? FailedRefitCount { get; private set; }
        public int CompleteRefitCount { get; private set; }
        public int CoordinateIncompleteRefitCount { get; private set; }
        public double? FailureFraction => AttemptedRefitCount > 0 && FailedRefitCount.HasValue
            ? (double)FailedRefitCount.Value / AttemptedRefitCount.Value : (double?)null;
        public bool HasCoarseMonteCarloPrecision { get; private set; }
        public bool HasFrequentFailures => FailureFraction >= FrequentFailureFraction;
        public int UncertainSignPairCount { get; private set; }
        public bool HasUncertainSigns => UncertainSignPairCount > 0;

        internal BootstrapCorrelationReliability(
            SolverConvergence convergence,
            int usableRefits,
            int completeRefits,
            int uncertainSignPairs,
            bool isAvailable)
        {
            AttemptedRefitCount = convergence?.ErrorEstimationAttemptedRefits;
            FailedRefitCount = convergence?.ErrorEstimationFailedRefits;
            UsableRefitCount = Math.Max(0, usableRefits);
            CompleteRefitCount = Math.Max(0, completeRefits);
            CoordinateIncompleteRefitCount = Math.Max(0, UsableRefitCount - CompleteRefitCount);
            UncertainSignPairCount = Math.Max(0, uncertainSignPairs);
            HasCoarseMonteCarloPrecision = isAvailable && CompleteRefitCount < RecommendedCompleteReplicates;
        }
    }

    /// <summary>
    /// Metadata for one coordinate in a correlation matrix. Values are always in
    /// the fitted coordinate system (for example Affinity is log10(Ka), not Kd).
    /// </summary>
    public sealed class BootstrapCorrelationParameterDescriptor
    {
        public ParameterType ParameterType { get; private set; }
        public ParameterType Key => ParameterType;
        public string Label { get; private set; }
        public string DisplayName => Label;
        public string Name => Label;
        public BootstrapCorrelationParameterScope Scope { get; private set; }
        public bool IsShared => Scope == BootstrapCorrelationParameterScope.Shared;
        public bool IsMember => Scope == BootstrapCorrelationParameterScope.Member;
        public bool IsGlobal => IsShared;
        public int SlotIndex { get; private set; }
        public int? MemberIndex { get; private set; }
        public string MemberId { get; private set; }
        public string MemberName { get; private set; }
        public bool WasOriginallyLocked { get; private set; }
        public bool OriginallyLocked => WasOriginallyLocked;
        public bool IncludedBecauseBootstrapUnlock { get; private set; }
        public bool IsDerivedGlobalCoordinate { get; private set; }

        internal BootstrapCorrelationParameterDescriptor(
            ParameterType parameterType,
            string label,
            BootstrapCorrelationParameterScope scope,
            int slotIndex,
            int? memberIndex,
            string memberId,
            string memberName,
            bool wasOriginallyLocked,
            bool includedBecauseBootstrapUnlock,
            bool isDerivedGlobalCoordinate)
        {
            ParameterType = parameterType;
            Label = label;
            Scope = scope;
            SlotIndex = slotIndex;
            MemberIndex = memberIndex;
            MemberId = memberId;
            MemberName = memberName;
            WasOriginallyLocked = wasOriginallyLocked;
            IncludedBecauseBootstrapUnlock = includedBecauseBootstrapUnlock;
            IsDerivedGlobalCoordinate = isDerivedGlobalCoordinate;
        }
    }

    /// <summary>
    /// Pearson correlation calculated from residual-bootstrap fitted coordinates.
    /// </summary>
    public sealed class BootstrapCorrelationResult
    {
        public BootstrapCorrelationAvailability Availability { get; private set; }
        public bool IsAvailable => Availability != null && Availability.IsAvailable;
        public IReadOnlyList<BootstrapCorrelationParameterDescriptor> Parameters { get; private set; }
        public IReadOnlyList<BootstrapCorrelationParameterDescriptor> Descriptors => Parameters;
        public IReadOnlyList<BootstrapCorrelationParameterDescriptor> ParameterDescriptors => Parameters;
        public IReadOnlyList<double[]> CompleteReplicateCoordinates { get; private set; }
        public IReadOnlyList<double[]> Coordinates => CompleteReplicateCoordinates;
        public int CompleteReplicateCount => CompleteReplicateCoordinates.Count;
        public int UsedReplicateCount => CompleteReplicateCount;
        public double[,] CorrelationMatrix { get; private set; }
        public double[,] Correlations => CorrelationMatrix;
        public double[,] Matrix => CorrelationMatrix;
        public double[,] Correlation => CorrelationMatrix;
        public BootstrapCorrelationCellDiagnostic[,] CellDiagnostics { get; private set; }
        public BootstrapCorrelationReliability Reliability { get; private set; }
        public IReadOnlyList<BootstrapCorrelationParameterDescriptor> OmittedParameters { get; private set; }
        public int OmittedParameterCount => OmittedParameters.Count;
        /// <summary>Whether B <= parameter count limits covariance rank.</summary>
        public bool IsRankLimited { get; private set; }
        public bool RankLimited => IsRankLimited;

        internal BootstrapCorrelationResult(
            BootstrapCorrelationAvailability availability,
            IReadOnlyList<BootstrapCorrelationParameterDescriptor> parameters,
            IReadOnlyList<double[]> coordinates,
            double[,] correlationMatrix,
            BootstrapCorrelationCellDiagnostic[,] cellDiagnostics,
            IReadOnlyList<BootstrapCorrelationParameterDescriptor> omittedParameters,
            bool isRankLimited,
            BootstrapCorrelationReliability reliability)
        {
            Availability = availability;
            Parameters = parameters ?? new List<BootstrapCorrelationParameterDescriptor>();
            CompleteReplicateCoordinates = coordinates ?? new List<double[]>();
            CorrelationMatrix = correlationMatrix;
            CellDiagnostics = cellDiagnostics;
            OmittedParameters = omittedParameters ?? new List<BootstrapCorrelationParameterDescriptor>();
            IsRankLimited = isRankLimited;
            Reliability = reliability;
        }
    }

    /// <summary>
    /// Computes parameter correlations from residual bootstrap refits. This class
    /// intentionally reads only the in-memory result object graph; no persistence
    /// schema changes are needed for the global reconstruction fallback.
    /// </summary>
    public sealed class BootstrapCorrelationAnalyzer
    {
        public const int DefaultMinimumCompleteReplicates = 30;
        public int MinimumCompleteReplicates { get; private set; }

        public BootstrapCorrelationAnalyzer(int minimumCompleteReplicates = DefaultMinimumCompleteReplicates)
        {
            if (minimumCompleteReplicates < 1)
                throw new ArgumentOutOfRangeException(nameof(minimumCompleteReplicates));
            MinimumCompleteReplicates = minimumCompleteReplicates;
        }

        public BootstrapCorrelationResult Analyze(SolutionInterface solution, int? selectedMemberIndex = null)
        {
            if (solution == null) throw new ArgumentNullException(nameof(solution));
            if (!IsResidualBootstrap(solution))
                return Unavailable(BootstrapCorrelationAvailabilityStatus.NoResidualBootstrap,
                    "Parameter correlation requires residual bootstrap replicates.");

            var primary = solution.Model;
            if (primary == null)
                return Unavailable(BootstrapCorrelationAvailabilityStatus.NoBootstrapReplicates, "The fitted model is unavailable.");

            var unlock = primary.ModelCloneOptions?.UnlockBootstrapParameters == true;
            var candidates = new List<Candidate>();
            var hasMultipleSlots = HasMultipleSlots(primary.Parameters.Table.Keys);
            foreach (var p in primary.Parameters.Table.Values)
            {
                if (!IsCorrelationCoordinate(p.Key, primary.ModelType)) continue;
                if (!p.IsFitted && !(unlock && p.IsLocked)) continue;
                candidates.Add(new Candidate(
                    Descriptor(p.Key, BootstrapCorrelationParameterScope.Single, null, primary, p.IsLocked,
                        unlock && p.IsLocked, false, hasMultipleSlots),
                    row => ParameterValue(row, p.Key),
                    primary.Data.UniqueID));
            }

            var rows = solution.BootstrapSolutions ?? new List<SolutionInterface>();
            if (rows.Count == 0)
                return Build(candidates, new List<double[]>(), BootstrapCorrelationAvailabilityStatus.NoBootstrapReplicates,
                    "No residual bootstrap replicates are available.", solution.Convergence, 0);

            var coordinates = CompleteRows(candidates, rows.Select(r => (object)r));
            return Build(candidates, coordinates, null, null, solution.Convergence,
                rows.Count(row => row != null));
        }

        public BootstrapCorrelationResult Analyze(GlobalSolution solution, int? selectedMemberIndex = null)
        {
            if (solution == null) throw new ArgumentNullException(nameof(solution));
            if (!IsResidualBootstrap(solution))
                return Unavailable(BootstrapCorrelationAvailabilityStatus.NoResidualBootstrap,
                    "Parameter correlation requires residual bootstrap replicates.");

            var primary = solution.Model;
            if (primary == null || primary.Models == null || primary.Models.Count == 0)
                return Unavailable(BootstrapCorrelationAvailabilityStatus.NoBootstrapReplicates, "The global fitted model is unavailable.");
            if (selectedMemberIndex.HasValue && (selectedMemberIndex.Value < 0 || selectedMemberIndex.Value >= primary.Models.Count))
                throw new ArgumentOutOfRangeException(nameof(selectedMemberIndex));

            var unlock = primary.ModelCloneOptions?.UnlockBootstrapParameters == true
                || primary.Models.Any(m => m.ModelCloneOptions?.UnlockBootstrapParameters == true);
            var candidates = new List<Candidate>();
            var sharedKeys = primary.Parameters?.GlobalTable?.Values ?? Enumerable.Empty<Parameter>();
            var hasMultipleSharedSlots = HasMultipleSlots(sharedKeys.Select(p => p.Key));
            foreach (var p in sharedKeys)
            {
                if (!IsCorrelationCoordinate(p.Key, primary.ModelType)) continue;
                if (!p.IsFitted && !(unlock && p.IsLocked)) continue;
                candidates.Add(new Candidate(
                    Descriptor(p.Key, BootstrapCorrelationParameterScope.Shared, null, null, p.IsLocked,
                        unlock && p.IsLocked, true, hasMultipleSharedSlots),
                    row => GlobalSharedValue(row, primary, p.Key), null));
            }

            // The selected member is the only local scope exposed by a global
            // result. Constrained member values remain reconstruction sources for
            // shared coordinates, but are not duplicated as local coordinates.
            if (selectedMemberIndex.HasValue)
            {
                var member = primary.Models[selectedMemberIndex.Value];
                var constraints = primary.Parameters;
                var hasMultipleMemberSlots = HasMultipleSlots(member.Parameters.Table.Keys);
                foreach (var p in member.Parameters.Table.Values)
                {
                    if (!IsCorrelationCoordinate(p.Key, member.ModelType)) continue;
                    if (constraints != null && constraints.GetConstraintForParameter(p.Key) != VariableConstraint.None) continue;
                    if (!p.IsFitted && !(unlock && p.IsLocked)) continue;
                    candidates.Add(new Candidate(
                        Descriptor(p.Key, BootstrapCorrelationParameterScope.Member, selectedMemberIndex, member,
                            p.IsLocked, unlock && p.IsLocked, false, hasMultipleMemberSlots),
                        row => GlobalMemberValue(row, primary, selectedMemberIndex.Value, p.Key),
                        member.Data.UniqueID));
                }
            }

            var rows = solution.BootstrapSolutions ?? new List<GlobalSolution>();
            if (rows.Count == 0)
                return Build(candidates, new List<double[]>(), BootstrapCorrelationAvailabilityStatus.NoBootstrapReplicates,
                    "No residual bootstrap replicates are available.", solution.Convergence, 0);

            var coordinates = CompleteRows(candidates, rows.Select(r => (object)r));
            return Build(candidates, coordinates, null, null, solution.Convergence,
                rows.Count(row => row != null));
        }

        public BootstrapCorrelationResult Analyze(AnalysisResult result, int? selectedMemberIndex = null)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            // A single-experiment AnalysisResult is represented by a one-member
            // GlobalSolution for historical reasons. Its fitted coordinates remain
            // on that member model, not in GlobalTable, so use the single scope.
            if (result.Solution?.Solutions != null && result.Solution.Solutions.Count == 1)
            {
                if (selectedMemberIndex.HasValue && selectedMemberIndex.Value != 0)
                    throw new ArgumentOutOfRangeException(nameof(selectedMemberIndex));
                return Analyze(result.Solution.Solutions[0]);
            }
            return Analyze(result.Solution, selectedMemberIndex);
        }

        public BootstrapCorrelationResult Analyze(GlobalSolution solution, SolutionInterface selectedMember)
        {
            if (selectedMember == null) return Analyze(solution, (int?)null);
            if (solution?.Model == null) throw new ArgumentNullException(nameof(solution));
            var index = solution.Model.Models.FindIndex(m => m.Data.UniqueID == selectedMember.Data.UniqueID);
            if (index < 0) throw new ArgumentException("The selected member is not part of the global result.", nameof(selectedMember));
            return Analyze(solution, index);
        }

        static bool IsResidualBootstrap(SolutionInterface solution)
        {
            if (solution.ErrorMethod == ErrorEstimationMethod.ProfileLikelihood)
                return false;
            return solution.ErrorMethod == ErrorEstimationMethod.BootstrapResiduals
                || solution.Model?.ModelCloneOptions?.ErrorEstimationMethod == ErrorEstimationMethod.BootstrapResiduals;
        }

        static bool IsResidualBootstrap(GlobalSolution solution)
        {
            if (solution.ErrorEstimationMethod == ErrorEstimationMethod.ProfileLikelihood
                || solution.Solutions.Any(s => s?.ErrorMethod == ErrorEstimationMethod.ProfileLikelihood))
                return false;
            if (solution.Model?.ModelCloneOptions?.ErrorEstimationMethod == ErrorEstimationMethod.BootstrapResiduals)
                return true;
            return solution.Solutions.Any(IsResidualBootstrap);
        }

        static bool IsCorrelationCoordinate(ParameterType key, AnalysisModel modelType)
        {
            if (key == ParameterType.Offset) return true;
            if (key == ParameterType.Nvalue1 || key == ParameterType.Nvalue2)
                return modelType != AnalysisModel.SequentialBindingSites;

            return ThermodynamicParameterSlots.TryResolve(key, out _, out var family)
                && (family == ThermodynamicParameterFamily.Affinity
                    || family == ThermodynamicParameterFamily.Enthalpy
                    || family == ThermodynamicParameterFamily.Gibbs
                    || family == ThermodynamicParameterFamily.HeatCapacity);
        }

        static bool HasMultipleSlots(IEnumerable<ParameterType> keys)
        {
            return keys.Any(key => key == ParameterType.Nvalue2
                || (ThermodynamicParameterSlots.TryResolve(key, out var slot, out _) && slot.Index > 1));
        }

        static double ParameterValue(SolutionInterface solution, ParameterType key)
        {
            if (solution == null) return double.NaN;
            // The model table is the fitted-coordinate surface. Solution.Parameters
            // is a report/error surface and may contain transformed values.
            if (solution.Model?.Parameters?.Table != null && solution.Model.Parameters.Table.ContainsKey(key))
                return solution.Model.Parameters.Table[key].Value;
            return double.NaN;
        }

        static double ParameterValue(Model model, ParameterType key)
        {
            return model?.Parameters?.Table != null && model.Parameters.Table.ContainsKey(key)
                ? model.Parameters.Table[key].Value : double.NaN;
        }

        static double GlobalSharedValue(GlobalSolution replicate, GlobalModel primary, ParameterType key)
        {
            var direct = replicate?.Model?.Parameters?.GlobalTable;
            if (direct != null && direct.ContainsKey(key) && IsFinite(direct[key].Value)) return direct[key].Value;
            var members = MemberModels(replicate, primary);
            var values = members.Select(m => ParameterValue(m, key)).Where(IsFinite).ToArray();

            if (ThermodynamicParameterSlots.TryResolve(key, out var slot, out var family)
                && family == ThermodynamicParameterFamily.Gibbs)
            {
                var affinity = slot.Affinity;
                var dg = members.Select(m =>
                {
                    if (m == null) return double.NaN;
                    var logKa = ParameterValue(m, affinity);
                    return IsFinite(logKa) ? -Energy.R * m.Data.MeasuredTemperatureKelvin * Math.Log(10.0) * logKa : double.NaN;
                }).Where(IsFinite).ToArray();
                return dg.Length == 0 ? double.NaN : dg.Average();
            }

            if (ThermodynamicParameterSlots.TryResolve(key, out slot, out family)
                && family == ThermodynamicParameterFamily.HeatCapacity)
            {
                return FitTemperatureSlope(members, slot.Enthalpy);
            }

            if (ThermodynamicParameterSlots.TryResolve(key, out slot, out family)
                && family == ThermodynamicParameterFamily.Enthalpy)
            {
                var cpValue = GlobalSharedValue(replicate, primary, slot.HeatCapacity);
                var reference = primary.MeanTemperature + 273.15;
                var hs = members.Select(m =>
                {
                    var h = ParameterValue(m, key);
                    return IsFinite(h) && IsFinite(cpValue) ? h - (m.Data.MeasuredTemperatureKelvin - reference) * cpValue : double.NaN;
                }).Where(IsFinite).ToArray();
                if (hs.Length != 0) return hs.Average();
            }

            return values.Length == 0 ? double.NaN : values.Average();
        }

        static double FitTemperatureSlope(IReadOnlyList<Model> members, ParameterType enthalpy)
        {
            var points = members.Where(m => m != null).Select(m => new { X = m.Data.MeasuredTemperatureKelvin, Y = ParameterValue(m, enthalpy) })
                .Where(p => IsFinite(p.X) && IsFinite(p.Y)).ToArray();
            if (points.Length < 2) return double.NaN;
            var meanX = points.Average(p => p.X);
            var meanY = points.Average(p => p.Y);
            var den = points.Sum(p => (p.X - meanX) * (p.X - meanX));
            return den == 0 ? double.NaN : points.Sum(p => (p.X - meanX) * (p.Y - meanY)) / den;
        }

        static double GlobalMemberValue(GlobalSolution replicate, GlobalModel primary, int memberIndex, ParameterType key)
        {
            return ParameterValue(MemberModels(replicate, primary).ElementAtOrDefault(memberIndex), key);
        }

        static IReadOnlyList<Model> MemberModels(GlobalSolution replicate, GlobalModel primary)
        {
            var result = new List<Model>();
            var replicateModels = replicate?.Model?.Models ?? new List<Model>();
            foreach (var member in primary.Models)
            {
                var found = replicateModels.FirstOrDefault(m => m.Data.UniqueID == member.Data.UniqueID);
                result.Add(found ?? (result.Count < replicateModels.Count ? replicateModels[result.Count] : null));
            }
            return result;
        }

        static List<double[]> CompleteRows(IReadOnlyList<Candidate> candidates, IEnumerable<object> rows)
        {
            var complete = new List<double[]>();
            foreach (var row in rows)
            {
                var values = candidates.Select(c => c.Value(row)).ToArray();
                if (values.All(IsFinite)) complete.Add(values);
            }
            return complete;
        }

        BootstrapCorrelationResult Build(
            IReadOnlyList<Candidate> candidates,
            List<double[]> complete,
            BootstrapCorrelationAvailabilityStatus? forcedStatus,
            string forcedReason,
            SolverConvergence convergence,
            int usableRefitCount)
        {
            // Drop zero-variance coordinates before calculating Pearson values.
            var varying = new List<int>();
            for (var i = 0; i < candidates.Count; i++)
            {
                if (complete.Any() && complete.Select(row => row[i]).Distinct().Count() > 1)
                    varying.Add(i);
            }

            var descriptors = varying.Select(i => candidates[i].Descriptor).ToList();
            var coordinates = complete.Select(row => varying.Select(i => row[i]).ToArray()).ToList();
            var status = forcedStatus;
            var reason = forcedReason;
            if (!status.HasValue)
            {
                if (complete.Count < MinimumCompleteReplicates)
                {
                    status = BootstrapCorrelationAvailabilityStatus.TooFewCompleteReplicates;
                    reason = $"At least {MinimumCompleteReplicates} complete residual-bootstrap replicates are required.";
                }
                else if (varying.Count < 2)
                {
                    status = BootstrapCorrelationAvailabilityStatus.TooFewVaryingParameters;
                    reason = "At least two varying fitted parameters are required.";
                }
                else
                {
                    status = BootstrapCorrelationAvailabilityStatus.Available;
                    reason = string.Empty;
                }
            }

            var matrix = status == BootstrapCorrelationAvailabilityStatus.Available
                ? Pearson(coordinates, descriptors.Count)
                : null;
            var cells = matrix == null ? null : BuildCellDiagnostics(matrix, complete.Count);
            var uncertainPairs = cells == null ? 0 : CountUncertainPairs(cells);
            var availability = new BootstrapCorrelationAvailability(status.Value, reason, complete.Count,
                MinimumCompleteReplicates, varying.Count);
            var omitted = candidates.Select((candidate, index) => new { candidate, index })
                .Where(item => !varying.Contains(item.index))
                .Select(item => item.candidate.Descriptor)
                .ToList();
            var rankLimited = status == BootstrapCorrelationAvailabilityStatus.Available
                && complete.Count <= descriptors.Count;
            var reliability = new BootstrapCorrelationReliability(convergence, usableRefitCount,
                complete.Count, uncertainPairs, status == BootstrapCorrelationAvailabilityStatus.Available);
            return new BootstrapCorrelationResult(availability, descriptors, coordinates, matrix, cells, omitted,
                rankLimited, reliability);
        }

        BootstrapCorrelationResult Unavailable(BootstrapCorrelationAvailabilityStatus status, string reason)
        {
            return new BootstrapCorrelationResult(
                new BootstrapCorrelationAvailability(status, reason, 0, MinimumCompleteReplicates, 0),
                new List<BootstrapCorrelationParameterDescriptor>(), new List<double[]>(), null,
                null, new List<BootstrapCorrelationParameterDescriptor>(), false,
                new BootstrapCorrelationReliability(null, 0, 0, 0, false));
        }

        static BootstrapCorrelationCellDiagnostic[,] BuildCellDiagnostics(double[,] matrix, int completeCount)
        {
            var width = matrix.GetLength(0);
            var result = new BootstrapCorrelationCellDiagnostic[width, width];
            for (var i = 0; i < width; i++)
            {
                result[i, i] = new BootstrapCorrelationCellDiagnostic(1, completeCount, true, null, null);
                for (var j = i + 1; j < width; j++)
                {
                    if (!IsFinite(matrix[i, j]))
                        throw new InvalidOperationException("A bootstrap Pearson correlation was non-finite.");
                    var r = Math.Max(-1, Math.Min(1, matrix[i, j]));
                    double lower;
                    double upper;
                    if (r == 1 || r == -1)
                    {
                        lower = upper = r;
                    }
                    else
                    {
                        var z = 0.5 * Math.Log((1 + r) / (1 - r));
                        var halfWidth = 1.959964 / Math.Sqrt(completeCount - 3.0);
                        lower = Math.Tanh(z - halfWidth);
                        upper = Math.Tanh(z + halfWidth);
                    }
                    var cell = new BootstrapCorrelationCellDiagnostic(r, completeCount, false, lower, upper);
                    result[i, j] = result[j, i] = cell;
                }
            }
            return result;
        }

        static int CountUncertainPairs(BootstrapCorrelationCellDiagnostic[,] cells)
        {
            var count = 0;
            for (var i = 0; i < cells.GetLength(0); i++)
                for (var j = i + 1; j < cells.GetLength(1); j++)
                    if (cells[i, j].IsSignUncertain) count++;
            return count;
        }

        static double[,] Pearson(IReadOnlyList<double[]> rows, int width)
        {
            var matrix = new double[width, width];
            var means = new double[width];
            for (var j = 0; j < width; j++) means[j] = rows.Average(row => row[j]);
            var covariance = new double[width, width];
            for (var i = 0; i < width; i++)
                for (var j = i; j < width; j++)
                {
                    var value = rows.Sum(row => (row[i] - means[i]) * (row[j] - means[j])) / (rows.Count - 1);
                    covariance[i, j] = covariance[j, i] = value;
                }
            for (var i = 0; i < width; i++)
                for (var j = 0; j < width; j++)
                    matrix[i, j] = covariance[i, j] / Math.Sqrt(covariance[i, i] * covariance[j, j]);
            return matrix;
        }

        static BootstrapCorrelationParameterDescriptor Descriptor(
            ParameterType key,
            BootstrapCorrelationParameterScope scope,
            int? memberIndex,
            Model model,
            bool originallyLocked,
            bool includedBecauseUnlock,
            bool derivedGlobal,
            bool hasMultipleSlots)
        {
            var slotIndex = key.GetProperties().NumberSubscript;
            var suffix = hasMultipleSlots ? slotIndex.ToString() : (slotIndex == 1 ? string.Empty : slotIndex.ToString());
            string label;
            if (ThermodynamicParameterSlots.TryResolve(key, out var slot, out var family))
            {
                slotIndex = slot.Index;
                label = family switch
                {
                    ThermodynamicParameterFamily.Enthalpy => "dH" + suffix,
                    ThermodynamicParameterFamily.Affinity => "log10 Ka" + suffix,
                    ThermodynamicParameterFamily.Gibbs => "dG" + suffix,
                    ThermodynamicParameterFamily.HeatCapacity => "dCp" + suffix,
                    _ => key.ToString(),
                };
            }
            else
            {
                label = key switch
                {
                    ParameterType.Nvalue1 or ParameterType.Nvalue2 => "N" + suffix,
                    ParameterType.Offset => "offset",
                    _ => key.ToString(),
                };
            }
            return new BootstrapCorrelationParameterDescriptor(key, label, scope, slotIndex, memberIndex,
                model?.Data?.UniqueID, model?.Data?.Name ?? model?.Data?.FileName,
                originallyLocked, includedBecauseUnlock, derivedGlobal);
        }

        sealed class Candidate
        {
            public BootstrapCorrelationParameterDescriptor Descriptor { get; private set; }
            public Func<object, double> Value { get; private set; }
            public string MemberId { get; private set; }

            public Candidate(BootstrapCorrelationParameterDescriptor descriptor, Func<object, double> value, string memberId)
            {
                Descriptor = descriptor;
                Value = value;
                MemberId = memberId;
            }
        }

        static double ParameterValue(object row, ParameterType key) => ParameterValue(row as SolutionInterface, key);
        static double GlobalSharedValue(object row, GlobalModel primary, ParameterType key) => GlobalSharedValue(row as GlobalSolution, primary, key);
        static double GlobalMemberValue(object row, GlobalModel primary, int index, ParameterType key) => GlobalMemberValue(row as GlobalSolution, primary, index, key);
        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
