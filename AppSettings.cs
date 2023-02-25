using System;
namespace AnalysisITC
{
    public static class AppSettings
    {
        public static double ReferenceTemperature { get; set; } = 25.0;

        public static PeakFitAlgorithm PeakFitAlgorithm { get; set; } = PeakFitAlgorithm.Exponential;
        public static double MinimumTemperatureSpanForFitting { get; internal set; } = 2;
        public static bool IncludeConcentrationErrorsInBootstrap { get; set; } = true;
    }

    public enum PeakFitAlgorithm
    {
        Exponential,
        Default,
    }
}
