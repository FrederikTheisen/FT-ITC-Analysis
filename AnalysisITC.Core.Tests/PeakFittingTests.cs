using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public class PeakFittingTests
    {
        [Fact]
        public void MirroredPositiveAndNegativePeaksHaveTheSameEnd()
        {
            var positive = CreateCorrectedExperiment(
                sampleInterval: 1,
                injectionTimes: new[] { 20f },
                peakShape: (injection, elapsed) => 10 * Math.Exp(-elapsed / 5));
            var negative = CreateCorrectedExperiment(
                sampleInterval: 1,
                injectionTimes: new[] { 20f },
                peakShape: (injection, elapsed) => 0);
            negative.BaseLineCorrectedDataPoints = positive.BaseLineCorrectedDataPoints
                .Select(point => new DataPoint(point.Time, -point.Power, point.Temperature))
                .ToList();

            var positiveResult = CorrectedPeakEndEstimator.EstimateEndOffsets(
                positive,
                positive.BaseLineCorrectedDataPoints);
            var negativeResult = CorrectedPeakEndEstimator.EstimateEndOffsets(
                negative,
                negative.BaseLineCorrectedDataPoints);

            Assert.Equal(positiveResult.EndOffsets, negativeResult.EndOffsets);
            Assert.Equal(PeakPolarity.Positive, positiveResult.Estimates[0].Polarity);
            Assert.Equal(PeakPolarity.Negative, negativeResult.Estimates[0].Polarity);
            Assert.True(positiveResult.Estimates[0].IsReliable);
            Assert.Equal(PeakEndDecision.ExponentialTail, positiveResult.Estimates[0].FinalDecision);
            Assert.InRange(
                positiveResult.EndOffsets[0],
                (float)(CorrectedPeakEndEstimator.TailFitStartOffsetSeconds +
                    4.5 * CorrectedPeakEndEstimator.EndpointTauMultiples),
                (float)(CorrectedPeakEndEstimator.TailFitStartOffsetSeconds +
                    5.5 * CorrectedPeakEndEstimator.EndpointTauMultiples));
        }

        [Fact]
        public void FirstTenSecondsDoNotInfluenceTheTailFit()
        {
            var plain = CreateCorrectedExperiment(
                sampleInterval: 1,
                injectionTimes: new[] { 20f },
                peakShape: (injection, elapsed) => 10 * Math.Exp(-elapsed / 5));
            var largeTransient = CreateCorrectedExperiment(
                sampleInterval: 1,
                injectionTimes: new[] { 20f },
                peakShape: (injection, elapsed) =>
                    10 * Math.Exp(-elapsed / 5) +
                    (elapsed < CorrectedPeakEndEstimator.TailFitStartOffsetSeconds
                        ? 100 * Math.Sin(elapsed)
                        : 0));

            var plainFit = CorrectedPeakEndEstimator.EstimateEndOffsets(
                plain,
                plain.BaseLineCorrectedDataPoints).Estimates[0];
            var transientFit = CorrectedPeakEndEstimator.EstimateEndOffsets(
                largeTransient,
                largeTransient.BaseLineCorrectedDataPoints).Estimates[0];

            Assert.Equal(plainFit.EndOffset, transientFit.EndOffset, 3);
            Assert.Equal(plainFit.Tau, transientFit.Tau, 3);
            Assert.Equal(plainFit.Amplitude, transientFit.Amplitude, 3);
        }

        [Fact]
        public void ExponentialTailRecoversTauAndUsesConfiguredTauMultiple()
        {
            const double tau = 5;
            var experiment = CreateCorrectedExperiment(
                sampleInterval: 1,
                injectionTimes: new[] { 20f },
                peakShape: (injection, elapsed) => 10 * Math.Exp(-elapsed / tau));

            var result = CorrectedPeakEndEstimator.EstimateEndOffsets(
                experiment,
                experiment.BaseLineCorrectedDataPoints);
            var estimate = result.Estimates[0];

            Assert.True(estimate.IsReliable);
            Assert.Equal(PeakEndDecision.ExponentialTail, estimate.FinalDecision);
            Assert.InRange(estimate.Tau, 4.5, 5.5);
            Assert.InRange(
                Math.Abs(
                    estimate.EndOffset -
                    (estimate.FitStartOffset + CorrectedPeakEndEstimator.EndpointTauMultiples * estimate.Tau)),
                0,
                0.001);
            Assert.InRange(
                estimate.EndOffset,
                (float)(estimate.FitStartOffset +
                    4.5 * CorrectedPeakEndEstimator.EndpointTauMultiples),
                (float)(estimate.FitStartOffset +
                    5.5 * CorrectedPeakEndEstimator.EndpointTauMultiples));
        }

        [Fact]
        public void NeighbourMedianRemovesAnIsolatedDurationArtefactWithoutChangingEdges()
        {
            var experiment = CreateCorrectedExperiment(
                sampleInterval: 1,
                injectionTimes: new[] { 20f, 100f, 180f, 260f, 340f },
                peakShape: (injection, elapsed) =>
                    10 * Math.Exp(-elapsed / (injection == 2 ? 10.0 : 5.0)),
                delay: 70);

            var result = CorrectedPeakEndEstimator.EstimateEndOffsets(
                experiment,
                experiment.BaseLineCorrectedDataPoints);
            var individualDurations = result.Estimates
                .Select((estimate, index) =>
                    estimate.IndividualEndOffset - experiment.Injections[index].IntegrationStartDelay)
                .ToArray();

            Assert.False(result.Estimates[0].NeighbourFilterApplied);
            Assert.False(result.Estimates[4].NeighbourFilterApplied);
            Assert.All(result.Estimates.Skip(1).Take(3), estimate =>
                Assert.True(estimate.NeighbourFilterApplied));
            Assert.Equal(individualDurations[0], result.EndOffsets[0], 3);
            Assert.Equal(individualDurations[4], result.EndOffsets[4], 3);
            Assert.True(individualDurations[2] > individualDurations[1]);
            Assert.Equal(individualDurations[1], result.EndOffsets[2], 2);
        }

        [Fact]
        public void FiveSecondSamplingUsesActualTimestamps()
        {
            var experiment = CreateCorrectedExperiment(
                sampleInterval: 5,
                injectionTimes: new[] { 20f },
                peakShape: (injection, elapsed) => -10 * Math.Exp(-elapsed / 5));

            var result = CorrectedPeakEndEstimator.EstimateEndOffsets(
                experiment,
                experiment.BaseLineCorrectedDataPoints);

            Assert.True(result.Estimates[0].IsReliable);
            Assert.InRange(result.Estimates[0].Tau, 3.5, 6.5);
            Assert.InRange(result.EndOffsets[0], 30f, 45f);
        }

        [Fact]
        public void WeakPeakUsesReliablePeerDurationAndOtherwiseKeepsItsCurrentEnd()
        {
            var withPeer = CreateCorrectedExperiment(
                sampleInterval: 1,
                injectionTimes: new[] { 20f, 90f },
                peakShape: (injection, elapsed) => injection == 0 ? -NonExponentialPeak(elapsed) : 0);
            withPeer.Injections[1].SetIntegrationStartTime(5);
            var peerResult = CorrectedPeakEndEstimator.EstimateEndOffsets(
                withPeer,
                withPeer.BaseLineCorrectedDataPoints);

            Assert.True(peerResult.Estimates[0].IsReliable);
            Assert.False(peerResult.Estimates[1].IsReliable);
            Assert.True(peerResult.Estimates[1].UsedPeerFallback);
            Assert.NotEqual(PeakEndDecision.ExponentialTail, peerResult.Estimates[1].DetectionDecision);
            Assert.Equal(PeakEndDecision.PeerDuration, peerResult.Estimates[1].FinalDecision);
            Assert.Equal(
                peerResult.EndOffsets[0] - withPeer.Injections[0].IntegrationStartDelay,
                peerResult.EndOffsets[1] - withPeer.Injections[1].IntegrationStartDelay);

            var withoutPeer = CreateCorrectedExperiment(
                sampleInterval: 1,
                injectionTimes: new[] { 20f },
                peakShape: (injection, elapsed) => 0);
            withoutPeer.Injections[0].SetIntegrationLengthByTime(37);
            var noPeerResult = CorrectedPeakEndEstimator.EstimateEndOffsets(
                withoutPeer,
                withoutPeer.BaseLineCorrectedDataPoints);

            Assert.False(noPeerResult.Estimates[0].IsReliable);
            Assert.False(noPeerResult.Estimates[0].UsedPeerFallback);
            Assert.Equal(37f, noPeerResult.EndOffsets[0]);
        }

        [Theory]
        [InlineData(BaselineInterpolatorTypes.Spline)]
        [InlineData(BaselineInterpolatorTypes.Polynomial)]
        [InlineData(BaselineInterpolatorTypes.Segmented)]
        public async Task RepeatedFitIsStableAcrossBaselineTypes(BaselineInterpolatorTypes baselineType)
        {
            var experiment = CreateSyntheticExperiment(baselineType);
            await experiment.Processor.ProcessData(showProgress: false);

            var first = await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);
            var firstOffsets = experiment.Injections.Select(injection => injection.IntegrationEndOffset).ToArray();
            var firstHeats = experiment.Injections.Select(injection => injection.RawPeakArea.Value).ToArray();
            var firstBaseline = experiment.Processor.Interpolator.Baseline.Select(point => point.Value).ToArray();
            var firstCorrected = experiment.BaseLineCorrectedDataPoints.Select(point => point.Power).ToArray();

            var second = await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);
            var secondOffsets = experiment.Injections.Select(injection => injection.IntegrationEndOffset).ToArray();
            var secondHeats = experiment.Injections.Select(injection => injection.RawPeakArea.Value).ToArray();

            Assert.True(first.Succeeded);
            Assert.True(second.Succeeded);
            Assert.False(second.RegionsChanged);
            if (baselineType == BaselineInterpolatorTypes.Segmented)
                Assert.Equal(PeakFitStatus.CycleResolved, first.Status);
            Assert.Equal(firstOffsets, secondOffsets);
            Assert.Equal(firstBaseline, experiment.Processor.Interpolator.Baseline.Select(point => point.Value).ToArray());
            Assert.Equal(firstCorrected, experiment.BaseLineCorrectedDataPoints.Select(point => point.Power).ToArray());
            for (int i = 0; i < firstHeats.Length; i++)
                Assert.Equal(firstHeats[i], secondHeats[i], 12);
        }

        [Fact]
        public async Task FitUsesOnlyTheCorrectedTrace()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline, initializeBaseline: false);
            experiment.BaseLineCorrectedDataPoints = experiment.DataPoints
                .Select(point => new DataPoint(point.Time, point.Power * 0.5f, point.Temperature))
                .ToList();

            var traces = new List<IReadOnlyList<DataPoint>>();
            experiment.Processor.PeakEndOffsetEstimator = trace =>
            {
                traces.Add(trace);
                return experiment.Injections.Select(injection => injection.IntegrationEndOffset).ToArray();
            };

            var result = await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);

            Assert.True(result.Succeeded);
            Assert.Equal(DataProcessor.PeakFitPassCount, traces.Count);
            Assert.All(traces, trace =>
            {
                Assert.NotSame(experiment.BaseLineCorrectedDataPoints, trace);
                Assert.Equal(
                    experiment.BaseLineCorrectedDataPoints.Select(point => point.Power),
                    trace.Select(point => point.Power));
            });
        }

        [Fact]
        public async Task FitWritesIterationAndInjectionDiagnosticsToTheApplicationLog()
        {
            var experiment = CreateCorrectedExperiment(
                sampleInterval: 1,
                injectionTimes: new[] { 20f, 100f, 180f },
                peakShape: (injection, elapsed) =>
                    -10 * Math.Exp(-elapsed / (injection == 1 ? 10.0 : 5.0)),
                delay: 70);

            await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);

            var log = AppEventHandler.GetLogReport();
            Assert.Contains("[PeakFit] Start:", log);
            Assert.Contains("zero-plateau exponential-tail detector", log);
            Assert.Contains("decision=ExponentialTail", log);
            Assert.Contains("initialA=", log);
            Assert.Contains("tau=", log);
            Assert.Contains("fitImprovement=", log);
            Assert.Contains("individual=", log);
            Assert.Contains("neighbourMedian=", log);
            Assert.Contains("Timing: corrected-data preparation=", log);
            Assert.Contains("Timing: pass 1 peak estimation=", log);
            Assert.Contains("Timing: pass 1 baseline preparation=", log);
            Assert.Contains("Timing: pass 2 peak estimation=", log);
            Assert.Contains("Timing: pass 2 baseline preparation=", log);
            Assert.Contains("Timing: pass 3 peak estimation=", log);
            Assert.Contains("Timing: final baseline preparation=", log);
            Assert.Contains("Timing: final integration=", log);
            Assert.Contains("Timing: targets=3", log);
            Assert.Contains("requestTotal=", log);
            Assert.Contains("[PeakFit] Complete:", log);
        }

        [Fact]
        public async Task FitCalculatesCorrectedDataWhenABaselineIsAvailable()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Segmented);
            Assert.Null(experiment.BaseLineCorrectedDataPoints);

            var result = await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);

            Assert.NotEqual(PeakFitStatus.Failed, result.Status);
            Assert.NotEqual(PeakFitStatus.NoData, result.Status);
            Assert.NotNull(experiment.BaseLineCorrectedDataPoints);
            Assert.Equal(experiment.DataPoints.Count, experiment.BaseLineCorrectedDataPoints.Count);
        }

        [Fact]
        public async Task LockedPeakFitIsASilentNoOp()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline);
            await experiment.Processor.ProcessData(showProgress: false);
            var baseline = experiment.Processor.Interpolator.Baseline.Select(value => value.Value).ToArray();
            experiment.BaseLineCorrectedDataPoints = null;
            var estimatorCalls = 0;
            experiment.Processor.PeakEndOffsetEstimator = _ =>
            {
                estimatorCalls++;
                return experiment.Injections.Select(injection => 20f).ToArray();
            };
            experiment.Processor.Lock();

            var statusUpdates = 0;
            var secondaryStatusUpdates = 0;
            var progressUpdates = 0;
            EventHandler<string> statusHandler = (_, _) => statusUpdates++;
            EventHandler<string> secondaryStatusHandler = (_, _) => secondaryStatusUpdates++;
            EventHandler<ProgressIndicatorEventData> progressHandler = (_, _) => progressUpdates++;
            StatusBarManager.StatusUpdated += statusHandler;
            StatusBarManager.SecondaryStatusUpdated += secondaryStatusHandler;
            StatusBarManager.ProgressUpdate += progressHandler;
            PeakFitResult result;
            try
            {
                result = await experiment.Processor.FitIntegrationPeaksAsync(showProgress: true);
            }
            finally
            {
                StatusBarManager.StatusUpdated -= statusHandler;
                StatusBarManager.SecondaryStatusUpdated -= secondaryStatusHandler;
                StatusBarManager.ProgressUpdate -= progressHandler;
            }

            Assert.Equal(PeakFitStatus.Locked, result.Status);
            Assert.False(result.Succeeded);
            Assert.False(result.RegionsChanged);
            Assert.Equal(0, result.Iterations);
            Assert.Equal(0, estimatorCalls);
            Assert.Null(experiment.BaseLineCorrectedDataPoints);
            Assert.Equal(baseline, experiment.Processor.Interpolator.Baseline.Select(value => value.Value).ToArray());
            Assert.Equal(0, statusUpdates);
            Assert.Equal(0, secondaryStatusUpdates);
            Assert.Equal(0, progressUpdates);
        }

        [Fact]
        public async Task FitFailsWithoutCorrectedDataOrABaseline()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline, initializeBaseline: false);
            var original = experiment.Injections.Select(injection => injection.IntegrationEndOffset).ToArray();

            var result = await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);

            Assert.Equal(PeakFitStatus.Failed, result.Status);
            Assert.False(result.RegionsChanged);
            Assert.Null(experiment.BaseLineCorrectedDataPoints);
            Assert.Equal(original, experiment.Injections.Select(injection => injection.IntegrationEndOffset).ToArray());
        }

        [Fact]
        public async Task SelectedFitDoesNotChangeOtherRegions()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline, initializeBaseline: false);
            experiment.BaseLineCorrectedDataPoints = experiment.DataPoints.ToList();
            experiment.Processor.PeakEndOffsetEstimator = _ => experiment.Injections.Select(injection => 20f).ToArray();
            var original = experiment.Injections.Select(injection => injection.IntegrationEndOffset).ToArray();

            var result = await experiment.Processor.FitIntegrationPeaksAsync(
                new[] { experiment.Injections[1] },
                showProgress: false);

            Assert.True(result.Succeeded);
            Assert.Equal(20f, experiment.Injections[1].IntegrationEndOffset);
            Assert.Equal(original[0], experiment.Injections[0].IntegrationEndOffset);
            Assert.Equal(original[2], experiment.Injections[2].IntegrationEndOffset);
            Assert.Equal(original[3], experiment.Injections[3].IntegrationEndOffset);
        }

        [Fact]
        public async Task RegionIndependentBaselineIsNotChangedByPeakFitting()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline);
            experiment.Processor.DiscardIntegratedPoints = false;
            await experiment.Processor.ProcessData(showProgress: false);
            var baseline = experiment.Processor.Interpolator.Baseline.Select(value => value.Value).ToArray();

            var result = await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);

            Assert.True(result.Succeeded);
            Assert.Equal(baseline, experiment.Processor.Interpolator.Baseline.Select(value => value.Value).ToArray());
        }

        [Fact]
        public async Task LockedProcessorRejectsEveryPublicProcessingEntryPoint()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline);
            await experiment.Processor.ProcessData(showProgress: false);
            var processor = experiment.Processor;
            var baseline = processor.Interpolator.Baseline.Select(value => value.Value).ToArray();
            var corrected = experiment.BaseLineCorrectedDataPoints.Select(value => value.Power).ToArray();
            var peakAreas = experiment.Injections.Select(injection => injection.PeakArea.Value).ToArray();
            var integrated = experiment.Injections.Select(injection => injection.IsIntegrated).ToArray();
            var baselineCompleted = processor.BaselineCompleted;

            var raw = experiment.DataPoints[0];
            experiment.DataPoints[0] = new DataPoint(raw.Time, raw.Power + 1, raw.Temperature);
            experiment.Injections[0].SetIntegrationLengthByTime(20);
            processor.Lock();

            var baselineEvents = 0;
            var processingEvents = 0;
            EventHandler baselineHandler = (sender, _) =>
            {
                if (ReferenceEquals(sender, processor)) baselineEvents++;
            };
            EventHandler processingHandler = (sender, _) =>
            {
                if (ReferenceEquals(sender, experiment)) processingEvents++;
            };
            DataProcessor.BaselineInterpolationCompleted += baselineHandler;
            DataProcessor.ProcessingCompleted += processingHandler;
            try
            {
                processor.WillProcessData();
                await processor.InterpolateBaseline();
                processor.SubtractBaseline();
                processor.IntegratePeaks();
                processor.DidProcessData();
                await processor.ProcessData(showProgress: false);
            }
            finally
            {
                DataProcessor.BaselineInterpolationCompleted -= baselineHandler;
                DataProcessor.ProcessingCompleted -= processingHandler;
            }

            Assert.Equal(baselineCompleted, processor.BaselineCompleted);
            Assert.Equal(integrated, experiment.Injections.Select(injection => injection.IsIntegrated).ToArray());
            Assert.Equal(baseline, processor.Interpolator.Baseline.Select(value => value.Value).ToArray());
            Assert.Equal(corrected, experiment.BaseLineCorrectedDataPoints.Select(value => value.Power).ToArray());
            Assert.Equal(peakAreas, experiment.Injections.Select(injection => injection.PeakArea.Value).ToArray());
            Assert.Equal(0, baselineEvents);
            Assert.Equal(0, processingEvents);

            processor.Unlock();
            await processor.ProcessData(showProgress: false);

            Assert.True(processor.BaselineCompleted);
            Assert.All(experiment.Injections, injection => Assert.True(injection.IsIntegrated));
            Assert.NotEqual(corrected, experiment.BaseLineCorrectedDataPoints.Select(value => value.Power).ToArray());
        }

        [Fact]
        public async Task LockLetsActivePeakFitFinishAndRejectsQueuedFit()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline, initializeBaseline: false);
            experiment.BaseLineCorrectedDataPoints = experiment.DataPoints.ToList();
            var estimatorStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var releaseEstimator = new ManualResetEventSlim(false);
            var estimatorCalls = 0;
            experiment.Processor.PeakEndOffsetEstimator = _ =>
            {
                if (Interlocked.Increment(ref estimatorCalls) == 1)
                {
                    estimatorStarted.TrySetResult(true);
                    releaseEstimator.Wait();
                }

                return experiment.Injections.Select(injection => 24f).ToArray();
            };

            var active = experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);
            await estimatorStarted.Task;
            var queued = experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);
            experiment.Processor.Lock();
            releaseEstimator.Set();

            var activeResult = await active;
            var queuedResult = await queued;

            Assert.True(activeResult.Succeeded);
            Assert.Equal(PeakFitStatus.Locked, queuedResult.Status);
            Assert.Equal(DataProcessor.PeakFitPassCount, estimatorCalls);
            Assert.All(experiment.Injections, injection => Assert.Equal(24f, injection.IntegrationEndOffset));
        }

        [Fact]
        public async Task CustomEstimatorRunsOncePerPassAgainstFrozenTraces()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline, initializeBaseline: false);
            experiment.BaseLineCorrectedDataPoints = experiment.DataPoints.ToList();
            var call = 0;
            experiment.Processor.PeakEndOffsetEstimator = _ =>
            {
                call++;
                return experiment.Injections.Select(injection => 20f).ToArray();
            };

            var result = await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);

            Assert.Equal(PeakFitStatus.Converged, result.Status);
            Assert.Equal(DataProcessor.PeakFitPassCount, result.Iterations);
            Assert.Equal(DataProcessor.PeakFitPassCount, call);
            Assert.All(experiment.Injections, injection => Assert.Equal(20f, injection.IntegrationEndOffset));
        }

        [Fact]
        public async Task AlternatingEstimatorCycleIsResolvedToStableMidpoint()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline, initializeBaseline: false);
            experiment.BaseLineCorrectedDataPoints = experiment.DataPoints.ToList();
            var call = 0;
            experiment.Processor.PeakEndOffsetEstimator = _ =>
            {
                var offset = Interlocked.Increment(ref call) % 2 == 1 ? 20f : 30f;
                return experiment.Injections.Select(injection => offset).ToArray();
            };

            var first = await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);
            var second = await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);

            Assert.Equal(PeakFitStatus.CycleResolved, first.Status);
            Assert.Equal(PeakFitStatus.CycleResolved, second.Status);
            Assert.True(first.RegionsChanged);
            Assert.False(second.RegionsChanged);
            Assert.All(experiment.Injections, injection => Assert.Equal(25f, injection.IntegrationEndOffset));
        }

        [Fact]
        public async Task NonConvergentEstimatorRestoresOriginalRegions()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline, initializeBaseline: false);
            experiment.BaseLineCorrectedDataPoints = experiment.DataPoints.ToList();
            var original = experiment.Injections.Select(injection => injection.IntegrationEndOffset).ToArray();
            var call = 0;
            experiment.Processor.PeakEndOffsetEstimator = _ =>
            {
                var offset = 12f + 3f * Interlocked.Increment(ref call);
                return experiment.Injections.Select(injection => offset).ToArray();
            };

            var result = await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);

            Assert.Equal(PeakFitStatus.NonConvergent, result.Status);
            Assert.Equal(DataProcessor.PeakFitMaximumPassCount, result.Iterations);
            Assert.False(result.Succeeded);
            Assert.False(result.RegionsChanged);
            Assert.Equal(original, experiment.Injections.Select(injection => injection.IntegrationEndOffset).ToArray());
        }

        [Fact]
        public async Task FitRunsOnAWorkerThreadAndReportsPassProgress()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline, initializeBaseline: false);
            experiment.BaseLineCorrectedDataPoints = experiment.DataPoints.ToList();

            var callerThread = Environment.CurrentManagedThreadId;
            var estimatorThreads = new ConcurrentQueue<int>();
            var estimatorStarted = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var releaseEstimator = new ManualResetEventSlim(false);
            experiment.Processor.PeakEndOffsetEstimator = _ =>
            {
                var thread = Environment.CurrentManagedThreadId;
                estimatorThreads.Enqueue(thread);
                estimatorStarted.TrySetResult(thread);
                releaseEstimator.Wait(TimeSpan.FromSeconds(5));
                return experiment.Injections.Select(injection => 24f).ToArray();
            };

            var statuses = new ConcurrentQueue<string>();
            var secondaryStatuses = new ConcurrentQueue<string>();
            var progressUpdates = new ConcurrentQueue<double>();
            EventHandler<string> statusHandler = (_, status) => statuses.Enqueue(status);
            EventHandler<string> secondaryHandler = (_, status) => secondaryStatuses.Enqueue(status);
            EventHandler<ProgressIndicatorEventData> progressHandler =
                (_, progress) => progressUpdates.Enqueue(progress.Progress);

            StatusBarManager.ClearAppStatus();
            StatusBarManager.StatusUpdated += statusHandler;
            StatusBarManager.SecondaryStatusUpdated += secondaryHandler;
            StatusBarManager.ProgressUpdate += progressHandler;

            PeakFitResult result;
            try
            {
                var fitTask = experiment.Processor.FitIntegrationPeaksAsync(showProgress: true);
                try
                {
                    var workerThread = await estimatorStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.NotEqual(callerThread, workerThread);
                }
                finally
                {
                    releaseEstimator.Set();
                }

                result = await fitTask;
            }
            finally
            {
                StatusBarManager.StatusUpdated -= statusHandler;
                StatusBarManager.SecondaryStatusUpdated -= secondaryHandler;
                StatusBarManager.ProgressUpdate -= progressHandler;
                StatusBarManager.ClearAppStatus();
            }

            Assert.True(result.Succeeded);
            Assert.Equal(DataProcessor.PeakFitPassCount, estimatorThreads.Count);
            Assert.All(estimatorThreads, thread => Assert.NotEqual(callerThread, thread));
            Assert.Contains(statuses, status => status.Contains("pass 1"));
            Assert.Contains(statuses, status => status.Contains("pass 2"));
            Assert.Contains(statuses, status => status.Contains("pass 3"));
            Assert.Contains(secondaryStatuses, status => status == "Pass 1");
            Assert.Contains(progressUpdates, progress => progress == 0);
            Assert.Contains(progressUpdates, progress => progress > 0 && progress < 1);
            Assert.Contains(progressUpdates, progress => progress == 1);
        }

        [Fact]
        public async Task RegionChangesRefitAnUnlockedRegionDependentBaseline()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline);
            await experiment.Processor.ProcessData(showProgress: false);
            var baseline = experiment.Processor.Interpolator.Baseline.Select(point => point.Value).ToArray();
            var corrected = experiment.BaseLineCorrectedDataPoints.Select(point => point.Power).ToArray();
            experiment.Processor.PeakEndOffsetEstimator = _ =>
                experiment.Injections.Select(injection => 24f).ToArray();

            var result = await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);

            Assert.True(result.Succeeded);
            Assert.True(result.RegionsChanged);
            Assert.NotEqual(baseline, experiment.Processor.Interpolator.Baseline.Select(point => point.Value).ToArray());
            Assert.NotEqual(corrected, experiment.BaseLineCorrectedDataPoints.Select(point => point.Power).ToArray());
        }

        [Fact]
        public async Task OverlappingFitRequestsPublishTheSameResult()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline, initializeBaseline: false);
            experiment.BaseLineCorrectedDataPoints = experiment.DataPoints.ToList();
            experiment.Processor.PeakEndOffsetEstimator = _ => experiment.Injections.Select(injection => 24f).ToArray();

            var results = await Task.WhenAll(
                experiment.Processor.FitIntegrationPeaksAsync(showProgress: false),
                experiment.Processor.FitIntegrationPeaksAsync(showProgress: false));

            Assert.All(results, result => Assert.True(result.Succeeded));
            Assert.All(experiment.Injections, injection => Assert.Equal(24f, injection.IntegrationEndOffset));
        }

        [Fact]
        public async Task FitPublishesOnlyOneFinalProcessingNotification()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline);
            await experiment.Processor.ProcessData(showProgress: false);
            var processingNotifications = 0;
            var baselineNotifications = 0;
            EventHandler processingHandler = (sender, args) =>
            {
                if (ReferenceEquals(sender, experiment)) processingNotifications++;
            };
            EventHandler baselineHandler = (sender, args) =>
            {
                if (ReferenceEquals(sender, experiment.Processor)) baselineNotifications++;
            };

            DataProcessor.ProcessingCompleted += processingHandler;
            DataProcessor.BaselineInterpolationCompleted += baselineHandler;
            try
            {
                await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);
            }
            finally
            {
                DataProcessor.ProcessingCompleted -= processingHandler;
                DataProcessor.BaselineInterpolationCompleted -= baselineHandler;
            }

            Assert.Equal(1, processingNotifications);
            Assert.Equal(0, baselineNotifications);
        }

        [Fact]
        public void ManualAndFactorModesDoNotUseTheIterativeFitPath()
        {
            var experiment = CreateSyntheticExperiment(BaselineInterpolatorTypes.Spline, initializeBaseline: false);
            experiment.Processor.PeakEndOffsetEstimator = _ => throw new InvalidOperationException("Iterative estimator should not be called");

            experiment.SetIntegrationLengthByTime(42);
            Assert.All(experiment.Injections, injection => Assert.Equal(42f, injection.IntegrationEndOffset));

            experiment.SetIntegrationLengthByFactor(1.2f);
            Assert.All(experiment.Injections, injection =>
            {
                Assert.True(injection.IntegrationEndOffset > injection.IntegrationStartDelay);
                Assert.True(injection.IntegrationEndOffset <= injection.Delay);
            });
        }

        [Fact]
        public async Task BundledThermogramProducesStableInScopeRegions()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "data_1.itc");
            var previousCulture = Thread.CurrentThread.CurrentCulture;
            ExperimentData experiment;
            try
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                experiment = MicroCalITC200Reader.ReadPath(path);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previousCulture;
            }
            experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Spline);
            await experiment.Processor.ProcessData(showProgress: false);

            var first = await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);
            var offsets = experiment.Injections.Select(injection => injection.IntegrationEndOffset).ToArray();
            var second = await experiment.Processor.FitIntegrationPeaksAsync(showProgress: false);

            Assert.True(first.Succeeded);
            Assert.True(second.Succeeded);
            Assert.Equal(offsets, experiment.Injections.Select(injection => injection.IntegrationEndOffset).ToArray());
            Assert.All(experiment.Injections, injection =>
            {
                Assert.True(injection.IntegrationEndOffset > injection.IntegrationStartDelay);
                Assert.True(injection.IntegrationEndOffset <= injection.Delay);
                Assert.True(injection.IsIntegrated);
            });
        }

        static ExperimentData CreateSyntheticExperiment(BaselineInterpolatorTypes baselineType, bool initializeBaseline = true)
        {
            var experiment = new ExperimentData("synthetic.itc")
            {
                InitialDelay = 30,
                CellVolume = 0.0002,
                CellConcentration = new FloatWithError(0.0001),
                SyringeConcentration = new FloatWithError(0.001),
            };

            var injectionTimes = new[] { 40f, 120f, 200f, 280f };
            for (int i = 0; i <= 370; i++)
            {
                var time = (float)i;
                var power = 2.0e-5 + (1.0e-8 * time) + (2.0e-6 * Math.Sin(time / 65.0));
                for (int injectionIndex = 0; injectionIndex < injectionTimes.Length; injectionIndex++)
                {
                    var elapsed = time - injectionTimes[injectionIndex];
                    if (elapsed >= 0 && elapsed <= 80)
                        power -= (8.0e-5 - injectionIndex * 7.0e-6) * Math.Exp(-elapsed / (8.0 + injectionIndex));
                }
                power += 2.0e-7 * Math.Sin(time * 1.73);
                experiment.DataPoints.Add(new DataPoint(time, (float)power, 25));
            }

            for (int i = 0; i < injectionTimes.Length; i++)
            {
                var injection = InjectionData.FromPEAQFile(
                    experiment,
                    i,
                    include: true,
                    time: injectionTimes[i],
                    volume: 2.0e-6,
                    delay: 80,
                    duration: 2,
                    temperature: 25);
                experiment.Injections.Add(injection);
                injection.SetIntegrationLengthByTime(35);
            }

            if (initializeBaseline)
                experiment.Processor.InitializeBaseline(baselineType);

            return experiment;
        }

        static ExperimentData CreateCorrectedExperiment(
            float sampleInterval,
            float[] injectionTimes,
            Func<int, double, double> peakShape,
            float delay = 60)
        {
            var experiment = new ExperimentData("corrected-synthetic.itc")
            {
                InitialDelay = injectionTimes.First(),
                CellVolume = 0.0002,
                CellConcentration = new FloatWithError(0.0001),
                SyringeConcentration = new FloatWithError(0.001),
            };

            var endTime = injectionTimes.Last() + delay + 20;
            var corrected = new List<DataPoint>();
            var sampleIndex = 0;
            for (float time = 0; time <= endTime + 0.001f; time += sampleInterval)
            {
                var noise = sampleIndex++ % 2 == 0 ? 0.1 : -0.1;
                double signal = noise;
                for (int injectionIndex = 0; injectionIndex < injectionTimes.Length; injectionIndex++)
                {
                    var elapsed = time - injectionTimes[injectionIndex];
                    if (elapsed >= 0 && elapsed <= delay)
                        signal += peakShape(injectionIndex, elapsed);
                }

                corrected.Add(new DataPoint(time, (float)signal, 25));
                experiment.DataPoints.Add(new DataPoint(time, (float)(25 + 0.01 * time + signal), 25));
            }

            experiment.BaseLineCorrectedDataPoints = corrected;
            for (int i = 0; i < injectionTimes.Length; i++)
            {
                var injection = InjectionData.FromPEAQFile(
                    experiment,
                    i,
                    include: true,
                    time: injectionTimes[i],
                    volume: 2.0e-6,
                    delay: delay,
                    duration: 2,
                    temperature: 25);
                experiment.Injections.Add(injection);
                injection.SetIntegrationLengthByTime(40);
            }

            return experiment;
        }

        static double NonExponentialPeak(double elapsed)
        {
            if (elapsed < 0 || elapsed > 16) return 0;
            if (elapsed <= 3) return 6 + (4 * elapsed / 3.0);

            var fraction = (elapsed - 3) / 13.0;
            return 10 * Math.Pow(1 - fraction, 2);
        }

        static double TriangularPulse(double elapsed, double center, double halfWidth, double amplitude)
        {
            var distance = Math.Abs(elapsed - center);
            if (distance >= halfWidth) return 0;
            return amplitude * (1 - distance / halfWidth);
        }
    }
}
