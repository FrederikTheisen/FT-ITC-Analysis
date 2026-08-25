using System;
using System.IO;
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
public sealed class PublishedElifeTwoSiteSourceDataTests : IDisposable
{
    private readonly DilutionMethod originalDilutionMethod;
    private readonly bool originalReprocessSetting;

    public PublishedElifeTwoSiteSourceDataTests()
    {
        originalDilutionMethod = AppSettings.DilutionCalculationMethod;
        originalReprocessSetting = AppSettings.ReprocessIntegratedHeatDataOnLoad;
        AppSettings.ReprocessIntegratedHeatDataOnLoad = false;
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

    [Theory]
    [InlineData("elife2023-wt-mn-twosite-first-run.DH", DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-wt-mn-twosite-first-run.DH", DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-wt-mn-twosite-second-run.DH", DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-wt-mn-twosite-second-run.DH", DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-wt-mn-twosite-third-run.DH", DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-wt-mn-twosite-third-run.DH", DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-wt-cd-twosite-first-run.DH", DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-wt-cd-twosite-first-run.DH", DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-wt-cd-twosite-second-run.DH", DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-wt-cd-twosite-second-run.DH", DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-wt-cd-twosite-third-run.DH", DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-wt-cd-twosite-third-run.DH", DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt)]
    public void TwoSiteSourceDataFitsWithoutWeighting(
        string fixtureName,
        DilutionMethod dilutionMethod,
        SolverAlgorithm solverAlgorithm)
    {
        AppSettings.DilutionCalculationMethod = dilutionMethod;
        var experiment = LoadExperiment(fixtureName);

        Assert.Equal(20, experiment.Injections.Count);
        Assert.Equal(25e-6, experiment.CellConcentration, 12);
        Assert.Equal(6e-3, experiment.SyringeConcentration, 12);
        Assert.Equal(203.9e-6, experiment.CellVolume, 12);
        Assert.False(experiment.Injections[0].Include);

        var model = new TwoSetsOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1.0, true);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue2, 1.0, true);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, Math.Log10(5263.0));
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity2, Math.Log10(508.0));
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, Energy.ConvertToJoule(5000.0, EnergyUnit.Cal));
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy2, Energy.ConvertToJoule(10000.0, EnergyUnit.Cal));
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0.0, true);

        var solver = new Solver
        {
            Model = model,
            SolverAlgorithm = solverAlgorithm,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = false,
            MaxOptimizerIterations = 6000,
            Silent = true,
        };

        var convergence = solver.Solve();

        Assert.True(convergence.Success, convergence.Message);
        Assert.All(new[]
        {
            model.Parameters.Table[ParameterType.Affinity1].Value,
            model.Parameters.Table[ParameterType.Enthalpy1].Value,
            model.Parameters.Table[ParameterType.Affinity2].Value,
            model.Parameters.Table[ParameterType.Enthalpy2].Value
        }, value => Assert.True(double.IsFinite(value)));
        Console.WriteLine(
            $"{fixtureName} {dilutionMethod}: Ka1={Math.Pow(10, model.Parameters.Table[ParameterType.Affinity1].Value):G8}, " +
            $"dH1={model.Parameters.Table[ParameterType.Enthalpy1].Value / Energy.ConvertToJoule(1.0, EnergyUnit.Cal):G8} cal/mol, " +
            $"Ka2={Math.Pow(10, model.Parameters.Table[ParameterType.Affinity2].Value):G8}, " +
            $"dH2={model.Parameters.Table[ParameterType.Enthalpy2].Value / Energy.ConvertToJoule(1.0, EnergyUnit.Cal):G8} cal/mol");
    }

    [Theory]
    [InlineData(DilutionMethod.MicroCal)]
    [InlineData(DilutionMethod.Exponential)]
    public void SedphatPublishedTwoSiteTableFitsWithoutWeighting(DilutionMethod dilutionMethod)
    {
        AppSettings.DilutionCalculationMethod = dilutionMethod;
        var experiment = LoadExperiment("sedphat-itc-two-site.DH");

        Assert.Equal(21, experiment.Injections.Count);
        Assert.Equal(4.5e-6, experiment.CellConcentration, 12);
        Assert.Equal(50e-6, experiment.SyringeConcentration, 12);
        Assert.Equal(1414.1e-6, experiment.CellVolume, 12);
        Assert.False(experiment.Injections[0].Include);
        Assert.Equal(2e-6, experiment.Injections[0].Volume, 12);
        Assert.Equal(8e-6, experiment.Injections[1].Volume, 12);

        var model = new TwoSetsOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1.0, true);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue2, 1.0, true);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6.616);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity2, Math.Log10(1037.0));
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, Energy.ConvertToJoule(-18430.0, EnergyUnit.Cal));
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy2, Energy.ConvertToJoule(-18430.0, EnergyUnit.Cal));
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0.0, true);

        var solver = new Solver
        {
            Model = model,
            SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = false,
            MaxOptimizerIterations = 6000,
            Silent = true,
        };

        var convergence = solver.Solve();
        Assert.True(convergence.Success, convergence.Message);
        Assert.All(new[]
        {
            model.Parameters.Table[ParameterType.Affinity1].Value,
            model.Parameters.Table[ParameterType.Enthalpy1].Value,
            model.Parameters.Table[ParameterType.Affinity2].Value,
            model.Parameters.Table[ParameterType.Enthalpy2].Value
        }, value => Assert.True(double.IsFinite(value)));
        Console.WriteLine(
            $"sedphat-itc-two-site.DH {dilutionMethod}: Ka1={Math.Pow(10, model.Parameters.Table[ParameterType.Affinity1].Value):G8}, " +
            $"dH1={model.Parameters.Table[ParameterType.Enthalpy1].Value / Energy.ConvertToJoule(1.0, EnergyUnit.Cal):G8} cal/mol, " +
            $"Ka2={Math.Pow(10, model.Parameters.Table[ParameterType.Affinity2].Value):G8}, " +
            $"dH2={model.Parameters.Table[ParameterType.Enthalpy2].Value / Energy.ConvertToJoule(1.0, EnergyUnit.Cal):G8} cal/mol");
    }

    private static ExperimentData LoadExperiment(string fixtureName) => IntegratedHeatReader.ReadFile(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "PublishedBenchmarks",
        fixtureName));

    private sealed class FixedEnergyUnitPromptService : IImportPromptService
    {
        private readonly EnergyUnit unit;

        public FixedEnergyUnitPromptService(EnergyUnit unit) => this.unit = unit;

        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse) =>
            new(unit, useForRemainingFilesInQueue: false, isCancelled: false);
    }
}
