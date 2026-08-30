using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Platform;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Analysis
{
    public static class ResultAnalysisController
    {
        public static TerminationFlag TerminateAnalysisFlag { get; private set; } = new TerminationFlag();

        public static event EventHandler<TerminationFlag> AnalysisStarted;
        public static event EventHandler<Tuple<int, int, float, string>> IterationFinished;
        public static event EventHandler<Tuple<int, TimeSpan>> AnalysisFinished;

        public static int CalculationIterations { get; set; } = 1000;

        public static void ReportCalculationStarted() => PlatformServices.MainThreadDispatcher.Invoke(() =>
        {
            AnalysisStarted?.Invoke(null, TerminateAnalysisFlag);
        });

        public static void ReportCalculationProgress(int iteration, int totaliterations = 0, string description = null) => PlatformServices.MainThreadDispatcher.Invoke(() =>
        {
            var totiter = totaliterations > 0 ? totaliterations : CalculationIterations;

            IterationFinished?.Invoke(null, new Tuple<int, int, float, string>(iteration, totiter, iteration / (float)totiter, description));
        });

        public static void ReportAnalysisFinished(object analysis, int iterations, TimeSpan time) => PlatformServices.MainThreadDispatcher.Invoke(() =>
        {
            AnalysisFinished?.Invoke(analysis, new Tuple<int, TimeSpan>(iterations, time));
        });
    }

    public class AdvancedAnalysis
    {
        internal static Random Rand { get; } = new Random();

        public AnalysisResult Data { get; private set; }
        public List<Tuple<double, FloatWithError>> DataPoints;
        public FitWithError Fit { get; set; }

        public int CompletedIterations { get; internal set; } = 0;
        public DateTime? CompletedAtUtc { get; internal set; }
        public ErrorEstimationMethod? CompletedErrorEstimationMethod { get; internal set; }

        public AdvancedAnalysis(AnalysisResult result)
        {
            Data = result;
        }

        public virtual async void PerformAnalysis()
        {
            await PerformAnalysisAsync();
        }

        public virtual async Task<bool> PerformAnalysisAsync()
        {
            ResultAnalysisController.TerminateAnalysisFlag.Lower();
            ResultAnalysisController.ReportCalculationStarted();

            var start = DateTime.UtcNow;
            var previous = CaptureCommittedState();
            var previousIterations = CompletedIterations;
            var previousCompletedAt = CompletedAtUtc;
            var previousErrorMethod = CompletedErrorEstimationMethod;
            var succeeded = false;

            try
            {
                await Task.Run(() => Calculate());
                succeeded = ResultAnalysisController.TerminateAnalysisFlag.Down;
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }

            if (succeeded)
            {
                CompletedAtUtc = DateTime.UtcNow;
                // Advanced analyses always propagate the parameter errors already
                // attached to the ITC result by random sampling.  The ITC solver's
                // error-estimation method is not an advanced-analysis method.
                CompletedErrorEstimationMethod = null;
                CommitRunState();
                Data.MarkModified();
            }
            else
            {
                RestoreCommittedState(previous);
                CompletedIterations = previousIterations;
                CompletedAtUtc = previousCompletedAt;
                CompletedErrorEstimationMethod = previousErrorMethod;
            }

            ResultAnalysisController.ReportAnalysisFinished(this, CompletedIterations, DateTime.UtcNow - start);
            return succeeded;
        }

        protected virtual void Calculate()
        {
            
        }

        protected virtual object CaptureCommittedState() => null;

        protected virtual void RestoreCommittedState(object state)
        {
        }

        protected virtual void CommitRunState()
        {
        }

        internal void RestoreRunMetadata(int completedIterations, DateTime? completedAtUtc, ErrorEstimationMethod? errorMethod)
        {
            CompletedIterations = completedIterations;
            CompletedAtUtc = completedAtUtc?.ToUniversalTime();
            // Older FTXTC files persisted "none" for advanced analyses that had
            // no separate error method.  Keep that historical representation
            // readable while exposing the current null/no-method semantics.
            CompletedErrorEstimationMethod = errorMethod == ErrorEstimationMethod.None ? null : errorMethod;
        }

        internal List<(double,double)> GetErrorData(List<(double, FloatWithError)> dps)
        {
            // Leave-one-out is an ITC model-fitting error estimator.  Advanced
            // analyses consume the resulting parameter uncertainties and always
            // propagate them by sampling every input parameter.
            return dps.Select(dp => (dp.Item1, dp.Item2.Sample(Rand))).ToList();
        }
    }
}
