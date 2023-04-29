using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AppKit;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
    public static class ResultAnalysisController
    {
        public static TerminationFlag TerminateAnalysisFlag { get; private set; } = new TerminationFlag();

        public static event EventHandler<TerminationFlag> AnalysisStarted;
        public static event EventHandler<Tuple<int, int, float>> IterationFinished;
        public static event EventHandler<Tuple<int, TimeSpan>> AnalysisFinished;

        public static int CalculationIterations { get; set; } = 1000;

        public static void ReportCalculationStarted() => NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            AnalysisStarted?.Invoke(null, TerminateAnalysisFlag);
        });

        public static void ReportCalculationProgress(int iteration, int totaliterations = 0) => NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            var totiter = totaliterations > 0 ? totaliterations : CalculationIterations;

            IterationFinished?.Invoke(null, new Tuple<int, int, float>(iteration, totiter, iteration / (float)totiter));
        });

        public static void ReportAnalysisFinished(object analysis, int iterations, TimeSpan time) => NSApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            AnalysisFinished?.Invoke(analysis, new Tuple<int, TimeSpan>(iterations, time));
        });
    }

    public class ResultAnalysis
    {
        internal static Random Rand { get; } = new Random();

        public AnalysisResult Data { get; private set; }
        public List<Tuple<double, FloatWithError>> DataPoints;
        public FitWithError Fit { get; set; }

        public int CompletedIterations { get; internal set; } = 0;

        public ResultAnalysis(AnalysisResult result)
        {
            Data = result;
        }

        public virtual async void PerformAnalysis()
        {
            ResultAnalysisController.TerminateAnalysisFlag.Lower();
            ResultAnalysisController.ReportCalculationStarted();

            DateTime start = DateTime.Now;

            await Task.Run(() => Calculate());

            ResultAnalysisController.ReportAnalysisFinished(this, CompletedIterations, DateTime.Now - start);
        }

        protected virtual void Calculate()
        {
            
        }
    }
}

