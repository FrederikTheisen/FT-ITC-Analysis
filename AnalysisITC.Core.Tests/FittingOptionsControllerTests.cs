using System;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;

using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class FittingOptionsControllerTests
{
    [Fact]
    public void ResetToPreferenceDefaultsCopiesPreferencesWithoutSavingThem()
    {
        var previousAlgorithm = AppSettings.DefaultSolverAlgorithm;
        var previousErrorMethod = AppSettings.DefaultErrorEstimationMethod;
        var previousBootstrapIterations = AppSettings.DefaultBootstrapIterations;
        var previousIncludeConcentrationErrors = AppSettings.IncludeConcentrationErrorsInBootstrap;
        var previousWeightedFitting = AppSettings.UseInjectionErrorWeightedFitting;

        var previousLiveAlgorithm = FittingOptionsController.Algorithm;
        var previousLiveErrorMethod = FittingOptionsController.ErrorEstimationMethod;
        var previousLiveBootstrapIterations = FittingOptionsController.BootstrapIterations;
        var previousLiveIncludeConcentrationErrors = FittingOptionsController.IncludeConcentrationVariance;
        var previousLiveWeightedFitting = FittingOptionsController.UseErrorWeightedFitting;
        var previousLiveUnlock = FittingOptionsController.UnlockBootstrapParameters;
        var settingsUpdated = false;
        EventHandler settingsUpdatedHandler = (_, _) => settingsUpdated = true;

        try
        {
            AppSettings.DefaultSolverAlgorithm = SolverAlgorithm.LevenbergMarquardt;
            AppSettings.DefaultErrorEstimationMethod = ErrorEstimationMethod.LeaveOneOut;
            AppSettings.DefaultBootstrapIterations = 500;
            AppSettings.IncludeConcentrationErrorsInBootstrap = true;
            AppSettings.UseInjectionErrorWeightedFitting = true;

            FittingOptionsController.Algorithm = SolverAlgorithm.NelderMead;
            FittingOptionsController.ErrorEstimationMethod = ErrorEstimationMethod.None;
            FittingOptionsController.BootstrapIterations = 10;
            FittingOptionsController.IncludeConcentrationVariance = false;
            FittingOptionsController.UseErrorWeightedFitting = false;
            FittingOptionsController.UnlockBootstrapParameters = true;

            AppSettings.SettingsDidUpdate += settingsUpdatedHandler;
            FittingOptionsController.ResetToPreferenceDefaults();

            Assert.Equal(SolverAlgorithm.LevenbergMarquardt, FittingOptionsController.Algorithm);
            Assert.Equal(ErrorEstimationMethod.LeaveOneOut, FittingOptionsController.ErrorEstimationMethod);
            Assert.Equal(500, FittingOptionsController.BootstrapIterations);
            Assert.True(FittingOptionsController.IncludeConcentrationVariance);
            Assert.True(FittingOptionsController.UseErrorWeightedFitting);
            Assert.False(FittingOptionsController.UnlockBootstrapParameters);
            Assert.False(settingsUpdated);

            Assert.Equal(SolverAlgorithm.LevenbergMarquardt, AppSettings.DefaultSolverAlgorithm);
            Assert.Equal(ErrorEstimationMethod.LeaveOneOut, AppSettings.DefaultErrorEstimationMethod);
            Assert.Equal(500, AppSettings.DefaultBootstrapIterations);
            Assert.True(AppSettings.IncludeConcentrationErrorsInBootstrap);
            Assert.True(AppSettings.UseInjectionErrorWeightedFitting);
        }
        finally
        {
            AppSettings.SettingsDidUpdate -= settingsUpdatedHandler;

            AppSettings.DefaultSolverAlgorithm = previousAlgorithm;
            AppSettings.DefaultErrorEstimationMethod = previousErrorMethod;
            AppSettings.DefaultBootstrapIterations = previousBootstrapIterations;
            AppSettings.IncludeConcentrationErrorsInBootstrap = previousIncludeConcentrationErrors;
            AppSettings.UseInjectionErrorWeightedFitting = previousWeightedFitting;

            FittingOptionsController.Algorithm = previousLiveAlgorithm;
            FittingOptionsController.ErrorEstimationMethod = previousLiveErrorMethod;
            FittingOptionsController.BootstrapIterations = previousLiveBootstrapIterations;
            FittingOptionsController.IncludeConcentrationVariance = previousLiveIncludeConcentrationErrors;
            FittingOptionsController.UseErrorWeightedFitting = previousLiveWeightedFitting;
            FittingOptionsController.UnlockBootstrapParameters = previousLiveUnlock;
        }
    }
}
