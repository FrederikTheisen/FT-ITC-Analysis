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

        public static InterpretationThermogramEvidence Compress(IEnumerable<(double Time, double PowerWatts, double? BaselineWatts)> source)
        {
            var raw = source.ToList();
            var finite = raw.Select((sample, index) => (sample, index))
                .Where(item => IsFinite(item.sample.Time) && IsFinite(item.sample.PowerWatts)).ToList();
            if (finite.Count == 0) return null;
            var powers = finite.Select(item => item.sample.PowerWatts).OrderBy(value => value).ToArray();
            var offset = powers.Length % 2 == 1 ? powers[powers.Length / 2]
                : powers[powers.Length / 2 - 1] / 2 + powers[powers.Length / 2] / 2;
            var anchor = finite[0].sample.Time;
            var selected = new HashSet<int>();
            foreach (var bin in finite.GroupBy(item => Math.Floor((item.sample.Time - anchor) / 15)))
            {
                selected.Add(bin.OrderBy(item => item.sample.PowerWatts).ThenBy(item => item.index).First().index);
                selected.Add(bin.OrderByDescending(item => item.sample.PowerWatts).ThenBy(item => item.index).First().index);
            }
            InterpretationThermogramSample Sample(int index) => new InterpretationThermogramSample
            {
                SourceIndex = index, TimeSeconds = raw[index].Time,
                RelativePowerMicrowatts = (raw[index].PowerWatts - offset) * 1e6,
                RelativeBaselineMicrowatts = raw[index].BaselineWatts.HasValue && IsFinite(raw[index].BaselineWatts.Value)
                    ? (raw[index].BaselineWatts.Value - offset) * 1e6 : null,
            };
            var result = new InterpretationThermogramEvidence
            {
                AnchorTimeSeconds = anchor, PowerOffsetWatts = offset,
                SourceSampleCount = raw.Count, FiniteSampleCount = finite.Count,
                Samples = selected.OrderBy(index => raw[index].Time).ThenBy(index => index).Select(Sample).ToList(),
                Endpoints = new[] { finite[0].index, finite[finite.Count - 1].index }.Distinct().Where(index => !selected.Contains(index))
                    .OrderBy(index => raw[index].Time).ThenBy(index => index).Select(Sample).ToList(),
            };
            result.RetainedSampleCount = result.Samples.Count + result.Endpoints.Count;
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
            package.DataBoundary.ContainsBaselineArrays = traces.SelectMany(item => item.Samples.Concat(item.Endpoints)).Any(item => item.RelativeBaselineMicrowatts.HasValue);
            if (traces.Count == 0) package.DataBoundary.ModelObservationRestriction = "No raw thermogram or sampled fitted-baseline arrays were supplied. Assess available summaries, controls and injection evidence only; do not claim to observe peak shape or settling.";
        }

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
