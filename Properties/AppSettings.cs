using System;
using AnalysisITC.AppClasses.Analysis2;
using Foundation;
using System.Linq;
using AnalysisITC.GUI;
using static AnalysisITC.AppClasses.Analysis2.Models.SolutionInterface;

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
        public static ConcentrationUnit DefaultConcentrationUnit { get; set; } = ConcentrationUnit.µM;

        public static NSUrl LastDocumentUrl { get; set; } = null;

        //Processing
        public static PeakFitAlgorithm PeakFitAlgorithm { get; set; } = PeakFitAlgorithm.Exponential;

        //Fitting
        public static bool InputAffinityAsDissociationConstant { get; set; } = true;

        public static ErrorEstimationMethod DefaultErrorEstimationMethod { get; set; } = ErrorEstimationMethod.BootstrapResiduals;
        public static int DefaultBootstrapIterations { get; set; } = 100;
        public static double MinimumTemperatureSpanForFitting { get; internal set; } = 2;
        public static bool IncludeConcentrationErrorsInBootstrap { get; set; } = true;
        public static double ConcentrationAutoVariance { get; set; } = 0.05;
        
        public static double OptimizerTolerance { get; set; } = double.Epsilon;
        public static int MaximumOptimizerIterations { get; set; } = 300000;
        public static bool EnableExtendedParameterLimits { get; set; } = false;

        //Final figure
        public static double[] FinalFigureDimensions { get; set; } = new double[2] { 6.5, 10.0 };
        public static FinalFigureDisplayParameters FinalFigureParameterDisplay { get; set; } = FinalFigureDisplayParameters.Default;

        //Export
        public static bool UnifyTimeAxisForExport { get; set; } = true;
        public static bool ExportBaselineCorrectedData { get; set; } = true;
        public static bool ExportFitPointsWithPeaks { get; set; } = true;
        public static Exporter.ExportDataSelection ExportSelectionMode { get; set; } = Exporter.ExportDataSelection.IncludedData;

        public static bool IsConcentrationAutoVarianceEnabled = ConcentrationAutoVariance > 0.001;

        public static void Save()
        {
            ApplySettings();

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
            Storage.SetInt((int)FinalFigureParameterDisplay, "FinalFigureParameterDisplay");
            Storage.SetBool(ExportBaselineCorrectedData, "ExportBaselineCorrectedData");
            Storage.SetInt((int)DefaultConcentrationUnit, "DefaultConcentrationUnit");
            Storage.SetBool(InputAffinityAsDissociationConstant, "InputAffinityAsDissociationConstant");
            Storage.SetURL(LastDocumentUrl, "LastDocumentUrl");
            Storage.SetBool(EnableExtendedParameterLimits, "EnableExtendedParameterLimits");

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

            ReferenceTemperature = GetDouble(dict, "ReferenceTemperature", ReferenceTemperature);
            EnergyUnit = (EnergyUnit)GetInt(dict, "EnergyUnit", (int)EnergyUnit);
            DefaultErrorEstimationMethod = (ErrorEstimationMethod)GetInt(dict, "DefaultErrorEstimationMethod", (int)DefaultErrorEstimationMethod);
            DefaultBootstrapIterations = GetInt(dict, "DefaultBootstrapIterations", DefaultBootstrapIterations);
            MinimumTemperatureSpanForFitting = GetDouble(dict, "MinimumTemperatureSpanForFitting", MinimumTemperatureSpanForFitting);
            IncludeConcentrationErrorsInBootstrap = GetBool(dict, "IncludeConcentrationErrorsInBootstrap", IncludeConcentrationErrorsInBootstrap);
            OptimizerTolerance = GetDouble(dict, "OptimizerTolerance", OptimizerTolerance);
            MaximumOptimizerIterations = GetInt(dict, "MaximumOptimizerIterations", MaximumOptimizerIterations);
            ColorScheme = (ColorSchemes)GetInt(dict, "ColorScheme", (int)ColorScheme);
            ColorShcemeGradientMode = (ColorShcemeGradientMode)GetInt(dict, "ColorShcemeGradientMode", (int)ColorShcemeGradientMode);
            ConcentrationAutoVariance = GetDouble(dict, "ConcentrationAutoVariance", ConcentrationAutoVariance);
            UnifyTimeAxisForExport = GetBool(dict, "UnifyTimeAxisForExport", UnifyTimeAxisForExport);
            ExportFitPointsWithPeaks = GetBool(dict, "ExportFitPointsWithPeaks", ExportFitPointsWithPeaks);
            ExportSelectionMode = (Exporter.ExportDataSelection)GetInt(dict, "ExportSelectionMode", (int)ExportSelectionMode);
            EnableExtendedParameterLimits = GetBool(dict, "EnableExtendedParameterLimits", EnableExtendedParameterLimits);
            FinalFigureParameterDisplay = (FinalFigureDisplayParameters)GetInt(dict, "FinalFigureParameterDisplay", (int)FinalFigureParameterDisplay);
            FinalFigureDimensions = GetArray(dict, "FinalFigureDimensions", FinalFigureDimensions);
            ExportBaselineCorrectedData = GetBool(dict, "ExportBaselineCorrectedData", ExportBaselineCorrectedData);
            DefaultConcentrationUnit = (ConcentrationUnit)GetInt(dict, "DefaultConcentrationUnit", (int)DefaultConcentrationUnit);
            InputAffinityAsDissociationConstant = GetBool(dict, "InputAffinityAsDissociationConstant", InputAffinityAsDissociationConstant);
            LastDocumentUrl = GetUrl(dict, "LastDocumentUrl");

            ApplySettings();
        }

        static int GetInt(NSDictionary dict, string key, int def = 0)
        {
            if (dict.ContainsKey(NSObject.FromObject(key)))
                return (int)Storage.IntForKey(key);
            else return def;
        }

        static double GetDouble(NSDictionary dict, string key, double def = 0)
        {
            if (dict.ContainsKey(NSObject.FromObject(key)))
                return Storage.DoubleForKey(key);
            else return def;
        }

        static bool GetBool(NSDictionary dict, string key, bool def = false)
        {
            if (dict.ContainsKey(NSObject.FromObject(key)))
                return Storage.BoolForKey(key);
            else return def;
        }

        private static NSUrl GetUrl(NSDictionary dict, string key)
        {
            if (dict.ContainsKey(NSObject.FromObject(key)))
                return Storage.URLForKey(key);
            else return null;
        }

        static double[] GetArray(NSDictionary dict, string key, double[] def = null)
        {
            if (!dict.ContainsKey(NSObject.FromObject(key))) return def;

            var arr = Storage.ArrayForKey(key);
            var doubleArray = new double[arr.Count()];

            for (int i = 0; i < arr.Count(); i++)
            {
                doubleArray[i] = ((NSNumber)arr.GetValue(i)).DoubleValue;
            }

            return doubleArray;
        }

        public static void ApplySettings()
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
