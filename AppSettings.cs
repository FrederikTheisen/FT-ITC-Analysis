using System;
namespace AnalysisITC
{
    public static class AppSettings
    {
        public static double ReferenceTemperature { get; set; } = 25.0;

        public static PeakFitAlgorithm PeakFitAlgorithm { get; set; } = PeakFitAlgorithm.Exponential;
    }

    public enum PeakFitAlgorithm
    {
        Exponential,
        Default,
    }
}
