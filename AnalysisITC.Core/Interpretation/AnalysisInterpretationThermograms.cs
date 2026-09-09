using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using AnalysisITC.Core.Data;

namespace AnalysisITC.Core.Interpretation
{
    public static class AnalysisInterpretationThermograms
    {
        public static InterpretationThermogramEvidence Compress(ExperimentData data)
        {
            if (data?.HasThermogram != true) return null;
            var baseline = data.Processor?.Interpolator?.Baseline;
            return Compress(data.DataPoints.Select((point, index) => ((double)point.Time, (double)point.Power,
                baseline?.Count == data.DataPoints.Count ? (double?)baseline[index].Value : null)));
        }

        public static InterpretationThermogramEvidence Compress(ExperimentData data, out string omissionReason)
        {
            omissionReason = null;
            if (data?.HasThermogram != true) return null;
            var baseline = data.Processor?.Interpolator?.Baseline;
            var trace = Compress(data.DataPoints.Select((point, index) => ((double)point.Time, (double)point.Power,
                baseline?.Count == data.DataPoints.Count ? (double?)baseline[index].Value : null)));
            if (trace == null)
            {
                var finiteCount = data.DataPoints.Count(point => IsFinite(point.Time) && IsFinite(point.Power));
                omissionReason = finiteCount == 0
                    ? "Thermogram omitted because it contains no finite time and raw-power samples."
                    : "Thermogram omitted because its time span is too large to preserve uniform 15-second bin alignment within the transport budget.";
            }
            return trace;
        }

        public static InterpretationThermogramEvidence Compress(IEnumerable<(double Time, double PowerWatts, double? BaselineWatts)> source)
        {
            var raw = source.ToList();
            var finite = raw.Select((sample, index) => (sample, index))
                .Where(item => IsFinite(item.sample.Time) && IsFinite(item.sample.PowerWatts)).ToList();
            if (finite.Count == 0) return null;
            var powers = finite.Select(item => item.sample.PowerWatts).OrderBy(value => value).ToArray();
            var offset = powers.Length % 2 == 1 ? powers[powers.Length / 2]
                : powers[powers.Length / 2 - 1] / 2 + powers[powers.Length / 2] / 2;
            var anchor = finite.Min(item => item.sample.Time);
            const double binWidth = 15;
            var spanBins = (finite.Max(item => item.sample.Time) - anchor) / binWidth;
            // Preserve alignment for ordinary sparse traces, while retaining the existing
            // omission fallback for pathological timestamps that cannot fit an array.
            const double maximumRepresentableBins = (2 * 1024 * 1024) / 6.0;
            if (!IsFinite(spanBins) || spanBins > maximumRepresentableBins - 1) return null;
            var binCount = (int)Math.Floor(spanBins) + 1;
            var power = Enumerable.Range(0, binCount).Select(_ => (double?[])new double?[] { null, null }).ToList();
            var baseline = Enumerable.Range(0, binCount).Select(_ => (double?[])new double?[] { null, null }).ToList();
            var hasBaseline = false;
            foreach (var item in finite)
            {
                var bin = (int)Math.Floor((item.sample.Time - anchor) / binWidth);
                var value = (item.sample.PowerWatts - offset) * 1e6;
                if (!power[bin][0].HasValue || value < power[bin][0]) power[bin][0] = value;
                if (!power[bin][1].HasValue || value > power[bin][1]) power[bin][1] = value;
            }
            // Baseline extrema are independent of raw-power validity. A finite stored
            // baseline at a timestamp whose raw power is nonfinite remains representable.
            foreach (var item in raw.Select((sample, index) => (sample, index))
                .Where(item => IsFinite(item.sample.Time) && item.sample.BaselineWatts.HasValue && IsFinite(item.sample.BaselineWatts.Value)))
            {
                var bin = (int)Math.Floor((item.sample.Time - anchor) / binWidth);
                if (bin < 0 || bin >= binCount) continue;
                hasBaseline = true;
                var value = (item.sample.BaselineWatts.Value - offset) * 1e6;
                if (!baseline[bin][0].HasValue || value < baseline[bin][0]) baseline[bin][0] = value;
                if (!baseline[bin][1].HasValue || value > baseline[bin][1]) baseline[bin][1] = value;
            }
            var result = new InterpretationThermogramEvidence
            {
                AnchorTimeSeconds = anchor, PowerOffsetWatts = offset,
                SourceSampleCount = raw.Count, FiniteSampleCount = finite.Count,
                PowerMinMax = power,
                BaselineMinMax = hasBaseline ? baseline : null,
            };
            return result;
        }

        // Includes every source sample and stored baseline, even when transmitted arrays are omitted.
        public static string SourceFingerprint(ExperimentData data)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                foreach (var point in data.DataPoints ?? new List<DataPoint>()) { writer.Write(point.Time); writer.Write(point.Power); }
                writer.Write("baseline");
                foreach (var point in data.Processor?.Interpolator?.Baseline ?? new List<AnalysisITC.Core.Units.Energy>()) writer.Write(point.Value);
            }
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(stream.ToArray()).Select(value => value.ToString("x2")));
        }

        public static AnalysisInterpretationPackage Copy(AnalysisInterpretationPackage package) =>
            System.Text.Json.JsonSerializer.Deserialize<AnalysisInterpretationPackage>(
                System.Text.Json.JsonSerializer.Serialize(package, AnalysisInterpretationPromptBuilder.CanonicalJsonOptions),
                AnalysisInterpretationPromptBuilder.CanonicalJsonOptions);

        public static bool OmitTraces(AnalysisInterpretationPackage package, string reason)
        {
            var experiments = package.Results.SelectMany(result => result.Experiments).Concat(package.SupportingExperiments).ToList();
            var hadTraces = experiments.Any(item => item.Thermogram != null);
            foreach (var item in experiments) item.Thermogram = null;
            if (hadTraces && !package.Omissions.Contains(reason)) package.Omissions.Add(reason);
            UpdateBoundary(package);
            return hadTraces;
        }

        public static void UpdateBoundary(AnalysisInterpretationPackage package)
        {
            var traces = package.Results.SelectMany(result => result.Experiments).Concat(package.SupportingExperiments)
                .Select(item => item.Thermogram).Where(item => item != null).ToList();
            package.DataBoundary.ContainsRawThermogramSamples = traces.Count > 0;
            package.DataBoundary.ContainsBaselineArrays = traces.Any(item => item.BaselineMinMax != null);
            if (traces.Count == 0) package.DataBoundary.ModelObservationRestriction = "No raw thermogram or sampled fitted-baseline arrays were supplied. Assess available summaries, controls and injection evidence only; do not claim to observe peak shape or settling.";
        }

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
