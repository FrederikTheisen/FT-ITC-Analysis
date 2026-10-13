using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;

namespace AnalysisITC.Core.Application
{
    public enum ProcessingClearScope { Selected, Active, All }

    public sealed class ProcessingClearPlan
    {
        readonly DocumentSaveStamp stamp;
        readonly Entry[] entries;
        bool applied;
        public int Count => entries.Length;

        internal ProcessingClearPlan(IEnumerable<ExperimentData> targets)
        {
            DocumentDirtyTracker.Initialize();
            stamp = DocumentDirtyTracker.CaptureSaveStamp();
            entries = targets.Select(data => new Entry(data)).Where(entry => entry.HasChanges).ToArray();
        }

        public bool TryApply()
        {
            if (applied || Count == 0) return false;
            return ProcessingActivity.TryClear(() =>
            {
                if (!DocumentDirtyTracker.MatchesSaveStamp(stamp) || entries.Any(entry => !entry.IsCurrent)) return false;
                // Publish all replacement state before notifying dependent experiments/views.
                foreach (var entry in entries) entry.ApplyState();
                applied = true;
                foreach (var entry in entries) entry.Notify();
                DataManager.InvokeDataDidChange();
                DataManager.InvokeUpdateDataViewCells();
                DataManager.InvokeUpdateTable();
                return true;
            });
        }

        internal sealed class Entry
        {
            readonly ExperimentData data;
            readonly DataProcessor oldProcessor;
            readonly Analysis.Models.Model oldModel;
            readonly long revision;
            readonly string signature;
            readonly DataProcessor replacement;
            readonly float[] endOffsets;
            public bool HasChanges { get; }
            public bool IsCurrent => DataManager.Data.Contains(data) && ReferenceEquals(data.Processor, oldProcessor)
                && ReferenceEquals(data.Model, oldModel) && data.ProcessingRevision == revision && Signature(data) == signature;

            public Entry(ExperimentData data, bool captureState = true)
            {
                this.data = data;
                oldProcessor = data.Processor;
                oldModel = data.Model;
                revision = data.ProcessingRevision;
                if (!data.HasThermogram)
                {
                    HasChanges = data.Model != null;
                    if (HasChanges && captureState) signature = Signature(data);
                    return;
                }
                replacement = new DataProcessor(data);
                replacement.InitializeBaseline(BaselineInterpolatorTypes.Spline);
                var endpoint = data.Injections.Count > 0 ? data.Injections[0].Delay / 2 : 0;
                // The reset region cannot extend beyond a short injection,
                // even when it contains fewer than two sampling intervals.
                endOffsets = data.Injections.Select(injection => (float)FWEMath.Clamp(endpoint,
                    Math.Min(2 * (float)data.TimeStep, injection.Delay), injection.Delay)).ToArray();
                HasChanges = data.Model != null || data.BaseLineCorrectedDataPoints?.Count > 0
                    || data.AverageHeatDirection != PeakHeatDirection.Unknown
                    || oldProcessor.BaselineCompleted || oldProcessor.IsLocked
                    || ProcessorSignature(oldProcessor) != ProcessorSignature(replacement)
                    || oldProcessor.Interpolator?.Baseline.Count > 0
                    || data.Injections.Where((injection, index) => injection.IsIntegrated
                        || !(FloatWithError.IsNaN(injection.RawPeakArea) || double.IsNaN(injection.RawPeakArea.Value)) || !(FloatWithError.IsNaN(injection.PeakArea) || double.IsNaN(injection.PeakArea.Value))
                        || injection.IntegrationStartDelay != 0 || injection.IntegrationEndOffset != endOffsets[index]
                        || injection.HeatDirection != PeakHeatDirection.Unknown).Any();
                if (HasChanges && captureState) signature = Signature(data);
            }

            public void ApplyState()
            {
                if (replacement != null)
                {
                    data.SetProcessor(replacement);
                    data.BaseLineCorrectedDataPoints = null;
                    for (var index = 0; index < data.Injections.Count; index++)
                        data.Injections[index].ClearCalculatedProcessing(endOffsets[index]);
                    data.AverageHeatDirection = PeakHeatDirection.Unknown;
                }
                data.Model = null;
            }

            public void Notify()
            {
                if (replacement != null) data.UpdateProcessing();
                data.RemoveModel(notifyDataChange: false);
            }

            static readonly JsonSerializerOptions json = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
            static string ProcessorSignature(DataProcessor processor) => JsonSerializer.Serialize(FTXTCWriter.CaptureProcessor(processor), json);
            static string Signature(ExperimentData data)
            {
                // Stream the fingerprint: do not retain serialized thermograms/baselines per target.
                using var hash = SHA256.Create();
                using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
                using var writer = new Utf8JsonWriter(stream);
                JsonSerializer.Serialize(writer, new
                {
                    data.HasThermogram, data.TimeStep, data.DataSourceFormat, data.AverageHeatDirection,
                    Processor = FTXTCWriter.CaptureProcessor(data.Processor),
                    Baseline = data.Processor.Interpolator?.Baseline.Select(value => FtxtcFloatWithError.Capture(value.FloatWithError)),
                    RawTrace = data.DataPoints.Select(point => new { point.Time, point.Power, point.Temperature }),
                    CorrectedTrace = data.BaseLineCorrectedDataPoints?.Select(point => new { point.Time, point.Power, point.Temperature }),
                    Injections = data.Injections.Select(injection => new
                    {
                        Snapshot = InjectionFitInputSnapshot.Capture(injection), injection.Include, injection.IsIntegrated,
                        injection.HeatDirection, injection.Time, injection.Delay, injection.Duration, injection.Filter,
                        injection.Temperature,
                        Raw = FtxtcFloatWithError.Capture(injection.RawPeakArea),
                        Corrected = FtxtcFloatWithError.Capture(injection.PeakArea)
                    }),
                }, json);
                writer.Flush();
                stream.FlushFinalBlock();
                return Convert.ToBase64String(hash.Hash);
            }
        }
    }
}
