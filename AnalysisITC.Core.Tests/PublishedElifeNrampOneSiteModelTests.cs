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
public sealed class PublishedElifeNrampOneSiteModelTests : IDisposable
{
    private const double RelativeTolerance = 0.005;
    private readonly DilutionMethod originalDilutionMethod;
    private readonly bool originalReprocessSetting;

    public PublishedElifeNrampOneSiteModelTests()
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
    [InlineData("elife2023-a47w-d296a-mn-onesite-first-run.DH", 3340.0, 6164.0, DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-a47w-d296a-mn-onesite-first-run.DH", 3340.0, 6164.0, DilutionMethod.MicroCal, SolverAlgorithm.NelderMead)]
    [InlineData("elife2023-a47w-d296a-mn-onesite-first-run.DH", 3340.0, 6164.0, DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-a47w-d296a-mn-onesite-first-run.DH", 3340.0, 6164.0, DilutionMethod.Exponential, SolverAlgorithm.NelderMead)]
    [InlineData("elife2023-a47w-d369a-mn-onesite-first-run.DH", 4690.0, 7563.0, DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-a47w-d369a-mn-onesite-first-run.DH", 4690.0, 7563.0, DilutionMethod.MicroCal, SolverAlgorithm.NelderMead)]
    [InlineData("elife2023-a47w-d369a-mn-onesite-first-run.DH", 4690.0, 7563.0, DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-a47w-d369a-mn-onesite-first-run.DH", 4690.0, 7563.0, DilutionMethod.Exponential, SolverAlgorithm.NelderMead)]
    [InlineData("elife2023-d369a-mn-onesite-first-run.DH", 2590.0, 9478.0, DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-d369a-mn-onesite-first-run.DH", 2590.0, 9478.0, DilutionMethod.MicroCal, SolverAlgorithm.NelderMead)]
    [InlineData("elife2023-d369a-mn-onesite-first-run.DH", 2590.0, 9478.0, DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-d369a-mn-onesite-first-run.DH", 2590.0, 9478.0, DilutionMethod.Exponential, SolverAlgorithm.NelderMead)]
    [InlineData("elife2023-m230a-d296a-mn-onesite-first-run.DH", 3110.0, 7063.0, DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-m230a-d296a-mn-onesite-first-run.DH", 3110.0, 7063.0, DilutionMethod.MicroCal, SolverAlgorithm.NelderMead)]
    [InlineData("elife2023-m230a-d296a-mn-onesite-first-run.DH", 3110.0, 7063.0, DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-m230a-d296a-mn-onesite-first-run.DH", 3110.0, 7063.0, DilutionMethod.Exponential, SolverAlgorithm.NelderMead)]
    [InlineData("elife2023-m230a-cd-onesite-first-run.DH", 7030.0, -6177.0, DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-m230a-cd-onesite-first-run.DH", 7030.0, -6177.0, DilutionMethod.MicroCal, SolverAlgorithm.NelderMead)]
    [InlineData("elife2023-m230a-cd-onesite-first-run.DH", 7030.0, -6177.0, DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData("elife2023-m230a-cd-onesite-first-run.DH", 7030.0, -6177.0, DilutionMethod.Exponential, SolverAlgorithm.NelderMead)]
    public void OneSetOfSitesReproducesOriginOneSiteFits(
        string fixtureName,
        double originAssociationConstant,
        double originEnthalpyCalPerMole,
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

        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1.0, true);
        model.Parameters.AddOrUpdateParameter(
            ParameterType.Enthalpy1,
            Energy.ConvertToJoule(originEnthalpyCalPerMole, EnergyUnit.Cal));
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, Math.Log10(originAssociationConstant));
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0.0, true);

        var solver = new Solver
        {
            Model = model,
            SolverAlgorithm = solverAlgorithm,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = false,
            MaxOptimizerIterations = 4000,
            Silent = true,
        };

        var convergence = solver.Solve();

        Assert.True(convergence.Success, convergence.Message);
        AssertRelativeAgreement(
            "Ka",
            originAssociationConstant,
            Math.Pow(10.0, model.Parameters.Table[ParameterType.Affinity1].Value));
        AssertRelativeAgreement(
            "dH",
            Energy.ConvertToJoule(originEnthalpyCalPerMole, EnergyUnit.Cal),
            model.Parameters.Table[ParameterType.Enthalpy1].Value);
    }

    private static ExperimentData LoadExperiment(string fixtureName) => IntegratedHeatReader.ReadFile(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "PublishedBenchmarks",
        fixtureName));

    private static void AssertRelativeAgreement(string parameter, double expected, double actual)
    {
        var relativeDifference = Math.Abs((actual - expected) / expected);
        Assert.True(
            relativeDifference <= RelativeTolerance,
            $"{parameter}: expected {expected:G10}, fitted {actual:G10}, relative difference {relativeDifference:P3}");
    }

    private sealed class FixedEnergyUnitPromptService : IImportPromptService
    {
        private readonly EnergyUnit unit;

        public FixedEnergyUnitPromptService(EnergyUnit unit) => this.unit = unit;

        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse) =>
            new(unit, useForRemainingFilesInQueue: false, isCancelled: false);
    }
}
