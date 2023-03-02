using System;
namespace AnalysisITC
{
    public static class AppSettings
    {
        //General
        public static double ReferenceTemperature { get; set; } = 25.0;
        public static EnergyUnit EnergyUnit { get; set; } = EnergyUnit.KiloJoule;

        //Processing
        public static PeakFitAlgorithm PeakFitAlgorithm { get; set; } = PeakFitAlgorithm.Exponential;

        //Fitting
        public static ErrorEstimationMethod DefaultErrorEstimationMethod { get; set; } = ErrorEstimationMethod.BootstrapResiduals;
        public static int DefaultBootstrapIterations { get; set; } = 100;
        public static double MinimumTemperatureSpanForFitting { get; internal set; } = 2;
        public static bool IncludeConcentrationErrorsInBootstrap { get; set; } = true;
        public static double OptimizerTolerance { get; set; } = double.Epsilon;

        //Final figure
        public static double[] FinalFigureDimensions { get; set; } = new double[2] { 6.0, 10.0 };

        public static void Save()
        {

        }

        public static void Load()
        {

        }
    }

    public enum PeakFitAlgorithm
    {
        Exponential,
        Default,
    }
}
