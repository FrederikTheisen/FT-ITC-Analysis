using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnalysisITC.Core.Processing;

namespace AnalysisITC.Core.Export
{
    internal sealed class FtxtcTandemMergeInfo
    {
        public List<double> Fractions { get; set; }
        public string Criterion { get; set; }
        public string Bookkeeping { get; set; }
        public double DeadVolume { get; set; }
        public bool DidRemoveOverflow { get; set; }
        public double RemoveOverflowVolume { get; set; }
        public int PointsPerSide { get; set; }
        public string Method { get; set; }
        public double ConfidenceLevel { get; set; }
        public double? GridStep { get; set; }
        public int MonteCarloDraws { get; set; }
        public double? HeatNoiseSd { get; set; }
        public List<FtxtcMixingInterval> Intervals { get; set; }

        public static FtxtcTandemMergeInfo Capture(TandemMergeInfo info) => info == null ? null : new FtxtcTandemMergeInfo
        {
            Fractions = info.Fractions.ToList(),
            Criterion = info.Criterion == TandemMixingCriterion.OneSiteFit ? "one-site" : "model-free",
            Bookkeeping = FtxtcWireIds.ConcentrationMethod(info.Bookkeeping),
            DeadVolume = info.DeadVolume, DidRemoveOverflow = info.DidRemoveOverflow,
            RemoveOverflowVolume = info.RemoveOverflowVolume, PointsPerSide = info.PointsPerSide,
            Method = info.Uncertainty.Method == MixingUncertaintyMethod.Grid95 ? "grid-95" : "monte-carlo-95",
            ConfidenceLevel = info.Uncertainty.ConfidenceLevel, GridStep = info.Uncertainty.GridStep,
            MonteCarloDraws = info.Uncertainty.MonteCarloDraws, HeatNoiseSd = info.Uncertainty.HeatNoiseSd,
            Intervals = info.Uncertainty.Intervals.Select(FtxtcMixingInterval.Capture).ToList(),
        };

        public TandemMergeInfo Restore()
        {
            if (ConfidenceLevel != .95 || Intervals == null || Fractions == null)
                throw new InvalidDataException("Incomplete tandem merge uncertainty.");
            var criterion = Criterion == "one-site" ? TandemMixingCriterion.OneSiteFit
                : Criterion == "model-free" ? TandemMixingCriterion.ModelFree : throw new InvalidDataException("Unknown tandem criterion.");
            var method = Method == "grid-95" ? MixingUncertaintyMethod.Grid95
                : Method == "monte-carlo-95" ? MixingUncertaintyMethod.MonteCarlo95 : throw new InvalidDataException("Unknown tandem uncertainty method.");
            var bookkeeping = FtxtcWireIds.ConcentrationMethod(Bookkeeping)
                ?? throw new InvalidDataException("Missing tandem bookkeeping.");
            return new TandemMergeInfo(Fractions, criterion, bookkeeping, DeadVolume, DidRemoveOverflow,
                RemoveOverflowVolume, PointsPerSide, new TandemMixingUncertainty(method,
                    Intervals.Select(i => i?.Restore() ?? throw new InvalidDataException("Missing tandem interval.")),
                    GridStep, MonteCarloDraws, HeatNoiseSd));
        }
    }

    internal sealed class FtxtcMixingInterval
    {
        public double? Lower { get; set; }
        public double? Upper { get; set; }
        public string LowerStatus { get; set; }
        public string UpperStatus { get; set; }

        public static FtxtcMixingInterval Capture(TandemMixingInterval interval) => new FtxtcMixingInterval
        {
            Lower = interval.Lower, Upper = interval.Upper,
            LowerStatus = WireStatus(interval.LowerStatus), UpperStatus = WireStatus(interval.UpperStatus),
        };

        public TandemMixingInterval Restore() => new TandemMixingInterval(Lower, Upper, ParseStatus(LowerStatus), ParseStatus(UpperStatus));
        static string WireStatus(MixingLimitStatus status) => status == MixingLimitStatus.Crossing ? "crossing"
            : status == MixingLimitStatus.BoundReached ? "bound-reached" : "unavailable";
        static MixingLimitStatus ParseStatus(string status) => status == "crossing" ? MixingLimitStatus.Crossing
            : status == "bound-reached" ? MixingLimitStatus.BoundReached
            : status == "unavailable" ? MixingLimitStatus.Unavailable : throw new InvalidDataException("Unknown tandem interval status.");
    }
}
