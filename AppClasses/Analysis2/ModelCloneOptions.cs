using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC
{
    public class ModelCloneOptions
    {
        public bool IsGlobalClone { get; set; } = false;

        public ErrorEstimationMethod ErrorEstimationMethod { get; set; } = ErrorEstimationMethod.None;
        public bool IncludeConcentrationErrorsInBootstrap { get; set; } = false;
        public bool EnableAutoConcentrationVariance { get; set; } = false;
        public double AutoConcentrationVariance { get; set; } = 0.05f;
        public int DiscardedDataPoint { get; set; } = 0;
        public bool UnlockBootstrapParameters { get; set; } = false;

        public ModelCloneOptions()
        {
            ErrorEstimationMethod = FittingOptionsController.ErrorEstimationMethod;
            IncludeConcentrationErrorsInBootstrap = FittingOptionsController.IncludeConcentrationVariance;
            EnableAutoConcentrationVariance = FittingOptionsController.EnableAutoConcentrationVariance;
            AutoConcentrationVariance = FittingOptionsController.AutoConcentrationVariance;
            UnlockBootstrapParameters = FittingOptionsController.UnlockBootstrapParameters;
        }

        public static ModelCloneOptions DefaultOptions
        {
            get
            {
                return new ModelCloneOptions();
            }
        }

        public static ModelCloneOptions DefaultGlobalOptions
        {
            get
            {
                return new ModelCloneOptions()
                {
                    IsGlobalClone = true,
                };
            }
        }
    }
}
