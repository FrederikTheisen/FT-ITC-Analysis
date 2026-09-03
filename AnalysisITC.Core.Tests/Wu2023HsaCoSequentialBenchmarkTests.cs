using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Published model reproduction")]
public sealed class Wu2023HsaCoSequentialBenchmarkTests : IDisposable
{
    readonly DilutionMethod originalDilutionMethod;
    readonly bool originalReprocessSetting;

    public Wu2023HsaCoSequentialBenchmarkTests()
    {
        originalDilutionMethod = AppSettings.DilutionCalculationMethod;
        originalReprocessSetting = AppSettings.ReprocessIntegratedHeatDataOnLoad;
        AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
        AppSettings.ReprocessIntegratedHeatDataOnLoad = true;
        IntegratedHeatReader.BeginImportQueue();
        PlatformServices.RegisterImportPromptService(new FixedEnergyUnitPromptService(EnergyUnit.MicroCal));
    }

    public void Dispose()
    {
        IntegratedHeatReader.EndImportQueue();
        PlatformServices.RegisterImportPromptService(null);
        AppSettings.DilutionCalculationMethod = originalDilutionMethod;
        AppSettings.ReprocessIntegratedHeatDataOnLoad = originalReprocessSetting;
    }

    public static IEnumerable<object[]> Optimizers()
    {
        yield return new object[] { SolverAlgorithm.LevenbergMarquardt };
        yield return new object[] { SolverAlgorithm.NelderMead };
    }

    [Theory]
    [MemberData(nameof(Optimizers))]
    public void H67AFigureIntegratedHeatsRecoverPublishedTwoStepSequentialFit(
        SolverAlgorithm algorithm)
    {
        var fit = Fit(
            "wu2023-h67a-hsa-co-figure6.DH",
            new PublishedParameters(4.85, -5568.0, 3.33, -18250.0),
            algorithm);

        Assert.InRange(fit.PublishedRmsdCalPerMole, 0, 50);
        Assert.InRange(fit.FittedRmsdCalPerMole, 0, 40);
        Assert.InRange(Math.Abs(fit.LogK1 - 4.85), 0, 0.03);
        Assert.InRange(Math.Abs(fit.LogK2 - 3.33), 0, 0.03);
        Assert.InRange(RelativeDifference(fit.Enthalpy1CalPerMole, -5568.0), 0, 0.03);
        Assert.InRange(RelativeDifference(fit.Enthalpy2CalPerMole, -18250.0), 0, 0.07);
    }

    [Fact]
    public void H9AFigureIntegratedHeatsExposeStableAllFreeParameterUnderidentification()
    {
        var published = new PublishedParameters(4.85, -4958.0, 3.45, -15820.0);
        var lm = Fit("wu2023-h9a-hsa-co-figure6.DH", published, SolverAlgorithm.LevenbergMarquardt);
        var nm = Fit("wu2023-h9a-hsa-co-figure6.DH", published, SolverAlgorithm.NelderMead);

        // The Table S3 vector already predicts the digitized author curve to
        // about six vertical pixels RMSD at the figure's 5.1 cal/mol-per-pixel scale.
        // Nevertheless both optimizers move to the same distant point on the
        // shallow four-parameter valley for only a ~10 cal/mol RMSD improvement.
        Assert.InRange(lm.PublishedRmsdCalPerMole, 0, 35);
        Assert.InRange(lm.FittedRmsdCalPerMole, 0, 25);
        Assert.True(Math.Abs(lm.LogK1 - published.LogK1) > 0.4);
        Assert.True(RelativeDifference(lm.Enthalpy1CalPerMole, published.Enthalpy1CalPerMole) > 0.4);

        Assert.InRange(Math.Abs(lm.LogK1 - nm.LogK1), 0, 0.001);
        Assert.InRange(Math.Abs(lm.LogK2 - nm.LogK2), 0, 0.001);
        Assert.InRange(RelativeDifference(lm.Enthalpy1CalPerMole, nm.Enthalpy1CalPerMole), 0, 0.001);
        Assert.InRange(RelativeDifference(lm.Enthalpy2CalPerMole, nm.Enthalpy2CalPerMole), 0, 0.001);
    }

    static FitResult Fit(string fixtureName, PublishedParameters published, SolverAlgorithm algorithm)
    {
        var experiment = LoadExperiment(fixtureName);
        AssertExperimentShape(experiment);

        var model = new SequentialBindingSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, published.LogK1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity2, published.LogK2);
        model.Parameters.AddOrUpdateParameter(
            ParameterType.Enthalpy1,
            Energy.ConvertToJoule(published.Enthalpy1CalPerMole, EnergyUnit.Cal));
        model.Parameters.AddOrUpdateParameter(
            ParameterType.Enthalpy2,
            Energy.ConvertToJoule(published.Enthalpy2CalPerMole, EnergyUnit.Cal));
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0.0, true);

        var publishedRmsd = NormalizedRmsdCalPerMole(model, experiment);
        var solver = new Solver
        {
            Model = model,
            SolverAlgorithm = algorithm,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = false,
            MaxOptimizerIterations = 20000,
            Silent = true,
        };
        var convergence = solver.Solve();
        Assert.True(convergence.Success, convergence.Message);

        return new FitResult(
            model.Parameters.Table[ParameterType.Affinity1].Value,
            Energy.ConvertFromJoule(model.Parameters.Table[ParameterType.Enthalpy1].Value, EnergyUnit.Cal),
            model.Parameters.Table[ParameterType.Affinity2].Value,
            Energy.ConvertFromJoule(model.Parameters.Table[ParameterType.Enthalpy2].Value, EnergyUnit.Cal),
            publishedRmsd,
            NormalizedRmsdCalPerMole(model, experiment));
    }

    static void AssertExperimentShape(ExperimentData experiment)
    {
        Assert.Equal(35, experiment.Injections.Count);
        Assert.Equal(50e-6, experiment.CellConcentration.Value, 12);
        Assert.Equal(2e-3, experiment.SyringeConcentration.Value, 12);
        Assert.Equal(1.4314e-3, experiment.CellVolume, 12);
        Assert.False(experiment.Injections[0].Include);
        Assert.Equal(2.0005e-6, experiment.Injections[0].Volume, 12);
        Assert.All(experiment.Injections.Skip(1), injection => Assert.Equal(8e-6, injection.Volume, 12));
    }

    static double RelativeDifference(double actual, double expected) =>
        Math.Abs(actual - expected) / Math.Abs(expected);

    static double NormalizedRmsdCalPerMole(SequentialBindingSites model, ExperimentData experiment)
    {
        var squared = experiment.Injections
            .Where(injection => injection.Include)
            .Select(injection =>
            {
                var residual = (model.Evaluate(injection.ID) - injection.PeakArea.Value) / injection.InjectionMass;
                var calPerMole = Energy.ConvertFromJoule(residual, EnergyUnit.Cal);
                return calPerMole * calPerMole;
            });
        return Math.Sqrt(squared.Average());
    }

    static ExperimentData LoadExperiment(string fixtureName) => IntegratedHeatReader.ReadFile(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "PublishedBenchmarks",
        "wu2023-hsa-co-sequential",
        fixtureName));

    sealed record PublishedParameters(
        double LogK1,
        double Enthalpy1CalPerMole,
        double LogK2,
        double Enthalpy2CalPerMole);

    sealed record FitResult(
        double LogK1,
        double Enthalpy1CalPerMole,
        double LogK2,
        double Enthalpy2CalPerMole,
        double PublishedRmsdCalPerMole,
        double FittedRmsdCalPerMole);

    sealed class FixedEnergyUnitPromptService : IImportPromptService
    {
        readonly EnergyUnit unit;

        public FixedEnergyUnitPromptService(EnergyUnit unit) => this.unit = unit;

        public EnergyUnitPromptResult AskForEnergyUnit(
            string filePath,
            string encounteredValue,
            bool allowQueueReuse) => new(unit, useForRemainingFilesInQueue: false, isCancelled: false);
    }
}
