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

        static NSDictionary Default = new NSDictionary();
        static NSUserDefaults Storage => NSUserDefaults.StandardUserDefaults;

        //General
        public static double ReferenceTemperature { get; set; } = 25.0;
        public static EnergyUnit EnergyUnit { get; set; } = EnergyUnit.KiloJoule;
        public static ColorSchemes ColorScheme { get; set; } = ColorSchemes.Default;
        public static ColorSchemeGradientMode ColorSchemeGradientMode { get; set; } = ColorSchemeGradientMode.Smooth;
        public static ConcentrationUnit DefaultConcentrationUnit { get; set; } = ConcentrationUnit.µM;
        public static int MaxDegreeOfParallelism { get; set; } = 10;

        private static NSUrl lastDocumentUrl = null;
        public static NSUrl LastDocumentUrl { get => lastDocumentUrl; set { lastDocumentUrl = value; Save(); } }

        //Processing
        public static PeakFitAlgorithm PeakFitAlgorithm { get; set; } = PeakFitAlgorithm.SingleExponential;
        public static bool IncludeBufferInIonicStrengthCalc { get; set; } = true;

        //Fitting
        public static bool InputAffinityAsDissociationConstant { get; set; } = true;

        public static ErrorEstimationMethod DefaultErrorEstimationMethod { get; set; } = ErrorEstimationMethod.BootstrapResiduals;
        public static int DefaultBootstrapIterations { get; set; } = 100;
        public static double MinimumTemperatureSpanForFitting { get; internal set; } = 2;
        public static double MinimumIonSpanForFitting { get; internal set; } = 0.01;
        public static bool IncludeConcentrationErrorsInBootstrap { get; set; } = false;
        public static double ConcentrationAutoVariance { get; set; } = 0.05;
        public static bool IsConcentrationAutoVarianceEnabled { get; set; } = ConcentrationAutoVariance > 0.001;

        public static double OptimizerTolerance { get; set; } = 0.5;
        public static int MaximumOptimizerIterations { get; set; } = 2000;
        public static bool EnableExtendedParameterLimits { get; set; } = false;
        public static ParameterLimitSetting ParameterLimitSetting { get; set; } = ParameterLimitSetting.Standard;

        //Analysis
        public static bool BuffersPreparedAtRoomTemperature { get; set; } = true;

        //Final figure
        public static double[] FinalFigureDimensions { get; set; } = new double[2] { 6.5, 10.0 };
        public static FinalFigureDisplayParameters FinalFigureParameterDisplay { get; set; } = FinalFigureDisplayParameters.Default;
        public static DisplayAttributeOptions DisplayAttributeOptions { get; set; } = DisplayAttributeOptions.Default;
        public static bool FinalFigureShowParameterBoxAsDefault { get; set; } = true;
        public static NumberPrecision NumberPrecision { get; set; } = NumberPrecision.Standard;
        public static bool ShowResidualGraph { get; set; } = true;
        public static bool ShowResidualGraphGap { get; set; } = false;
        public static bool UnifyResidualGraphAxis { get; set; } = false;

        //Export
        public static bool UnifyTimeAxisForExport { get; set; } = true;
        public static bool ExportBaselineCorrectedData { get; set; } = true;
        public static bool ExportFitPointsWithPeaks { get; set; } = true;
        public static ExportDataSelection ExportSelectionMode { get; set; } = ExportDataSelection.IncludedData;
        public static int NumOfDecimalsToExport { get; set; } = 2;
        public static ExportColumns ExportColumns { get; set; } = ExportColumns.Default;

        public static void Initialize()
        {
            Load();
        }

        public static void Save()
        {
            ApplySettings();
            SaveToStorage();
            Storage.Synchronize();
            SettingsDidUpdate?.Invoke(null, null);
        }

        static void SaveToStorage()
        {
            Storage.SetDouble(ReferenceTemperature, "ReferenceTemperature");
            Storage.SetInt((int)EnergyUnit, "EnergyUnit");
            Storage.SetInt((int)DefaultErrorEstimationMethod, "DefaultErrorEstimationMethod");
            Storage.SetInt(DefaultBootstrapIterations, "DefaultBootstrapIterations");
            Storage.SetDouble(MinimumTemperatureSpanForFitting, "MinimumTemperatureSpanForFitting");
            Storage.SetBool(IncludeConcentrationErrorsInBootstrap, "IncludeConcentrationErrorsInBootstrap");
            Storage.SetDouble(OptimizerTolerance, "OptimizerTolerance");
            Storage.SetInt(MaximumOptimizerIterations, "MaximumOptimizerIterations");
            Storage.SetInt((int)ColorScheme, "ColorScheme");
            Storage.SetInt((int)ColorSchemeGradientMode, "ColorShcemeGradientMode");
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
            Storage.SetInt((int)ParameterLimitSetting, "ParameterLimitSetting");
            Storage.SetBool(BuffersPreparedAtRoomTemperature, "BuffersPreparedAtRoomTemperature");
            Storage.SetInt(NumOfDecimalsToExport, "NumOfDecimalsToExport");
            Storage.SetDouble(MinimumIonSpanForFitting, "MinimumIonSpanForFitting");
            Storage.SetBool(FinalFigureShowParameterBoxAsDefault, "FinalFigureShowParameterBoxAsDefault");
            Storage.SetInt((int)PeakFitAlgorithm, "PeakFitAlgorithm");
            Storage.SetInt((int)NumberPrecision, "NumberPrecision");
            Storage.SetBool(IncludeBufferInIonicStrengthCalc, "IncludeBufferInIonicStrengthCalc");
            Storage.SetInt((int)DisplayAttributeOptions, "DisplayAttributeOptions");
            Storage.SetInt((int)ExportColumns, "ExportColumns");
            Storage.SetBool(ShowResidualGraph, "ShowResidualGraph");
            Storage.SetBool(ShowResidualGraphGap, "ShowResidualGraphGap");
            Storage.SetBool(UnifyResidualGraphAxis, "UnifyResidualGraphAxis");


            StoreArray(FinalFigureDimensions, "FinalFigureDimensions");

            Storage.SetBool(true, "IsSaved");
        }

        public static void Load()
        {
            NSDictionary dict = Storage.ToDictionary();

            // Check if the dictionary is empty or not
            if (!dict.ContainsKey(NSObject.FromObject("IsSaved")) || !Storage.BoolForKey("IsSaved"))
            {
                Console.WriteLine("No settings are stored in NSUserDefaults.");
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
            ColorSchemeGradientMode = (ColorSchemeGradientMode)GetInt(dict, "ColorShcemeGradientMode", (int)ColorSchemeGradientMode);
            ConcentrationAutoVariance = GetDouble(dict, "ConcentrationAutoVariance", ConcentrationAutoVariance);
            UnifyTimeAxisForExport = GetBool(dict, "UnifyTimeAxisForExport", UnifyTimeAxisForExport);
            ExportFitPointsWithPeaks = GetBool(dict, "ExportFitPointsWithPeaks", ExportFitPointsWithPeaks);
            ExportSelectionMode = (ExportDataSelection)GetInt(dict, "ExportSelectionMode", (int)ExportSelectionMode);
            EnableExtendedParameterLimits = GetBool(dict, "EnableExtendedParameterLimits", EnableExtendedParameterLimits);
            ParameterLimitSetting = (ParameterLimitSetting)GetInt(dict, "ParameterLimitSetting", (int)ParameterLimitSetting);
            FinalFigureParameterDisplay = (FinalFigureDisplayParameters)GetInt(dict, "FinalFigureParameterDisplay", (int)FinalFigureParameterDisplay);
            FinalFigureDimensions = GetArray(dict, "FinalFigureDimensions", FinalFigureDimensions);
            ExportBaselineCorrectedData = GetBool(dict, "ExportBaselineCorrectedData", ExportBaselineCorrectedData);
            DefaultConcentrationUnit = (ConcentrationUnit)GetInt(dict, "DefaultConcentrationUnit", (int)DefaultConcentrationUnit);
            InputAffinityAsDissociationConstant = GetBool(dict, "InputAffinityAsDissociationConstant", InputAffinityAsDissociationConstant);
            lastDocumentUrl = GetUrl(dict, "LastDocumentUrl");
            BuffersPreparedAtRoomTemperature = GetBool(dict, "BuffersPreparedAtRoomTemperature", BuffersPreparedAtRoomTemperature);
            NumOfDecimalsToExport = GetInt(dict, "NumOfDecimalsToExport", NumOfDecimalsToExport);
            MinimumIonSpanForFitting = GetDouble(dict, "MinimumIonSpanForFitting", MinimumIonSpanForFitting);
            FinalFigureShowParameterBoxAsDefault = GetBool(dict, "FinalFigureShowParameterBoxAsDefault", FinalFigureShowParameterBoxAsDefault);
            PeakFitAlgorithm = (PeakFitAlgorithm)GetInt(dict, "PeakFitAlgorithm", (int)PeakFitAlgorithm);
            NumberPrecision = (NumberPrecision)GetInt(dict, "NumberPrecision", (int)NumberPrecision);
            IncludeBufferInIonicStrengthCalc = GetBool(dict, "IncludeBufferInIonicStrengthCalc", IncludeBufferInIonicStrengthCalc);
            DisplayAttributeOptions = (DisplayAttributeOptions)GetInt(dict, "DisplayAttributeOptions", (int)DisplayAttributeOptions);
            ExportColumns = (ExportColumns)GetInt(dict, "ExportColumns", (int)ExportColumns.Default);
            ShowResidualGraph = GetBool(dict, "ShowResidualGraph", ShowResidualGraph);
            ShowResidualGraphGap = GetBool(dict, "ShowResidualGraphGap", ShowResidualGraphGap);
            UnifyResidualGraphAxis = GetBool(dict, "UnifyResidualGraphAxis", UnifyResidualGraphAxis);

            ApplySettings();

            StatusBarManager.ClearAppStatus();
        }

        public static void Reset()
        {
            ReferenceTemperature = 25;
            EnergyUnit = EnergyUnit.KiloJoule;
            DefaultErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
            DefaultBootstrapIterations = 100;
            MinimumTemperatureSpanForFitting = 3;
            IncludeConcentrationErrorsInBootstrap = false;
            OptimizerTolerance = double.Epsilon;
            MaximumOptimizerIterations = 300000;
            ColorScheme = ColorSchemes.Default;
            ColorSchemeGradientMode = ColorSchemeGradientMode.Smooth;
            ConcentrationAutoVariance = 0.1;
            UnifyTimeAxisForExport = true;
            ExportFitPointsWithPeaks = true;
            ExportSelectionMode = ExportDataSelection.IncludedData; ;
            EnableExtendedParameterLimits = false;
            ParameterLimitSetting = ParameterLimitSetting.Standard;
            FinalFigureParameterDisplay = FinalFigureDisplayParameters.Default;
            FinalFigureDimensions = new double[] { 6.5, 10 };
            ExportBaselineCorrectedData = true;
            DefaultConcentrationUnit = ConcentrationUnit.µM;
            InputAffinityAsDissociationConstant = true;
            lastDocumentUrl = null;
            IncludeBufferInIonicStrengthCalc = true;
            BuffersPreparedAtRoomTemperature = true;
            NumOfDecimalsToExport = 1;
            MinimumIonSpanForFitting = 0.03;
            FinalFigureShowParameterBoxAsDefault = true;
            PeakFitAlgorithm = PeakFitAlgorithm.SingleExponential;
            NumberPrecision = NumberPrecision.Standard;
            DisplayAttributeOptions = DisplayAttributeOptions.Default;
            ExportColumns = ExportColumns.Default;
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
            FinalFigureGraphView.DrawFitParameters = FinalFigureShowParameterBoxAsDefault;
            FinalFigureGraphView.ShowResiduals = ShowResidualGraph;
            FinalFigureGraphView.GapResidualGraph = ShowResidualGraphGap;
            FinalFigureGraphView.MirrorDataGraphAxisUnification = UnifyResidualGraphAxis;
        }
    }

    public enum PeakFitAlgorithm
    {
        SingleExponential,
        DoubleExponential,
    }

    public enum NumberPrecision
    {
        Strict,
        Standard,
        SingleDecimal,
        AllDecimals,
    }

    public enum ParameterLimitSetting
    {
        Standard,
        Expanded10,
        Expandend100,
        NoLimit,
    }
}
