using System;
using AnalysisITC.AppClasses.Analysis2;
using Foundation;
using System.Linq;
using AnalysisITC.GUI;

namespace AnalysisITC
{
    public static class AppSettings
    {
        public static event EventHandler SettingsDidUpdate;

        static NSUserDefaults Storage => NSUserDefaults.StandardUserDefaults;

        //General
        public static double ReferenceTemperature { get; set; } = 25.0;
        public static EnergyUnit EnergyUnit { get; set; } = EnergyUnit.KiloJoule;
        public static ColorSchemes ColorScheme { get; set; } = ColorSchemes.Default;
        public static ColorShcemeGradientMode ColorShcemeGradientMode { get; set; } = ColorShcemeGradientMode.Smooth;

        //Processing
        public static PeakFitAlgorithm PeakFitAlgorithm { get; set; } = PeakFitAlgorithm.Exponential;

        //Fitting
        public static ErrorEstimationMethod DefaultErrorEstimationMethod { get; set; } = ErrorEstimationMethod.BootstrapResiduals;
        public static int DefaultBootstrapIterations { get; set; } = 100;
        public static double MinimumTemperatureSpanForFitting { get; internal set; } = 2;
        public static bool IncludeConcentrationErrorsInBootstrap { get; set; } = true;
        public static double ConcentrationAutoVariance { get; set; } = 0.05;
        
        public static double OptimizerTolerance { get; set; } = double.Epsilon;
        public static int MaximumOptimizerIterations { get; set; } = 300000;

        //Final figure
        public static double[] FinalFigureDimensions { get; set; } = new double[2] { 6.0, 10.0 };

        //Export
        public static bool UnifyTimeAxisForExport { get; set; } = true;
        public static bool ExportFitPointsWithPeaks { get; set; } = true;
        public static Exporter.ExportDataSelection ExportSelectionMode { get; set; } = Exporter.ExportDataSelection.IncludedData;

        public static bool IsConcentrationAutoVarianceEnabled = ConcentrationAutoVariance > 0.001;

        public static void Save()
        {
            ApplyDefaultSettings();

            Storage.SetDouble(ReferenceTemperature, "ReferenceTemperature");
            Storage.SetInt((int)EnergyUnit, "EnergyUnit");
            Storage.SetInt((int)DefaultErrorEstimationMethod, "DefaultErrorEstimationMethod");
            Storage.SetInt(DefaultBootstrapIterations, "DefaultBootstrapIterations");
            Storage.SetDouble(MinimumTemperatureSpanForFitting, "MinimumTemperatureSpanForFitting");
            Storage.SetBool(IncludeConcentrationErrorsInBootstrap, "IncludeConcentrationErrorsInBootstrap");
            Storage.SetDouble(OptimizerTolerance, "OptimizerTolerance");
            Storage.SetInt(MaximumOptimizerIterations, "MaximumOptimizerIterations");
            Storage.SetInt((int)ColorScheme, "ColorScheme");
            Storage.SetInt((int)ColorShcemeGradientMode, "ColorShcemeGradientMode");
            Storage.SetDouble(ConcentrationAutoVariance, "ConcentrationAutoVariance");
            Storage.SetBool(UnifyTimeAxisForExport, "UnifyTimeAxisForExport");
            Storage.SetBool(ExportFitPointsWithPeaks, "ExportFitPointsWithPeaks");
            Storage.SetInt((int)ExportSelectionMode, "ExportSelectionMode");

            StoreArray(FinalFigureDimensions, "FinalFigureDimensions");

            Storage.SetBool(true, "IsSaved");

            Storage.Synchronize();

            SettingsDidUpdate?.Invoke(null, null);
        }

        static void StoreArray(double[] arr, string key)
        {
            // Create an NSMutableArray to hold the NSNumber objects
            var array = new NSMutableArray();

            // Convert each double to an NSNumber object and add it to the NSMutableArray
            foreach (double d in arr) array.Add(new NSNumber(d));

            // Store the NSMutableArray as an array with a specific key
            Storage.SetValueForKey(array, new NSString(key));
        }

        public static void Load()
        {
            NSDictionary dict = Storage.ToDictionary();

            // Check if the dictionary is empty or not
            if (!Storage.BoolForKey("IsSaved"))
            {
                Console.WriteLine("No settings are stored in NSUserDefaults.");
                return;
            }
            else Console.WriteLine("There are {0} settings stored in NSUserDefaults.", dict.Count);
            
            ReferenceTemperature = Storage.DoubleForKey("ReferenceTemperature");
            EnergyUnit = (EnergyUnit)(int)Storage.IntForKey("EnergyUnit");
            DefaultErrorEstimationMethod = (ErrorEstimationMethod)(int)Storage.IntForKey("DefaultErrorEstimationMethod");
            DefaultBootstrapIterations = (int)Storage.IntForKey("DefaultBootstrapIterations");
            MinimumTemperatureSpanForFitting = Storage.DoubleForKey("MinimumTemperatureSpanForFitting");
            IncludeConcentrationErrorsInBootstrap = Storage.BoolForKey("IncludeConcentrationErrorsInBootstrap");
            OptimizerTolerance = Storage.DoubleForKey("OptimizerTolerance");
            MaximumOptimizerIterations = (int)Storage.IntForKey("MaximumOptimizerIterations");
            if (MaximumOptimizerIterations == 0) MaximumOptimizerIterations = 300000;
            ColorScheme = (ColorSchemes)(int)Storage.IntForKey("ColorScheme");
            ColorShcemeGradientMode = (ColorShcemeGradientMode)(int)Storage.IntForKey("ColorShcemeGradientMode");
            ConcentrationAutoVariance = Storage.DoubleForKey("ConcentrationAutoVariance");
            UnifyTimeAxisForExport = Storage.BoolForKey("UnifyTimeAxisForExport");
            ExportFitPointsWithPeaks = Storage.BoolForKey("ExportFitPointsWithPeaks");
            ExportSelectionMode = (Exporter.ExportDataSelection)(int)Storage.IntForKey("ExportSelectionMode");

            ApplyDefaultSettings();
        }

        static double[] GetArray(string key)
        {
            var obj = Storage.ValueForKey(new NSString(key));

            var arr = Storage.ArrayForKey(key);

            return arr.Select(v => (double)NSNumber.FromObject(v)).ToArray();
        }

        public static void ApplyDefaultSettings()
        {
            FittingOptionsController.BootstrapIterations = DefaultBootstrapIterations;
            FittingOptionsController.ErrorEstimationMethod = DefaultErrorEstimationMethod;
            FittingOptionsController.IncludeConcentrationVariance = IncludeConcentrationErrorsInBootstrap;
            FittingOptionsController.AutoConcentrationVariance = ConcentrationAutoVariance;
            FittingOptionsController.EnableAutoConcentrationVariance = IsConcentrationAutoVarianceEnabled;
            FinalFigureGraphView.Width = (float)FinalFigureDimensions[0];
            FinalFigureGraphView.Height = (float)FinalFigureDimensions[1];
        }
    }

    public enum PeakFitAlgorithm
    {
        Exponential,
        Default,
    }
}
