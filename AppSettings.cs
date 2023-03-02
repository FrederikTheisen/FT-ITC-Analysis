﻿using System;
using AnalysisITC.AppClasses.Analysis2;
using Foundation;
using System.Linq;

namespace AnalysisITC
{
    public static class AppSettings
    {
        static NSUserDefaults Storage => NSUserDefaults.StandardUserDefaults;

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
        public static int MaximumOptimizerIterations { get; set; } = 300000;

        //Final figure
        public static double[] FinalFigureDimensions { get; set; } = new double[2] { 6.0, 10.0 };

        public static void Save()
        {
            Storage.SetDouble(ReferenceTemperature, "ReferenceTemperature");
            Storage.SetInt((int)EnergyUnit, "EnergyUnit");
            Storage.SetInt((int)DefaultErrorEstimationMethod, "DefaultErrorEstimationMethod");
            Storage.SetInt(DefaultBootstrapIterations, "DefaultBootstrapIterations");
            Storage.SetDouble(MinimumTemperatureSpanForFitting, "MinimumTemperatureSpanForFitting");
            Storage.SetBool(IncludeConcentrationErrorsInBootstrap, "IncludeConcentrationErrorsInBootstrap");
            Storage.SetDouble(OptimizerTolerance, "OptimizerTolerance");
            Storage.SetInt(MaximumOptimizerIterations, "MaximumOptimizerIterations");

            StoreArray(FinalFigureDimensions, "FinalFigureDimensions");

            Storage.SetBool(true, "IsSaved");

            Storage.Synchronize();
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
        }
    }

    public enum PeakFitAlgorithm
    {
        Exponential,
        Default,
    }
}
