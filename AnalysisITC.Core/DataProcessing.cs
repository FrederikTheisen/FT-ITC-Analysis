using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MathNet.Numerics.Interpolation;
using MathNet.Numerics.LinearAlgebra.Double;
using MathNet.Numerics.LinearAlgebra.Solvers;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;
using AnalysisITC.Platform;

namespace AnalysisITC.Core.Processing
{
    public class DataProcessor
    {
        public const int PeakFitPassCount = 3;

        public static event EventHandler BaselineInterpolationCompleted;
        public static event EventHandler ProcessingCompleted;

        internal ExperimentData Data { get; set; }
        public bool IsLocked { get; private set; } = false;

        CancellationToken cToken { get; set; }
        CancellationTokenSource csource = new CancellationTokenSource();
        readonly SemaphoreSlim processingGate = new SemaphoreSlim(1, 1);

        internal Func<IReadOnlyList<DataPoint>, float[]> PeakEndOffsetEstimator { get; set; }

        public BaselineInterpolator Interpolator { get; set; }
        public BaselineInterpolatorTypes BaselineType
        {
            get
            {
                if (Interpolator == null) return BaselineInterpolatorTypes.None;
                else
                {
                    switch (Interpolator)
                    {
                        case SplineInterpolator: return BaselineInterpolatorTypes.Spline;
                        case AssymetricLeastSquaresInterpolator: return BaselineInterpolatorTypes.ASL;
                        case PolynomialLeastSquaresInterpolator: return BaselineInterpolatorTypes.Polynomial;
                        case SegmentedBaselineInterpolator: return BaselineInterpolatorTypes.Segmented;
                        default: return BaselineInterpolatorTypes.None;
                    }
                }
            }
        }
        public bool DiscardIntegratedPoints { get; set; } = true;
        public InjectionData.IntegrationLengthMode IntegrationLengthMode { get; set; } = InjectionData.IntegrationLengthMode.Time;
        public float IntegrationLengthFactor { get; set; } = 2;

        public bool BaselineCompleted { get; internal set; } = false;
        public bool IntegrationCompleted => Data.Injections.All(inj => inj.IsIntegrated);


        public DataProcessor(ExperimentData data)
        {
            Data = data;

            DiscardIntegratedPoints = AppSettings.DiscardIntegrationRegionForBaseline;
        }

        public DataProcessor(ExperimentData data, DataProcessor dataProcessor)
        {
            Data = data;
            DiscardIntegratedPoints = dataProcessor.DiscardIntegratedPoints;
            IntegrationLengthFactor = dataProcessor.IntegrationLengthFactor;
            IntegrationLengthMode = dataProcessor.IntegrationLengthMode;
            BaselineCompleted = dataProcessor.BaselineCompleted;
            if (dataProcessor.Interpolator != null) Interpolator = dataProcessor.Interpolator.Copy(this);
            if (dataProcessor.IsLocked) Lock();
        }

        public void InitializeBaseline(BaselineInterpolatorTypes mode)
        {
            switch (mode)
            {
                case BaselineInterpolatorTypes.None: break;
                case BaselineInterpolatorTypes.Spline: Interpolator = new SplineInterpolator(this); break;
                case BaselineInterpolatorTypes.Polynomial: Interpolator = new PolynomialLeastSquaresInterpolator(this); break;
                case BaselineInterpolatorTypes.Segmented: Interpolator = new SegmentedBaselineInterpolator(this); break;
                case BaselineInterpolatorTypes.ASL:
                default: Interpolator = new SplineInterpolator(this); break;
            }
        }

        public async Task ProcessData(bool replace = true, bool invalidate = true, bool showProgress = true)
        {
            if (BaselineType == BaselineInterpolatorTypes.None) return;

            await processingGate.WaitAsync();
            try
            {
                await ProcessDataCore(replace, invalidate, showProgress);
            }
            finally
            {
                processingGate.Release();
            }
        }

        async Task ProcessDataCore(bool replace, bool invalidate, bool showProgress)
        {

            if (showProgress) StatusBarManager.StartInderminateProgress();

            this.WillProcessData(invalidate);
            await this.InterpolateBaseline(replace);
            this.IntegratePeaks(invalidate);
            this.DidProcessData(invalidate);

            if (showProgress) StatusBarManager.StopIndeterminateProgress();
        }

        public async Task<PeakFitResult> FitIntegrationPeaksAsync(
            IEnumerable<InjectionData> targetInjections = null,
            bool invalidate = true,
            bool showProgress = true)
        {
            var requestTimer = Stopwatch.StartNew();
            var gateTimer = Stopwatch.StartNew();
            var gateWaitMilliseconds = 0d;
            var workerMilliseconds = 0d;
            var publicationMilliseconds = 0d;
            PeakFitResult result = null;
            var targets = targetInjections?.ToArray();
            var targetCount = targets?.Length ?? Data?.Injections?.Count ?? 0;
            ReportPeakFitProgress(
                showProgress,
                "Waiting to fit injection peaks...",
                "Preparing",
                0);

            await processingGate.WaitAsync();
            gateWaitMilliseconds = gateTimer.Elapsed.TotalMilliseconds;
            try
            {
                ReportPeakFitProgress(
                    showProgress,
                    "Preparing injection peak fitting...",
                    "Preparing corrected data",
                    0.025);

                var workerTimer = Stopwatch.StartNew();
                result = await Task.Run(() => FitIntegrationPeaksCoreAsync(
                    targets,
                    showProgress));
                workerMilliseconds = workerTimer.Elapsed.TotalMilliseconds;

                var publicationTimer = Stopwatch.StartNew();
                if (result.Status != PeakFitStatus.NoData)
                    DidProcessData(invalidate);
                publicationMilliseconds = publicationTimer.Elapsed.TotalMilliseconds;

                return result;
            }
            finally
            {
                requestTimer.Stop();
                LogPeakFit(
                    $"Timing: targets={targetCount}, gateWait={FormatPeakFitMilliseconds(gateWaitMilliseconds)}, " +
                    $"worker={FormatPeakFitMilliseconds(workerMilliseconds)}, " +
                    $"publication={FormatPeakFitMilliseconds(publicationMilliseconds)}, " +
                    $"requestTotal={FormatPeakFitMilliseconds(requestTimer.Elapsed.TotalMilliseconds)}.");
                processingGate.Release();
                CompletePeakFitProgress(showProgress, result);
            }
        }

        async Task<PeakFitResult> FitIntegrationPeaksCoreAsync(
            IEnumerable<InjectionData> targetInjections,
            bool showProgress)
        {
            if (Data?.Injections == null || Data.Injections.Count == 0 || Data.DataPoints == null || Data.DataPoints.Count == 0)
            {
                LogPeakFit("Skipped: the experiment has no injections or thermogram samples.");
                return new PeakFitResult(PeakFitStatus.NoData, 0, false);
            }

            var targetSet = targetInjections == null
                ? new HashSet<InjectionData>(Data.Injections)
                : new HashSet<InjectionData>(targetInjections.Where(inj => inj != null && Data.Injections.Contains(inj)));

            if (targetSet.Count == 0)
            {
                LogPeakFit("Skipped: no valid target injections were supplied.");
                return new PeakFitResult(PeakFitStatus.NoData, 0, false);
            }

            var targetIndices = Data.Injections
                .Select((injection, index) => new { injection, index })
                .Where(item => targetSet.Contains(item.injection))
                .Select(item => item.index)
                .ToArray();

            var original = CapturePeakFitState(targetIndices);
            var iterations = 0;
            var finalState = original;
            var coreTimer = Stopwatch.StartNew();

            LogPeakFit(
                $"Start: experiment=\"{Data.FileName}\", targets={FormatPeakFitTargets(targetIndices)}, " +
                $"baseline={BaselineType}, locked={IsLocked}, discardIntegratedPoints={DiscardIntegratedPoints}, " +
                $"baselineRefitEnabled={CanRefitBaseline()}, passes={PeakFitPassCount}, " +
                $"samples={Data.DataPoints.Count}, dt={FormatPeakFitNumber(Data.TimeStep)} s.");
            LogPeakFit(
                $"Criteria: fitStart≥{FormatPeakFitNumber(CorrectedPeakEndEstimator.TailFitStartOffsetSeconds)} s, " +
                $"model=A*exp(-x/tau), plateau=0, " +
                $"endpoint=fitStart+{FormatPeakFitNumber(CorrectedPeakEndEstimator.EndpointTauMultiples)}tau, " +
                $"minimumFitImprovement={FormatPeakFitNumber(100 * CorrectedPeakEndEstimator.MinimumFitImprovement)}%, " +
                $"neighbourFilter=median(previous,self,next) for interior injections.",
                1);
            LogPeakFit($"Starting endpoints: {FormatPeakFitState(original, targetIndices)}", 1);

            try
            {
                ReportPeakFitProgress(
                    showProgress,
                    "Preparing baseline-corrected peak data...",
                    "Preparing corrected data",
                    0.05);

                var correctedDataTimer = Stopwatch.StartNew();
                var correctedTraceSource = "existing corrected trace";
                if (!HasValidCorrectedTrace() && CanCalculateBaseline())
                {
                    correctedTraceSource = "new baseline interpolation";
                    LogPeakFit("Corrected trace is missing; calculating the baseline before detection.", 1);
                    await InterpolateBaseline(replace: true, notify: false, throwOnError: true);
                }

                if (!HasValidCorrectedTrace())
                {
                    correctedTraceSource = "stored fixed baseline";
                    RefreshCorrectedTraceFromCurrentBaseline();
                }

                if (!HasValidCorrectedTrace())
                    throw new InvalidOperationException("Peak fitting requires baseline-corrected data.");

                LogPeakFit(
                    $"Corrected trace ready: source={correctedTraceSource}, points={Data.BaseLineCorrectedDataPoints.Count}.",
                    1);
                LogPeakFitTiming("corrected-data preparation", correctedDataTimer);

                for (iterations = 1; iterations <= PeakFitPassCount; iterations++)
                {
                    ReportPeakFitProgress(
                        showProgress,
                        $"Fitting injection peaks...",
                        $"Pass {iterations} of {PeakFitPassCount}",
                        0.1 + 0.25 * (iterations - 1));

                    var previousState = finalState;
                    var frozenCorrectedTrace = Data.BaseLineCorrectedDataPoints.ToArray();
                    LogPeakFit(
                        $"Pass {iterations}/{PeakFitPassCount}: froze {frozenCorrectedTrace.Length} corrected samples.",
                        1);

                    var estimationTimer = Stopwatch.StartNew();
                    var estimatedState = EstimatePeakFitState(
                        frozenCorrectedTrace,
                        targetIndices,
                        out var detection);
                    estimationTimer.Stop();
                    finalState = StabilizeOneSampleEndpointJitter(
                        previousState,
                        estimatedState,
                        targetIndices,
                        out int stabilizedEndpointCount);
                    if (stabilizedEndpointCount > 0)
                    {
                        LogPeakFit(
                            $"Pass {iterations}/{PeakFitPassCount}: retained {stabilizedEndpointCount} endpoint(s) " +
                            "whose candidate moved by only one thermogram sample.",
                            2);
                    }
                    LogPeakFitIteration(
                        iterations,
                        previousState,
                        finalState,
                        detection,
                        targetIndices);
                    ApplyPeakFitState(finalState);
                    LogPeakFitTiming($"pass {iterations} peak estimation", estimationTimer);

                    if (iterations < PeakFitPassCount)
                    {
                        Stopwatch baselineTimer;
                        if (CanRefitBaseline())
                        {
                            ReportPeakFitProgress(
                                showProgress,
                                $"Recalculating baseline after peak-fit pass {iterations}...",
                                $"Pass {iterations} of {PeakFitPassCount - 1}",
                                0.25 + 0.25 * (iterations - 1));
                            LogPeakFit(
                                $"Pass {iterations}/{PeakFitPassCount}: recalculating the baseline for the candidate regions.",
                                1);
                            baselineTimer = Stopwatch.StartNew();
                            await InterpolateBaseline(
                                replace: true,
                                notify: false,
                                throwOnError: true);
                        }
                        else
                        {
                            ReportPeakFitProgress(
                                showProgress,
                                $"Preparing peak-fit pass {iterations + 1} of {PeakFitPassCount}...",
                                $"Pass {iterations} of {PeakFitPassCount - 1}",
                                0.25 + 0.25 * (iterations - 1));
                            LogPeakFit(
                                $"Pass {iterations}/{PeakFitPassCount}: baseline is fixed or region-independent; reusing it.",
                                1);
                            baselineTimer = Stopwatch.StartNew();
                            RefreshCorrectedTraceFromCurrentBaseline();
                        }
                        LogPeakFitTiming($"pass {iterations} baseline preparation", baselineTimer);

                        if (!HasValidCorrectedTrace())
                            throw new InvalidOperationException("Peak fitting lost its baseline-corrected data.");
                    }
                }

                // The final baseline must correspond to the regions committed by pass three.
                Stopwatch finalBaselineTimer;
                if (CanRefitBaseline())
                {
                    ReportPeakFitProgress(
                        showProgress,
                        "Updating the final baseline...",
                        $"Pass {iterations} of {PeakFitPassCount - 1}",
                        0.85);
                    LogPeakFit("Recalculating the final baseline for the committed regions.", 1);
                    finalBaselineTimer = Stopwatch.StartNew();
                    await InterpolateBaseline(
                        replace: true,
                        notify: false,
                        throwOnError: true);
                }
                else
                {
                    finalBaselineTimer = Stopwatch.StartNew();
                    RefreshCorrectedTraceFromCurrentBaseline();
                }
                LogPeakFitTiming("final baseline preparation", finalBaselineTimer);

                ReportPeakFitProgress(
                    showProgress,
                    "Integrating fitted injection peaks...",
                    $"Pass {iterations} of {PeakFitPassCount - 1}",
                    0.95);
                var integrationTimer = Stopwatch.StartNew();
                if (HasValidCorrectedTrace())
                    IntegratePeaks(invalidate: false, notify: false);
                LogPeakFitTiming("final integration", integrationTimer);

                var regionsChanged = !finalState.HasSameOffsets(original);
                coreTimer.Stop();
                LogPeakFit(
                    $"Complete: status={PeakFitStatus.Converged}, iterations={PeakFitPassCount}, " +
                    $"regionsChanged={regionsChanged}, finalEndpoints={FormatPeakFitState(finalState, targetIndices)}, " +
                    $"coreTotal={FormatPeakFitMilliseconds(coreTimer.Elapsed.TotalMilliseconds)}.");

                return new PeakFitResult(
                    PeakFitStatus.Converged,
                    PeakFitPassCount,
                    regionsChanged);
            }
            catch (Exception ex)
            {
                coreTimer.Stop();
                ReportPeakFitProgress(
                    showProgress,
                    "Peak fitting failed; restoring the original regions...",
                    "Rolling back",
                    0.95);
                LogPeakFit($"Failed after iteration {iterations}: {ex.Message}");
                LogPeakFit(ex.StackTrace ?? "No stack trace available.", 1);

                ApplyPeakFitState(original);
                if (CanRefitBaseline())
                {
                    await InterpolateBaseline(
                        replace: true,
                        notify: false,
                        throwOnError: false);
                }
                else
                {
                    RefreshCorrectedTraceFromCurrentBaseline();
                }

                if (HasValidCorrectedTrace())
                    IntegratePeaks(invalidate: false, notify: false);

                LogPeakFit($"Failure rollback complete: endpoints={FormatPeakFitState(original, targetIndices)}.");
                LogPeakFit(
                    $"Timing: failed core total={FormatPeakFitMilliseconds(coreTimer.Elapsed.TotalMilliseconds)}.",
                    1);
                return new PeakFitResult(PeakFitStatus.Failed, iterations, false);
            }
        }

        static void ReportPeakFitProgress(
            bool showProgress,
            string status,
            string secondaryStatus,
            double progress)
        {
            if (!showProgress) return;

            PlatformServices.MainThreadDispatcher.Invoke(() =>
            {
                StatusBarManager.SetStatus(status, 0, priority: 1);
                StatusBarManager.SetSecondaryStatus(secondaryStatus, 0);
                StatusBarManager.SetProgress(progress);
            });
        }

        static void CompletePeakFitProgress(bool showProgress, PeakFitResult result)
        {
            if (!showProgress) return;

            PlatformServices.MainThreadDispatcher.Invoke(() =>
            {
                StatusBarManager.ClearAppStatus();
                StatusBarManager.SetProgress(1);
                StatusBarManager.SetStatus(result?.Status switch
                {
                    PeakFitStatus.Converged => $"Injection peaks fitted in {result.Iterations} passes",
                    PeakFitStatus.CycleResolved => "Injection peak fitting completed",
                    PeakFitStatus.NonConvergent => "Injection peak fitting did not converge",
                    PeakFitStatus.NoData => "No injection peak data available to fit",
                    _ => "Injection peak fitting failed",
                }, 3000);
            });
        }

        bool CanCalculateBaseline() =>
            Interpolator != null && !IsLocked;

        bool CanRefitBaseline() =>
            Interpolator != null && DiscardIntegratedPoints && !IsLocked;

        bool HasValidCorrectedTrace() =>
            Data.BaseLineCorrectedDataPoints != null &&
            Data.BaseLineCorrectedDataPoints.Count == Data.DataPoints.Count;

        void RefreshCorrectedTraceFromCurrentBaseline()
        {
            if (Interpolator?.Baseline != null && Interpolator.Baseline.Count == Data.DataPoints.Count)
                SubtractBaseline();
        }

        PeakFitState EstimatePeakFitState(
            IReadOnlyList<DataPoint> trace,
            int[] targetIndices,
            out PeakEndDetectionResult detection)
        {
            float[] estimates;
            if (PeakEndOffsetEstimator != null)
            {
                detection = null;
                estimates = PeakEndOffsetEstimator(trace);
            }
            else
            {
                detection = CorrectedPeakEndEstimator.EstimateEndOffsets(Data, trace);
                estimates = detection.EndOffsets;
            }
            var offsets = Data.Injections.Select(injection => injection.IntegrationEndOffset).ToArray();

            foreach (var index in targetIndices)
            {
                if (index < estimates.Length)
                    offsets[index] = CanonicalizeEndOffset(Data.Injections[index], estimates[index]);
            }

            return CreatePeakFitState(offsets, targetIndices);
        }

        void LogPeakFitIteration(
            int iteration,
            PeakFitState previous,
            PeakFitState candidate,
            PeakEndDetectionResult detection,
            int[] targetIndices)
        {
            var changedCount = candidate.Signature
                .Zip(previous.Signature, (next, current) => next != current)
                .Count(changed => changed);
            var detectorText = detection == null
                ? "custom estimator"
                : "zero-plateau exponential-tail detector";

            LogPeakFit(
                $"Pass {iteration}: changedEndpointSamples={changedCount}/{targetIndices.Length}, {detectorText}.",
                1);

            for (int targetPosition = 0; targetPosition < targetIndices.Length; targetPosition++)
            {
                var injectionIndex = targetIndices[targetPosition];
                var injection = Data.Injections[injectionIndex];
                var changed = previous.Signature[targetPosition] != candidate.Signature[targetPosition];

                if (detection == null || injectionIndex >= detection.Estimates.Length)
                {
                    LogPeakFit(
                        $"Injection {injectionIndex + 1} (id={injection.ID}): custom estimator, " +
                        $"previous={FormatPeakFitNumber(previous.Offsets[injectionIndex])} s, " +
                        $"canonical={FormatPeakFitNumber(candidate.Offsets[injectionIndex])} s, " +
                        $"sampleIndex={candidate.Signature[targetPosition]}, changed={changed}.",
                        2);
                    continue;
                }

                var estimate = detection.Estimates[injectionIndex];
                var detectorReason = estimate.DetectionDecision == estimate.FinalDecision
                    ? estimate.FinalDecision.ToString()
                    : $"{estimate.FinalDecision} (detector={estimate.DetectionDecision})";
                var fitStartText = double.IsNaN(estimate.FitStartOffset)
                    ? "n/a"
                    : FormatPeakFitNumber(estimate.FitStartOffset) + " s";
                var tauText = double.IsNaN(estimate.Tau)
                    ? "n/a"
                    : FormatPeakFitNumber(estimate.Tau) + " s";
                var fitImprovementText = double.IsNaN(estimate.FitImprovement)
                    ? "n/a"
                    : FormatPeakFitNumber(100 * estimate.FitImprovement) + "%";
                var neighbourText = estimate.NeighbourFilterApplied
                    ? $", individual={FormatPeakFitNumber(estimate.IndividualEndOffset)} s, " +
                      $"neighbourMedian={FormatPeakFitNumber(estimate.EndOffset)} s"
                    : $", individual={FormatPeakFitNumber(estimate.EndOffset)} s";

                LogPeakFit(
                    $"Injection {injectionIndex + 1} (id={injection.ID}): decision={detectorReason}, " +
                    $"polarity={estimate.Polarity}, points={estimate.SampleCount}, " +
                    $"fitStart={fitStartText}, initialA={FormatPeakFitScientific(estimate.InitialAmplitude)}, " +
                    $"A={FormatPeakFitScientific(estimate.Amplitude)}, tau={tauText}, " +
                    $"rmse={FormatPeakFitScientific(estimate.RootMeanSquareError)}, " +
                    $"fitImprovement={fitImprovementText}, " +
                    $"previous={FormatPeakFitNumber(previous.Offsets[injectionIndex])} s{neighbourText}, " +
                    $"canonical={FormatPeakFitNumber(candidate.Offsets[injectionIndex])} s, " +
                    $"sampleIndex={candidate.Signature[targetPosition]}, changed={changed}.",
                    2);
            }
        }

        void LogPeakFit(string message, int indentation = 0) =>
            AppEventHandler.PrintAndLog(message, indentation, "PeakFit");

        void LogPeakFitTiming(string phase, Stopwatch timer)
        {
            timer.Stop();
            LogPeakFit($"Timing: {phase}={FormatPeakFitMilliseconds(timer.Elapsed.TotalMilliseconds)}.", 1);
        }

        string FormatPeakFitTargets(int[] targetIndices) =>
            targetIndices.Length == Data.Injections.Count
                ? $"all ({targetIndices.Length})"
                : string.Join(",", targetIndices.Select(index => (index + 1).ToString(CultureInfo.InvariantCulture)));

        static string FormatPeakFitState(PeakFitState state, int[] targetIndices) =>
            string.Join(", ", targetIndices.Select(index =>
                $"inj{index + 1}={FormatPeakFitNumber(state.Offsets[index])}s"));

        static string FormatPeakFitNumber(double value) =>
            double.IsNaN(value) || double.IsInfinity(value)
                ? "n/a"
                : value.ToString("0.###", CultureInfo.InvariantCulture);

        static string FormatPeakFitScientific(double value) =>
            double.IsNaN(value) || double.IsInfinity(value)
                ? "n/a"
                : value.ToString("0.###E+0", CultureInfo.InvariantCulture);

        static string FormatPeakFitMilliseconds(double milliseconds) =>
            milliseconds.ToString("0.###", CultureInfo.InvariantCulture) + " ms";

        PeakFitState CapturePeakFitState(int[] targetIndices) =>
            CreatePeakFitState(Data.Injections.Select(injection => injection.IntegrationEndOffset).ToArray(), targetIndices);

        PeakFitState CreatePeakFitState(float[] offsets, int[] targetIndices)
        {
            var signature = targetIndices
                .Select(index => GetEffectiveEndSampleIndex(Data.Injections[index], offsets[index]))
                .ToArray();
            return new PeakFitState(offsets, signature);
        }

        PeakFitState StabilizeOneSampleEndpointJitter(
            PeakFitState previousState,
            PeakFitState candidateState,
            int[] targetIndices,
            out int stabilizedEndpointCount)
        {
            stabilizedEndpointCount = 0;
            var stabilizedOffsets = candidateState.Offsets.ToArray();

            for (int targetPosition = 0; targetPosition < targetIndices.Length; targetPosition++)
            {
                if (candidateState.Signature[targetPosition] ==
                    previousState.Signature[targetPosition])
                {
                    continue;
                }

                if (Math.Abs(
                        candidateState.Signature[targetPosition] -
                        previousState.Signature[targetPosition]) > 1)
                {
                    continue;
                }

                int injectionIndex = targetIndices[targetPosition];
                stabilizedOffsets[injectionIndex] = previousState.Offsets[injectionIndex];
                stabilizedEndpointCount++;
            }

            return stabilizedEndpointCount == 0
                ? candidateState
                : CreatePeakFitState(stabilizedOffsets, targetIndices);
        }

        float CanonicalizeEndOffset(InjectionData injection, float estimate)
        {
            var minimum = injection.IntegrationStartDelay + Math.Max(2f * (float)Data.TimeStep, 1f);
            var maximum = injection.Delay;
            var clamped = FWEMath.Clamp(estimate, minimum, maximum);

            var candidates = Data.DataPoints
                .Where(point => point.Time - injection.Time >= minimum && point.Time - injection.Time <= maximum)
                .OrderBy(point => Math.Abs((point.Time - injection.Time) - clamped))
                .ToList();

            if (candidates.Count == 0)
                return clamped;

            var nearest = candidates[0];
            return FWEMath.Clamp(nearest.Time - injection.Time, minimum, maximum);
        }

        int GetEffectiveEndSampleIndex(InjectionData injection, float endOffset)
        {
            var endTime = injection.Time + endOffset;
            var index = -1;
            for (int i = 0; i < Data.DataPoints.Count && Data.DataPoints[i].Time <= endTime; i++)
                index = i;
            return index;
        }

        void ApplyPeakFitState(PeakFitState state)
        {
            for (int i = 0; i < Data.Injections.Count && i < state.Offsets.Length; i++)
                Data.Injections[i].SetIntegrationLengthByTime(state.Offsets[i], markModified: false);
        }

        public void Lock() => IsLocked = true;
        public void Unlock() => IsLocked = false;
        public void ToggleLock() => IsLocked = !IsLocked;

        public void WillProcessData(bool invalidate = true)
        {
            BaselineCompleted = false;

            Data.Injections.ForEach(inj => inj.IsIntegrated = false); //FIXME Crashes if not on UI thread
            Data.UpdateProcessing(invalidate);
        }

        public async Task InterpolateBaseline(bool replace = true, bool notify = true, bool throwOnError = false)
        {
            try
            {
                BaselineCompleted = false;
                csource.Cancel();
                csource = new CancellationTokenSource();
                cToken = csource.Token;

                await Task.Run(() => Interpolator.Interpolate(cToken, replace));

                SubtractBaseline();

                BaselineCompleted = true;
                if (notify) BaselineInterpolationCompleted?.Invoke(this, null);
            }
            catch (Exception ex)
            {
                AppEventHandler.PrintAndLog("Baseline Interpolation Error");
                AppEventHandler.PrintAndLog(ex.Message);
                AppEventHandler.PrintAndLog(ex.StackTrace);
                if (throwOnError) throw;
            }
        }

        public void DidProcessData(bool invalidate = true)
        {
            Data.UpdateProcessing(invalidate);

            ProcessingCompleted?.Invoke(Data, null);
        }

        public void SubtractBaseline()
        {
            Data.BaseLineCorrectedDataPoints = new List<DataPoint>();

            foreach (var (dp,bl) in Data.DataPoints.Zip(Interpolator.Baseline, (x, y) => new Tuple<DataPoint, Energy>(x, y)))
            {
                var bldp = dp.SubtractBaseline((float)bl);

                Data.BaseLineCorrectedDataPoints.Add(bldp);
            }

            Data.CalculateExperimentHeatDirection();
        }

        public void IntegratePeaks(bool invalidate = true, bool notify = true)
        {
            if (Data.BaseLineCorrectedDataPoints == null || Data.BaseLineCorrectedDataPoints.Count == 0) return;

            try
            {
                foreach (var inj in Data.Injections)
                {
                    inj.Integrate();
                }
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }

            if (notify)
            {
                Data.UpdateProcessing(invalidate);
                ProcessingCompleted?.Invoke(Data, null);
            }
        }

        sealed class PeakFitState
        {
            public float[] Offsets { get; }
            public int[] Signature { get; }

            public PeakFitState(float[] offsets, int[] signature)
            {
                Offsets = offsets.ToArray();
                Signature = signature.ToArray();
            }

            public bool HasSameSignature(PeakFitState other) => Signature.SequenceEqual(other.Signature);
            public bool HasSameOffsets(PeakFitState other) => Offsets.SequenceEqual(other.Offsets);
        }
    }

    public enum PeakFitStatus
    {
        Converged,
        CycleResolved,
        NonConvergent,
        NoData,
        Failed,
    }

    public sealed class PeakFitResult
    {
        public PeakFitStatus Status { get; }
        public int Iterations { get; }
        public bool RegionsChanged { get; }
        public bool Succeeded => Status == PeakFitStatus.Converged || Status == PeakFitStatus.CycleResolved;

        public PeakFitResult(PeakFitStatus status, int iterations, bool regionsChanged)
        {
            Status = status;
            Iterations = iterations;
            RegionsChanged = regionsChanged;
        }
    }

    public class BaselineInterpolator
    {
        public DataProcessor Processor { get; set; }
        public List<Energy> Baseline { get; set; } = new List<Energy>();
        public bool IsLocked => Processor.IsLocked;
        
        internal ExperimentData Data => Processor.Data;
        public SplineInterpolator SplineInterpolator => this as SplineInterpolator;
        public PolynomialLeastSquaresInterpolator PolynomialLeastSquaresInterpolator => this as PolynomialLeastSquaresInterpolator;
        public SegmentedBaselineInterpolator SegmentedBaselineInterpolator => this as SegmentedBaselineInterpolator;

        public bool Finished => Baseline.Count > 0;

        public BaselineInterpolator(DataProcessor processor)
        {
            Processor = processor;
            Processor.Unlock();

            Baseline = new List<Energy>();
        }

        public virtual BaselineInterpolator Copy(DataProcessor processor)
        {
            var interpolator = new BaselineInterpolator(processor);
            interpolator.CopyBaselineFrom(this);

            return interpolator;
        }

        protected void CopyBaselineFrom(BaselineInterpolator interpolator)
        {
            Baseline = interpolator.Baseline.ToList();
        }

        public List<DataPoint> GetInterpolatedDataPoints(double start, double end)
        {
            var datapoints = Data.DataPoints.Where(dp => dp.Time >= start && dp.Time <= end);

            if (Processor.DiscardIntegratedPoints)
            {
                foreach (var inj in Data.Injections)
                {
                    datapoints = datapoints.Where(dp => dp.Time < inj.IntegrationStartTime || dp.Time > inj.IntegrationEndTime);
                }
            }

            return datapoints.ToList();
        }

        public async virtual Task Interpolate(CancellationToken token, bool replace = true)
        {

        }

        public void WriteToConsole()
        {
            for (int i = 0; i < Data.DataPoints.Count; i++)
            {
                Console.WriteLine(Data.DataPoints[i].Time + " " + Data.DataPoints[i].Power + " " + Baseline[i]);
            }
        }

        public void ConvertToSpline(int pointdensity = 2)
        {
            if (this is SplineInterpolator) return;
            pointdensity = Math.Max(1, pointdensity);

            int num_of_points = (Data.InjectionCount + 1) * pointdensity;

            int skip = Math.Max(1, Baseline.Count / (num_of_points + 1));

            var interpolator = new SplineInterpolator(Processor)
            {
                PointsPerInjection = pointdensity,
                Algorithm = SplineInterpolator.PolynomialToSplineConversionTargetAlgorithm
            };
            
            //interpolator.IsLocked = true;

            int k = 0;
            for (int i = skip; i < Baseline.Count - 1; i += skip)
            {
                var time = Data.DataPoints[i].Time;
                var val = Baseline[i].Value;
                var slope = (Baseline[i + 1].Value - Baseline[i - 1].Value) / 2;

                interpolator.SplinePoints.Add(new SplineInterpolator.SplinePoint(time, val, k, slope));
                k++;
            }

            Processor.Interpolator = interpolator;
            _ = Processor.ProcessData();
            Processor.Lock();
        }
    }

    public enum BaselineInterpolatorTypes
    {
        None = -1,
        Spline = 0,
        ASL = 1,
        Polynomial = 2,
        Segmented = 3,
    }

    public class SegmentedBaselineInterpolator : BaselineInterpolator
    {
        public const int MinimumDegree = 0;
        public const int MaximumDegree = 2;

        int degree = 1;

        public int Degree
        {
            get => degree;
            set => degree = ClampDegree(value);
        }

        public List<BaselineSegment> Segments { get; private set; } = new List<BaselineSegment>();

        public SegmentedBaselineInterpolator(DataProcessor processor) : base(processor)
        {
        }

        public static int ClampDegree(int value) => Math.Min(MaximumDegree, Math.Max(MinimumDegree, value));

        public override BaselineInterpolator Copy(DataProcessor processor)
        {
            var interpolator = new SegmentedBaselineInterpolator(processor)
            {
                Degree = this.Degree,
            };

            interpolator.Segments = Segments.Select(segment => segment.Copy()).ToList();
            interpolator.CopyBaselineFrom(this);

            return interpolator;
        }

        public override async Task Interpolate(CancellationToken token, bool replace = true)
        {
            await base.Interpolate(token, replace);

            Segments = CreateSegments(token);
            Baseline = Data.DataPoints.Select(dp => new Energy(EvaluateBaseline(dp.Time))).ToList();
        }

        List<BaselineSegment> CreateSegments(CancellationToken token)
        {
            var segments = new List<BaselineSegment>();

            if (Data.DataPoints.Count == 0) return segments;

            var firstTime = Data.DataPoints.First().Time;
            var lastTime = Data.DataPoints.Last().Time;

            if (Data.Injections.Count == 0)
            {
                segments.Add(FitSegment(BaselineSegmentKind.InitialDelay, -1, firstTime, lastTime));
                return segments;
            }

            var firstInjection = Data.Injections.First();
            segments.Add(FitSegment(
                BaselineSegmentKind.InitialDelay,
                -1,
                firstTime,
                Math.Min(lastTime, firstInjection.IntegrationStartTime)));

            token.ThrowIfCancellationRequested();

            for (int i = 0; i < Data.Injections.Count; i++)
            {
                var injection = Data.Injections[i];
                var nextInjection = i < Data.Injections.Count - 1 ? Data.Injections[i + 1] : null;
                var start = Math.Max(firstTime, injection.IntegrationEndTime);
                var end = nextInjection != null ? nextInjection.IntegrationStartTime : lastTime;

                if (end <= start)
                {
                    var scopeEnd = injection.Time + injection.Delay;
                    end = scopeEnd > start ? scopeEnd : start;
                }

                segments.Add(FitSegment(
                    BaselineSegmentKind.InjectionScope,
                    injection.ID,
                    Math.Min(start, lastTime),
                    Math.Min(Math.Max(end, start), lastTime)));

                token.ThrowIfCancellationRequested();
            }

            return segments;
        }

        BaselineSegment FitSegment(BaselineSegmentKind kind, int injectionID, double start, double end)
        {
            if (end < start) (start, end) = (end, start);

            var points = GetSegmentDataPoints(start, end);
            var center = 0.5 * (start + end);

            if (points.Count == 0)
                return new BaselineSegment(kind, injectionID, start, end, center, new[] { 0.0 });

            var fitDegree = Math.Min(Degree, points.Count - 1);
            if (fitDegree < MinimumDegree)
                return new BaselineSegment(kind, injectionID, start, end, center, new[] { points.Average(dp => (double)dp.Power) });

            var x = points.Select(dp => (double)dp.Time - center).ToArray();
            var y = points.Select(dp => (double)dp.Power).ToArray();
            var coefficients = MathNet.Numerics.Fit.Polynomial(x, y, fitDegree);

            return new BaselineSegment(kind, injectionID, start, end, center, coefficients);
        }

        List<DataPoint> GetSegmentDataPoints(double start, double end)
        {
            var points = GetInterpolatedDataPoints(start, end);

            if (points.Count == 0)
                points = Data.DataPoints.Where(dp => dp.Time >= start && dp.Time <= end).ToList();

            if (points.Count == 0)
            {
                var center = 0.5 * (start + end);
                points = Data.DataPoints
                    .OrderBy(dp => Math.Abs(dp.Time - center))
                    .Take(1)
                    .ToList();
            }

            return points.OrderBy(dp => dp.Time).ToList();
        }

        double EvaluateBaseline(double time)
        {
            if (Segments.Count == 0) return 0;

            for (int i = 0; i < Data.Injections.Count; i++)
            {
                var injection = Data.Injections[i];
                if (time < injection.IntegrationStartTime || time > injection.IntegrationEndTime) continue;

                var left = SegmentBeforeInjection(i);
                var right = SegmentForInjection(injection.ID);

                return BlendSegments(left, right, time, injection.IntegrationStartTime, injection.IntegrationEndTime);
            }

            var containingSegment = Segments.FirstOrDefault(segment => segment.Contains(time));
            if (containingSegment != null) return containingSegment.Evaluate(time);

            var nearestPrevious = Segments.LastOrDefault(segment => segment.StartTime <= time);
            if (nearestPrevious != null) return nearestPrevious.Evaluate(time);

            return Segments.First().Evaluate(time);
        }

        BaselineSegment SegmentBeforeInjection(int injectionIndex)
        {
            if (injectionIndex <= 0)
                return Segments.FirstOrDefault(segment => segment.Kind == BaselineSegmentKind.InitialDelay) ?? Segments.First();

            return SegmentForInjection(Data.Injections[injectionIndex - 1].ID);
        }

        BaselineSegment SegmentForInjection(int injectionID)
        {
            return Segments.FirstOrDefault(segment => segment.Kind == BaselineSegmentKind.InjectionScope && segment.InjectionID == injectionID)
                ?? Segments.Last();
        }

        static double BlendSegments(BaselineSegment left, BaselineSegment right, double time, double start, double end)
        {
            if (left == null && right == null) return 0;
            if (left == null) return right.Evaluate(time);
            if (right == null) return left.Evaluate(time);
            if (end <= start) return right.Evaluate(time);

            var weight = Math.Min(1, Math.Max(0, (time - start) / (end - start)));

            return (1 - weight) * left.Evaluate(time) + weight * right.Evaluate(time);
        }

        public enum BaselineSegmentKind
        {
            InitialDelay,
            InjectionScope,
        }

        public class BaselineSegment
        {
            public BaselineSegmentKind Kind { get; }
            public int InjectionID { get; }
            public double StartTime { get; }
            public double EndTime { get; }
            public double CenterTime { get; }
            public double[] Coefficients { get; }
            public int Degree => Math.Max(0, Coefficients.Length - 1);

            public BaselineSegment(BaselineSegmentKind kind, int injectionID, double startTime, double endTime, double centerTime, double[] coefficients)
            {
                Kind = kind;
                InjectionID = injectionID;
                StartTime = startTime;
                EndTime = endTime;
                CenterTime = centerTime;
                Coefficients = coefficients ?? new[] { 0.0 };
            }

            public bool Contains(double time) => time >= StartTime && time <= EndTime;

            public double Evaluate(double time)
            {
                var x = time - CenterTime;
                var value = 0.0;
                var power = 1.0;

                foreach (var coefficient in Coefficients)
                {
                    value += coefficient * power;
                    power *= x;
                }

                return value;
            }

            public BaselineSegment Copy()
            {
                return new BaselineSegment(Kind, InjectionID, StartTime, EndTime, CenterTime, Coefficients.ToArray());
            }
        }
    }

    public class SplineInterpolator : BaselineInterpolator
    {
        public const int MinimumPointsPerInjection = 1;
        public const int MaximumPointsPerInjection = 8;
        const double SmoothSplinePenalty = 1;
        const double DefaultSplinePointWeight = 200.0;
        const double LockedSplinePointWeight = 1000.0;
        static int defaultPointsPerInjection = 2;
        static SplinePointDensity defaultPointDensity = SplinePointDensity.Balanced;

        SplineInterpolatorAlgorithm algorithm = SplineInterpolatorAlgorithm.Smooth;
        int pointsPerInjection = DefaultPointsPerInjection;

        public static SplineInterpolatorAlgorithm PolynomialToSplineConversionTargetAlgorithm { get; set; } = SplineInterpolatorAlgorithm.Rigid;
        public static double BalancedSplinePointIntegrationFractionThreshold { get; set; } = 0.33;
        public static int DenseSplinePointsAtZeroIntegration { get; set; } = 5;
        public static double LockedSplinePointPlacementMarginFraction { get; set; } = 1.0 / 3.0;
        public static SplineHandleMode DefaultHandleMode { get; set; } = SplineHandleMode.Mean;
        public static bool DefaultAllowPointTimeDragging { get; set; } = false;

        public static SplinePointDensity DefaultPointDensity
        {
            get => defaultPointDensity;
            set
            {
                defaultPointDensity = value;
                DefaultPointsPerInjection = PointsPerInjectionForDensity(value);
            }
        }

        public static int DefaultPointsPerInjection
        {
            get => defaultPointsPerInjection;
            set => defaultPointsPerInjection = ClampPointsPerInjection(value);
        }

        public int PointsPerInjection
        {
            get => pointsPerInjection;
            set => pointsPerInjection = ClampPointsPerInjection(value);
        }

        public SplineInterpolatorAlgorithm Algorithm
        {
            get => algorithm;
            set
            {
                if (value == SplineInterpolatorAlgorithm.Handles)
                {
                    algorithm = SplineInterpolatorAlgorithm.Smooth;
                    ShowHandles = true;
                }
                else algorithm = value;
            }
        }

        public SplinePointDensity PointDensity { get; set; } = SplinePointDensity.Balanced;
        public bool ShowHandles { get; set; } = false;
        public bool AllowPointTimeDragging { get; set; } = false;
        public SplineHandleMode HandleMode { get; set; } = SplineHandleMode.Mean;

        public List<SplinePoint> SplinePoints { get; private set; } = new List<SplinePoint>();

        Spline SplineFunction;

        public SplineInterpolator(DataProcessor processor) : base(processor)
        {
            PointDensity = DefaultPointDensity;
            PointsPerInjection = DefaultPointsPerInjection;
            HandleMode = DefaultHandleMode;
            AllowPointTimeDragging = DefaultAllowPointTimeDragging;
        }

        static int ClampPointsPerInjection(int value) => Math.Min(MaximumPointsPerInjection, Math.Max(MinimumPointsPerInjection, value));

        public void ApplyPointDensity()
        {
            PointsPerInjection = PointsPerInjectionForDensity(PointDensity);
        }

        public static int PointsPerInjectionForDensity(SplinePointDensity density)
        {
            return ClampPointsPerInjection((int)density + 1);
        }

        public override BaselineInterpolator Copy(DataProcessor processor)
        {
            var interpolator = new SplineInterpolator(processor)
            {
                PointsPerInjection = this.PointsPerInjection,
                Algorithm = this.Algorithm,
                PointDensity = this.PointDensity,
                ShowHandles = this.ShowHandles,
                AllowPointTimeDragging = this.AllowPointTimeDragging,
                HandleMode = this.HandleMode,
            };

            interpolator.SetSplinePoints(SplinePoints.Select(sp => sp.Copy()).ToList());
            interpolator.CopyBaselineFrom(this);
            interpolator.RebuildSplineFunctionFromCurrentSplinePoints();

            return interpolator;
        }

        public List<SplinePoint> GetInitialPoints(int pointperinjection = 1)
        {
            var points = new List<SplinePoint>();

            //First points
            var segmmentL = (Data.InitialDelay - 5) / 4;
            points.Add(new SplinePoint(segmmentL, GetDataRangeMean(0, 2 * segmmentL), 0, SplineSlope(segmmentL, 0, 2 * segmmentL)));
            points.Add(new SplinePoint(3 * segmmentL, GetDataRangeMean(2 * segmmentL, 4 * segmmentL), points.Count, SplineSlope(3 * segmmentL, 2 * segmmentL, 4 * segmmentL)));

            foreach (var inj in Data.Injections)
            {
                var start = inj.Time;
                var end = inj.Time + inj.Delay - 5;

                if (Processor.DiscardIntegratedPoints)
                {
                    if (start < inj.IntegrationEndTime) start = inj.IntegrationEndTime;
                }

                if (end <= start) start = (float)Math.Max(inj.Time, end - Data.TimeStep);

                var baselineLength = Math.Max(end - start, Data.TimeStep);
                var pointCount = GetAutomaticSplinePointCount(inj.Delay, inj.IntegrationLength);
                var length = baselineLength / pointCount;

                for (int j = 0; j < pointCount; j++)
                {
                    var s = start + j * length;
                    var e = s + length;
                    var time = (s + e) / 2;

                    double slope = SplineSlope(time, s, e);

                    points.Add(new SplinePoint(time, GetDataRangeMean(s, e), points.Count, slope));
                }
            }

            return points;
        }

        int GetAutomaticSplinePointCount(double injectionDelay, double integratedLength)
        {
            if (PointDensity == SplinePointDensity.Sparse) return 1;

            var injectionScope = Math.Max(Data.TimeStep, injectionDelay);
            var fractionIntegrated = Math.Min(1, Math.Max(0, integratedLength / injectionScope));

            if (PointDensity == SplinePointDensity.Balanced)
            {
                var threshold = Math.Min(1, Math.Max(0, BalancedSplinePointIntegrationFractionThreshold));
                return fractionIntegrated < threshold ? 2 : 1;
            }

            var densePointCount = Math.Floor((1 - fractionIntegrated) * Math.Max(1, DenseSplinePointsAtZeroIntegration));
            return ClampPointsPerInjection((int)Math.Max(1, densePointCount));
        }

        double SplineSlope(double time, double s = 0, double e = 1) => DataPoint.Slope(GetInterpolatedDataPoints(s, e));

        double GetDataRangeMean(double start, double end)
        {
            List<DataPoint> points = GetInterpolatedDataPoints(start, end);

            if (points.Count < 1) points.Add(Data.DataPoints.Last(dp => dp.Time < end));

            switch (HandleMode)
            {
                default:
                case SplineHandleMode.Mean: return DataPoint.Mean(points); 
                case SplineHandleMode.Median: return DataPoint.Median(points); 
                case SplineHandleMode.MinVolatility: return DataPoint.VolatilityWeightedAverage(points); 
                
            }
        }

        public override async Task Interpolate(CancellationToken token, bool replace = true)
        {
            await base.Interpolate(token, replace);

            List<SplinePoint> splinePoints;

            if (SplinePoints.Count == 0 || (replace && !IsLocked)) splinePoints = MergeLockedSplinePoints(GetInitialPoints(PointsPerInjection));
            else splinePoints = SplinePoints;

            UpdateAutomaticSplineSlopes(splinePoints);
            var spline = CreateSpline(splinePoints);

            Baseline = Data.DataPoints.Select(dp => spline.Evaluate(dp.Time)).ToList();
            SplinePoints = splinePoints;
            SplineFunction = spline;
        }

        public void RefreshBaselineFromCurrentSplinePoints()
        {
            if (SplinePoints.Count == 0) return;

            UpdateAutomaticSplineSlopes(SplinePoints);
            var spline = CreateSpline(SplinePoints);
            Baseline = Data.DataPoints.Select(dp => spline.Evaluate(dp.Time)).ToList();
            SplineFunction = spline;
        }

        void RebuildSplineFunctionFromCurrentSplinePoints()
        {
            if (SplinePoints.Count == 0) return;

            SplineFunction = CreateSpline(SplinePoints);
        }

        Spline CreateSpline(List<SplinePoint> splinePoints)
        {
            var sortedPoints = splinePoints.OrderBy(sp => sp.Time).ToList();
            var x = sortedPoints.Select(sp => sp.Time);
            var y = sortedPoints.Select(sp => (double)sp.Power);

            switch (Algorithm)
            {
                default:
                case SplineInterpolatorAlgorithm.Linear: return new Spline(LinearSpline.Interpolate(x, y));
                case SplineInterpolatorAlgorithm.Rigid: return new Spline(CubicSpline.InterpolatePchip(x, y));
                case SplineInterpolatorAlgorithm.Smooth: return new Spline(CubicSpline.InterpolateHermite(x, y, sortedPoints.Select(s => s.Slope)), sortedPoints);
            }
        }

        public void RemoveSplinePoint(int id)
        {
            SplinePoints.RemoveAt(id);

            SortAndRenumberSplinePoints();

            _ = Processor.ProcessData(false);
        }

        public void MoveSplinePoint(int id, double time, double power)
        {
            if (id < 0 || id >= SplinePoints.Count) return;

            var point = SplinePoints[id];
            point.Time = time;
            point.Power = power;
            point.Lock();
            SortAndRenumberSplinePoints();
            RefreshBaselineFromCurrentSplinePoints();
        }

        public void SetSplinePointSlope(int id, double slope)
        {
            if (id < 0 || id >= SplinePoints.Count) return;

            var point = SplinePoints[id];
            point.Slope = slope;
            point.Lock();
            point.LockSlope();
            RefreshBaselineFromCurrentSplinePoints();
        }

        public void InsertSplinePoint(double cursorpos, bool usedatavalue = false)
        {
            if (Baseline.Count == 0) return;

            double pointValue;
            if (usedatavalue) pointValue = cursorpos > Data.DataPoints.Last().Time ? Data.DataPoints.Last().Power : Data.DataPoints.First(dp => dp.Time > cursorpos).Power;
            else  pointValue = SplineFunction.Evaluate((float)cursorpos);

            var newsp = new SplinePoint(cursorpos, pointValue, 0) { Locked = true, UserDefined = true };

            // Insert and order by time
            SplinePoints.Add(newsp);
            SortAndRenumberSplinePoints();

            _ = Processor.ProcessData(false);
        }

        List<SplinePoint> MergeLockedSplinePoints(List<SplinePoint> generatedPoints)
        {
            var placementMargin = GetLockedSplinePointPlacementMargin(generatedPoints);

            foreach (var lockedPoint in SplinePoints.Where(sp => sp.Locked))
            {
                var hasCloseGeneratedPoint = generatedPoints.Any(gp => !gp.Locked && IsWithinPlacementMargin(gp, lockedPoint, placementMargin));

                if (hasCloseGeneratedPoint)
                {
                    generatedPoints.RemoveAll(gp => !gp.Locked && IsWithinPlacementMargin(gp, lockedPoint, placementMargin));
                    generatedPoints.Add(lockedPoint);
                }
                else if (!lockedPoint.UserDefined && lockedPoint.ID >= 0 && lockedPoint.ID < generatedPoints.Count)
                {
                    generatedPoints[lockedPoint.ID] = lockedPoint;
                }
                else
                {
                    generatedPoints.Add(lockedPoint);
                }
            }

            return SortAndRenumberSplinePoints(generatedPoints);
        }

        double GetLockedSplinePointPlacementMargin(List<SplinePoint> generatedPoints)
        {
            var expectedPointDistance = GetExpectedGeneratedSplinePointDistance(generatedPoints);
            var margin = Math.Max(0, LockedSplinePointPlacementMarginFraction) * expectedPointDistance;

            return double.IsNaN(margin) || double.IsInfinity(margin) ? 0 : margin;
        }

        double GetExpectedGeneratedSplinePointDistance(List<SplinePoint> generatedPoints)
        {
            var spacings = generatedPoints
                .Select(sp => sp.Time)
                .OrderBy(time => time)
                .Zip(generatedPoints.Select(sp => sp.Time).OrderBy(time => time).Skip(1), (left, right) => right - left)
                .Where(spacing => spacing > double.Epsilon)
                .OrderBy(spacing => spacing)
                .ToList();

            if (spacings.Count == 0) return Math.Max(Data.TimeStep, double.Epsilon);

            return spacings[spacings.Count / 2];
        }

        static bool IsWithinPlacementMargin(SplinePoint generatedPoint, SplinePoint lockedPoint, double placementMargin)
        {
            return Math.Abs(generatedPoint.Time - lockedPoint.Time) <= placementMargin;
        }

        void UpdateAutomaticSplineSlopes(List<SplinePoint> points)
        {
            if (Algorithm != SplineInterpolatorAlgorithm.Smooth) return;
            if (points.Count < 2) return;

            var guideSpline = CreatePenalizedSmoothingSpline(points);
            foreach (var point in points.Where(point => !point.SlopeLocked))
            {
                point.Slope = guideSpline.Slope(point.Time);
            }

            ApplyLinearSegmentSlopes(points);
        }

        void ApplyLinearSegmentSlopes(List<SplinePoint> points)
        {
            var sortedPoints = points.OrderBy(sp => sp.Time).ToList();

            for (int i = 0; i < sortedPoints.Count - 1; i++)
            {
                if (!IsLinearSegment(sortedPoints, i)) continue;

                var slope = SplinePointSegmentSlope(sortedPoints[i], sortedPoints[i + 1]);
                sortedPoints[i].Slope = slope;
                sortedPoints[i + 1].Slope = slope;
            }
        }

        static bool IsLinearSegment(List<SplinePoint> points, int index) => points[index].Linear && points[index + 1].Linear;

        static double SplinePointSegmentSlope(SplinePoint left, SplinePoint right)
        {
            var dx = right.Time - left.Time;

            return Math.Abs(dx) > double.Epsilon ? (right.Power - left.Power) / dx : 0;
        }

        Spline CreatePenalizedSmoothingSpline(List<SplinePoint> points)
        {
            if (points.Count < 3)
            {
                var x = points.Select(sp => sp.Time);
                var y = points.Select(sp => (double)sp.Power);
                if (points.Count < 2) return new Spline(points.Count == 0 ? 0 : points[0].Power);
                return new Spline(CubicSpline.InterpolatePchip(x, y));
            }

            var sortedPoints = points.OrderBy(sp => sp.Time).ToList();
            var xValues = sortedPoints.Select(sp => sp.Time).ToArray();
            var yValues = sortedPoints.Select(sp => (double)sp.Power).ToArray();
            var fitMatrix = DenseMatrix.Create(sortedPoints.Count, sortedPoints.Count, 0);
            var fitTarget = DenseVector.Create(sortedPoints.Count, i =>
            {
                var weight = SplinePointFitWeight(sortedPoints[i]);
                return weight * yValues[i];
            });

            for (int i = 0; i < sortedPoints.Count; i++)
            {
                fitMatrix[i, i] = SplinePointFitWeight(sortedPoints[i]);
            }

            // Penalize curvature by adding lambda * D'D for the second-difference operator.
            for (int i = 1; i < sortedPoints.Count - 1; i++)
            {
                fitMatrix[i - 1, i - 1] += SmoothSplinePenalty;
                fitMatrix[i - 1, i] -= 2 * SmoothSplinePenalty;
                fitMatrix[i - 1, i + 1] += SmoothSplinePenalty;
                fitMatrix[i, i - 1] -= 2 * SmoothSplinePenalty;
                fitMatrix[i, i] += 4 * SmoothSplinePenalty;
                fitMatrix[i, i + 1] -= 2 * SmoothSplinePenalty;
                fitMatrix[i + 1, i - 1] += SmoothSplinePenalty;
                fitMatrix[i + 1, i] -= 2 * SmoothSplinePenalty;
                fitMatrix[i + 1, i + 1] += SmoothSplinePenalty;
            }

            var smoothedValues = fitMatrix.Solve(fitTarget).ToArray();

            return new Spline(CubicSpline.InterpolateNaturalSorted(xValues, smoothedValues));
        }

        double SplinePointFitWeight(SplinePoint point) => point.Locked || point.UserDefined ? LockedSplinePointWeight : DefaultSplinePointWeight;

        public void SetSplinePoints(List<SplinePoint> points)
        {
            SplinePoints = SortAndRenumberSplinePoints(points);
        }

        List<SplinePoint> SortAndRenumberSplinePoints(List<SplinePoint> points)
        {
            var sorted = points.OrderBy(sp => sp.Time).ToList();
            sorted.ForEach(sp => sp.ID = sorted.IndexOf(sp));

            return sorted;
        }

        void SortAndRenumberSplinePoints()
        {
            SplinePoints = SortAndRenumberSplinePoints(SplinePoints);
        }

        public enum SplineInterpolatorAlgorithm
        {
            Smooth = 0,
            Handles = 1,
            Rigid = 2,
            Linear = 3,
        }

        public enum SplinePointDensity
        {
            Sparse = 0,
            Balanced = 1,
            Dense = 2,
        }

        public enum SplineHandleMode
        {
            Mean,
            Median,
            MinVolatility
        }

        private class Spline
        {
            CubicSpline CubicSplineFunction = null;
            LinearSpline LinearSplineFunction = null;
            double? ConstantFunction = null;
            double[] SegmentTimes = null;
            double[] SegmentPowers = null;
            bool[] LinearSegments = null;

            public Spline(CubicSpline spline)
            {
                CubicSplineFunction = spline;
            }

            public Spline(CubicSpline spline, List<SplinePoint> points) : this(spline)
            {
                SegmentTimes = points.Select(sp => sp.Time).ToArray();
                SegmentPowers = points.Select(sp => sp.Power).ToArray();
                LinearSegments = points.Zip(points.Skip(1), (left, right) => left.Linear && right.Linear).ToArray();
            }

            public Spline(LinearSpline spline)
            {
                LinearSplineFunction = spline;
            }

            public Spline(double value)
            {
                ConstantFunction = value;
            }

            public Energy Evaluate(float time)
            {
                if (TryGetLinearSegment(time, out int index)) return new(EvaluateLinearSegment(index, time));
                if (CubicSplineFunction != null) return new(CubicSplineFunction.Interpolate(time));
                if (LinearSplineFunction != null) return new(LinearSplineFunction.Interpolate(time));
                if (ConstantFunction.HasValue) return new(ConstantFunction.Value);

                return new(0.0);
            }

            public double Slope(double time)
            {
                if (TryGetLinearSegment(time, out int index)) return LinearSegmentSlope(index);
                if (CubicSplineFunction != null) return CubicSplineFunction.Differentiate(time);
                if (LinearSplineFunction != null) return LinearSplineFunction.Differentiate(time);
                if (ConstantFunction.HasValue) return 0;

                else return 0;
            }

            bool TryGetLinearSegment(double time, out int index)
            {
                index = -1;
                if (LinearSegments == null) return false;

                for (int i = 0; i < LinearSegments.Length; i++)
                {
                    if (!LinearSegments[i]) continue;
                    if (time < SegmentTimes[i] || time > SegmentTimes[i + 1]) continue;

                    index = i;
                    return true;
                }

                return false;
            }

            double EvaluateLinearSegment(int index, double time)
            {
                return SegmentPowers[index] + LinearSegmentSlope(index) * (time - SegmentTimes[index]);
            }

            double LinearSegmentSlope(int index)
            {
                var dx = SegmentTimes[index + 1] - SegmentTimes[index];

                return Math.Abs(dx) > double.Epsilon ? (SegmentPowers[index + 1] - SegmentPowers[index]) / dx : 0;
            }
        }

        public class SplinePoint
        {
            /// <summary>
            /// Mouse over feature relevant ID
            /// </summary>
            public int ID;
            public double Time;
            public double Power;
            public double Slope;
            public bool Locked;
            public bool SlopeLocked;
            public bool Linear;
            public bool UserDefined;

            public SplinePoint(double time, double power, int id, double slope = 0)
            {
                Time = time;
                Power = power;
                ID = id;
                Slope = slope;
            }

            public void Lock() => Locked = true;
            public void Unlock() => Locked = false;
            public void LockSlope() => SlopeLocked = true;
            public void UnlockSlope() => SlopeLocked = false;

            public SplinePoint Copy()
            {
                return new SplinePoint(Time, Power, ID, Slope)
                {
                    Locked = Locked,
                    SlopeLocked = SlopeLocked,
                    Linear = Linear,
                    UserDefined = UserDefined
                };
            }
        }
    }

    public class AssymetricLeastSquaresInterpolator : BaselineInterpolator
    {
        static int alg_niter = 10;
        double Lambda = 1000;
        double p = 0.96;
        double[] datapoints => Data.DataPoints.Select(dp => (double)dp.Power).ToArray();

        public AssymetricLeastSquaresInterpolator(DataProcessor processor) : base(processor)
        {

        }

        public override async Task Interpolate(CancellationToken token, bool replace = true)
        {
            var y = SparseVector.OfEnumerable(datapoints);
            var L = y.Count; //len(y)
            var D = Diff(new DiagonalMatrix(L, L, 1)); //sparse.csc_matrix(np.diff(np.eye(L), 2))
            var w = new SparseVector(L).Add(1);

            var z = new SparseVector(L);

            for (int i = 0; i < alg_niter; i++)
            {
                Console.WriteLine("ASL iter: " + i);
                var W = SparseMatrix.CreateDiagonal(L, L, (o => w[o]));
                var a = SparseMatrix.OfMatrix(D * D.Transpose());
                var Z = W + Lambda * a;
                var mul = w.PointwiseMultiply(y);

                var monitor = new Iterator<double>(
                    new IterationCountStopCriterion<double>(2000),
                    new ResidualStopCriterion<double>(0.001));

                var solver = new MathNet.Numerics.LinearAlgebra.Double.Solvers.TFQMR();

                var nZ = Z.SolveIterative(mul, solver, monitor);//Solve(mul);
                z = SparseVector.OfEnumerable(nZ.Storage.ToArray());
                w = Select(z.ToList(), y.ToList());

                token.ThrowIfCancellationRequested();
            }

            //Baseline = z.Select(o => o).ToList();

            await base.Interpolate(token, replace);
        }

        SparseMatrix Diff(DiagonalMatrix m)
        {
            var dense = new SparseMatrix(m.RowCount, m.RowCount - 2);

            var rows = m.EnumerateRows().ToList();

            for (int i = 0; i < rows.Count(); i++)
            {
                var row = rows[i];
                double[] newrow = new double[row.Count() - 2];

                for (int j = 0; j < row.Count() - 2; j++)
                {
                    if (i == j) newrow[j] = 1;
                    else if (i == j + 1) newrow[j] = -2;
                    else if (i == j + 2) newrow[j] = 1;

                    //newrow[j] = (row[j + 2] - row[j + 1]) - (row[j + 1] - row[j]);
                }

                dense.SetRow(i, newrow);
            }
            return dense;
        }

        DenseVector Select(List<double> z, List<double> y)
        {
            var w = new DenseVector(z.Count);

            for (int i = 0; i < z.Count(); i++)
            {
                if (z[i] < y[i]) w[i] = p;
                else w[i] = (1 - p);
            }

            return w;
        }
    }

    public class PolynomialLeastSquaresInterpolator : BaselineInterpolator
    {
        double[] fit;

        public int Degree { get; set; } = 12;
        public double ZLimit { get; set; } = 2;

        public PolynomialLeastSquaresInterpolator(DataProcessor processor) : base(processor)
        {
        }

        public override BaselineInterpolator Copy(DataProcessor processor)
        {
            var interpolator = new PolynomialLeastSquaresInterpolator(processor)
            {
                Degree = this.Degree,
                ZLimit = this.ZLimit,
            };

            interpolator.CopyBaselineFrom(this);

            return interpolator;
        }

        public override async Task Interpolate(CancellationToken token, bool replace = true)
        {
            await base.Interpolate(token, replace);

            //Arrays of time and power datapoints
            var x = Data.DataPoints.Select(dp => (double)dp.Time).ToArray();
            var y = Data.DataPoints.Select(dp => (double)dp.Power).ToArray();

            if (Processor.DiscardIntegratedPoints)
            {
                foreach (var inj in Data.Injections)
                {
                    y = y.Where((v, idx) => x[idx] < inj.IntegrationStartTime || x[idx] > inj.IntegrationEndTime).ToArray();
                    x = x.Where((v, idx) => v < inj.IntegrationStartTime || v > inj.IntegrationEndTime).ToArray();
                }
            }

            var fit = MathNet.Numerics.Fit.Polynomial(x, y, Degree);
            var line = LineFromFit(fit, x);

            var previousRSoS = 1.0;

            var r = ResidualSumOfSquares(line, y);

            while (r > double.Epsilon && Math.Abs(previousRSoS - r) > double.Epsilon)
            {
                var residuals = Residuals(line, y);

                var s = Math.Sqrt(r / (line.Length - 1));
                var avg = residuals.Average();

                var Zscores = residuals.Select(v => Math.Abs(v - avg) / s).ToArray();
                previousRSoS = r;

                //x = x.Where((v, idx) => IdxToTime(idx) < Data.InitialDelay || IdxToTime(idx) > Data.Injections.Last().IntegrationEndTime || Zscores[idx] < ZLimit).ToArray();
                //y = y.Where((v, idx) => IdxToTime(idx) < Data.InitialDelay || IdxToTime(idx) > Data.Injections.Last().IntegrationEndTime || Zscores[idx] < ZLimit).ToArray();

                y = y.Where((v, idx) => Zscores[idx] < ZLimit).ToArray();
                x = x.Where((v, idx) => Zscores[idx] < ZLimit).ToArray();

                fit = MathNet.Numerics.Fit.Polynomial(x, y, Degree);
                line = LineFromFit(fit, x);

                r = ResidualSumOfSquares(line, y);

                token.ThrowIfCancellationRequested();
            }

            this.fit = fit;

            Baseline = Evaluate().Select(e => new Energy(e)).ToList();
        }

        double[] Residuals(double[] fit, double[] dat)
        {
            double[] res = new double[fit.Length];

            for (int i = 0; i < fit.Length; i++)
            {
                var v1 = fit[i];
                var v2 = dat[i];

                res[i] = v1 - v2;
            }

            return res;
        }

        double ResidualSumOfSquares(double[] fit, double[] dat)
        {
            var sum = 0.0;
            var res = Residuals(fit, dat);

            foreach (var r in res) sum += r * r;

            return sum;
        }

        double[] LineFromFit(double[] fit, double[] x)
        {
            int order = fit.Length - 1;

            double[] line = new double[x.Length];

            for (int i = 0; i < x.Length; i++)
            {
                var xval = x[i];
                var yval = 0.0;

                for (int e = 0; e <= order; e++)
                {
                    yval += fit[e] * Math.Pow(xval, e);
                }

                line[i] = yval;
            }

            return line;
        }

        double IdxToTime(int idx)
        {
            return Data.DataPoints[idx].Time;
        }

        double[] Evaluate()
        {
            double[] eval = new double[Data.DataPoints.Count];
            var data = Data.DataPoints.Select(dp => dp.Time).ToArray();

            for (int i = 0; i < eval.Length; i++)
            {
                eval[i] = MathNet.Numerics.Polynomial.Evaluate(data[i], this.fit);
            }

            return eval;
        }
    }
}
