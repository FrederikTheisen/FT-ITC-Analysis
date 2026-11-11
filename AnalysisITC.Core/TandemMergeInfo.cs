using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.DataReaders;

namespace AnalysisITC.Core.Processing
{
    public enum MixingUncertaintyMethod { Grid95, MonteCarlo95 }
    public enum MixingLimitStatus { Crossing, BoundReached, Unavailable }

    public sealed class TandemMixingInterval
    {
        public double? Lower { get; }
        public double? Upper { get; }
        public MixingLimitStatus LowerStatus { get; }
        public MixingLimitStatus UpperStatus { get; }

        public TandemMixingInterval(double? lower, double? upper,
            MixingLimitStatus lowerStatus, MixingLimitStatus upperStatus)
        {
            if (!Enum.IsDefined(typeof(MixingLimitStatus), lowerStatus) || !Enum.IsDefined(typeof(MixingLimitStatus), upperStatus)
                || !ValidSide(lower, lowerStatus) || !ValidSide(upper, upperStatus) || lower > upper
                || (lowerStatus == MixingLimitStatus.BoundReached && lower != 0)
                || (upperStatus == MixingLimitStatus.BoundReached && upper != 1))
                throw new ArgumentException("Invalid mixing-fraction interval.");
            Lower = lower; Upper = upper; LowerStatus = lowerStatus; UpperStatus = upperStatus;
        }

        static bool ValidSide(double? value, MixingLimitStatus status) => status == MixingLimitStatus.Unavailable
            ? !value.HasValue : value.HasValue && TandemConcatenation.IsValidMixingFraction(value.Value);

        public static TandemMixingInterval Unavailable() => new TandemMixingInterval(null, null,
            MixingLimitStatus.Unavailable, MixingLimitStatus.Unavailable);
    }

    public sealed class TandemMixingUncertainty
    {
        public MixingUncertaintyMethod Method { get; }
        public double ConfidenceLevel => .95;
        public IReadOnlyList<TandemMixingInterval> Intervals { get; }
        public double? GridStep { get; }
        public int MonteCarloDraws { get; }
        public double? HeatNoiseSd { get; }

        public TandemMixingUncertainty(MixingUncertaintyMethod method, IEnumerable<TandemMixingInterval> intervals,
            double? gridStep = null, int monteCarloDraws = 0, double? heatNoiseSd = null)
        {
            var saved = intervals?.ToArray() ?? throw new ArgumentNullException(nameof(intervals));
            if (!Enum.IsDefined(typeof(MixingUncertaintyMethod), method) || saved.Length == 0 || saved.Any(i => i == null)
                || (gridStep.HasValue && (!TandemConcatenation.IsValidMixingFraction(gridStep.Value) || gridStep <= 0))
                || monteCarloDraws < 0 || (heatNoiseSd.HasValue && (!Finite(heatNoiseSd.Value) || heatNoiseSd < 0)))
                throw new ArgumentException("Invalid mixing uncertainty metadata.");
            if (method == MixingUncertaintyMethod.Grid95 && (!gridStep.HasValue || monteCarloDraws != 0 || heatNoiseSd.HasValue)
                || method == MixingUncertaintyMethod.MonteCarlo95 && (gridStep.HasValue || monteCarloDraws != 200))
                throw new ArgumentException("Mixing uncertainty settings do not match the method.");
            Method = method; Intervals = Array.AsReadOnly(saved); GridStep = gridStep;
            MonteCarloDraws = monteCarloDraws; HeatNoiseSd = heatNoiseSd;
        }

        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Immutable evidence captured when an automatic tandem merge is created.</summary>
    public sealed class TandemMergeInfo
    {
        public IReadOnlyList<double> Fractions { get; }
        public TandemMixingCriterion Criterion { get; }
        public DilutionMethod Bookkeeping { get; }
        public double DeadVolume { get; }
        public bool DidRemoveOverflow { get; }
        public double RemoveOverflowVolume { get; }
        public int PointsPerSide { get; }
        public TandemMixingUncertainty Uncertainty { get; }

        public TandemMergeInfo(IEnumerable<double> fractions, TandemMixingCriterion criterion, DilutionMethod bookkeeping,
            double deadVolume, bool didRemoveOverflow, double removeOverflowVolume, int pointsPerSide,
            TandemMixingUncertainty uncertainty)
        {
            var saved = fractions?.ToArray() ?? throw new ArgumentNullException(nameof(fractions));
            if (saved.Length == 0 || saved.Length > 4 || saved.Any(f => !TandemConcatenation.IsValidMixingFraction(f))
                || !Enum.IsDefined(typeof(TandemMixingCriterion), criterion) || !Enum.IsDefined(typeof(DilutionMethod), bookkeeping)
                || !TandemConcatenation.IsValidDeadVolume(deadVolume) || !TandemMixingUncertainty.Finite(removeOverflowVolume)
                || removeOverflowVolume < 0 || uncertainty == null || uncertainty.Intervals.Count != saved.Length
                || (criterion == TandemMixingCriterion.ModelFree ? pointsPerSide < 3 : pointsPerSide != 0)
                || (criterion == TandemMixingCriterion.ModelFree) != (uncertainty.Method == MixingUncertaintyMethod.MonteCarlo95))
                throw new ArgumentException("Invalid automatic tandem merge metadata.");
            Fractions = Array.AsReadOnly(saved); Criterion = criterion; Bookkeeping = bookkeeping;
            DeadVolume = deadVolume; DidRemoveOverflow = didRemoveOverflow; RemoveOverflowVolume = removeOverflowVolume;
            PointsPerSide = pointsPerSide; Uncertainty = uncertainty;
        }
    }
}
