using AnalysisITC.Core.Application;

namespace AnalysisITC.Core.Analysis
{
    public class ModelCloneOptions
    {
        public bool IsGlobalClone { get; set; } = false;

        public ErrorEstimationMethod ErrorEstimationMethod { get; set; } = ErrorEstimationMethod.None;
        public bool IncludeConcentrationErrorsInBootstrap { get; set; } = false;
        public bool EnableAutoConcentrationVariance { get; set; } = false;
        public double AutoConcentrationVariance { get; set; } = 0;
        public int DiscardedDataPoint { get; set; } = 0;
        public bool UnlockBootstrapParameters { get; set; } = false;

        internal bool EffectiveIncludeConcentrationErrors =>
            ErrorEstimationMethod == ErrorEstimationMethod.BootstrapResiduals
            && IncludeConcentrationErrorsInBootstrap;

        internal bool EffectiveUnlockBootstrapParameters =>
            ErrorEstimationMethod == ErrorEstimationMethod.BootstrapResiduals
            && UnlockBootstrapParameters;

        internal bool EffectiveSampleModelOptionParameters =>
            ErrorEstimationMethod == ErrorEstimationMethod.BootstrapResiduals;

        internal bool HasLegacyCombinedLeaveOneOut =>
            ErrorEstimationMethod == ErrorEstimationMethod.LeaveOneOut
            && (IncludeConcentrationErrorsInBootstrap
                || UnlockBootstrapParameters);

        public ModelCloneOptions()
        {
            ErrorEstimationMethod = FittingOptionsController.ErrorEstimationMethod;
            IncludeConcentrationErrorsInBootstrap = FittingOptionsController.IncludeConcentrationVariance;
            UnlockBootstrapParameters = FittingOptionsController.UnlockBootstrapParameters;
        }

        internal void ConfigureForRun(ErrorEstimationMethod method)
        {
            ErrorEstimationMethod = method;
            // Retain historical automatic-concentration metadata on loaded results,
            // but do not carry it into a new fit or uncertainty calculation.
            EnableAutoConcentrationVariance = false;
            AutoConcentrationVariance = 0;

            // Raw flags are retained when a historical result is loaded. Clear them
            // only on the run-specific model graph so newly produced LOO results
            // truthfully record that these bootstrap behaviors were unused.
            if (method == ErrorEstimationMethod.LeaveOneOut || method == ErrorEstimationMethod.ProfileLikelihood)
            {
                IncludeConcentrationErrorsInBootstrap = false;
                UnlockBootstrapParameters = false;
            }

            if (method != ErrorEstimationMethod.LeaveOneOut) return;

            IncludeConcentrationErrorsInBootstrap = false;
            UnlockBootstrapParameters = false;
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
