using System;
using System.Collections.Generic;
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
public sealed class Nature2022CanSequentialBenchmarkTests : IDisposable
{
    const double Eta = 1.0065750263310016;
    const double KdA1Micromolar = 1.4762055628369972;
    const double KdA2Micromolar = 0.18211877274971924;
    const double KdB2Micromolar = 0.27879105348594285;
    const double DhA1KcalPerMole = -36.903317860618174;
    const double DhB1KcalPerMole = -29.819465518667954;
    const double DhB2KcalPerMole = -40.307923920441787;

    static readonly double[] OffsetsKcalPerMole =
    {
        0.34611151995817996,
        -0.033651140984190059,
        -0.56611565226946092,
    };

    readonly DilutionMethod originalDilutionMethod;
    readonly bool originalReprocessSetting;

    public Nature2022CanSequentialBenchmarkTests()
    {
        originalDilutionMethod = AppSettings.DilutionCalculationMethod;
        originalReprocessSetting = AppSettings.ReprocessIntegratedHeatDataOnLoad;
        AppSettings.DilutionCalculationMethod = DilutionMethod.Exponential;
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

    [Fact]
    public void MicroscopicPolynomialAndHeatMapStrictlyToMacroscopicSequentialSteps()
    {
        var experiment = LoadExperiment(1);
        var model = ConfigureModel(experiment, 0);
        var kdB1 = KdA1Micromolar * KdB2Micromolar / KdA2Micromolar;
        var kd1 = MacroscopicKd1Micromolar;
        var kd2 = MacroscopicKd2Micromolar;
        var dh1 = MacroscopicDh1KcalPerMole;
        var dh2 = MacroscopicDh2KcalPerMole;

        Assert.Equal(0.89291414632308064, kd1, 14);
        Assert.Equal(0.46090982623566212, kd2, 14);
        Assert.Equal(-34.104283382675469, dh1, 12);
        Assert.Equal(-43.106958398384485, dh2, 12);

        foreach (var totalLigandMicromolar in new[] { 0.05, 0.25, 1.0, 3.0, 12.0, 50.0 })
        {
            var state = model.CalculateState(Eta * 3e-6, totalLigandMicromolar * 1e-6);
            var freeMicromolar = state.FreeLigand * 1e6;
            var microscopicWeights = new[]
            {
                1.0,
                freeMicromolar / KdA1Micromolar,
                freeMicromolar / kdB1,
                freeMicromolar * freeMicromolar / (KdA1Micromolar * KdB2Micromolar),
            };
            var normalization = microscopicWeights[0] + microscopicWeights[1]
                + microscopicWeights[2] + microscopicWeights[3];
            var microscopicSinglyBound = (microscopicWeights[1] + microscopicWeights[2]) / normalization;
            var microscopicDoublyBound = microscopicWeights[3] / normalization;
            var microscopicHeat = (
                microscopicWeights[1] * DhA1KcalPerMole
                + microscopicWeights[2] * DhB1KcalPerMole
                + microscopicWeights[3] * (DhA1KcalPerMole + DhB2KcalPerMole)) / normalization;
            var macroscopicHeat = state.Fractions[1] * dh1
                + state.Fractions[2] * (dh1 + dh2);

            Assert.InRange(Math.Abs(state.Fractions[1] - microscopicSinglyBound), 0, 2e-14);
            Assert.InRange(Math.Abs(state.Fractions[2] - microscopicDoublyBound), 0, 2e-14);
            Assert.InRange(Math.Abs(macroscopicHeat - microscopicHeat), 0, 2e-12);
        }
    }

    [Theory]
    [InlineData(1, 0.125, 0.128)]
    [InlineData(2, 0.125, 0.128)]
    [InlineData(3, 0.125, 0.128)]
    public void ReferencePredictionsAreReadableAndExposeFiniteInjectionConventionMismatch(
        int run, double minimumCalPerMole, double maximumCalPerMole)
    {
        var experiment = LoadExperiment(run);
        var model = ConfigureModel(experiment, OffsetsKcalPerMole[run - 1]);

        Assert.Equal(29, experiment.Injections.Count);
        Assert.Equal(Eta * 3e-6, experiment.CellConcentration.Value, 15);
        Assert.Equal(run == 3 ? 55e-6 : 60e-6, experiment.SyringeConcentration.Value, 15);
        Assert.Equal(1420.6e-6, experiment.CellVolume, 15);
        Assert.False(experiment.Injections[0].Include);

        var maximumDifference = 0.0;
        for (var index = 1; index < experiment.Injections.Count; index++)
        {
            var injection = experiment.Injections[index];
            var differenceJoulesPerMole = Math.Abs(model.Evaluate(index) - injection.PeakArea.Value)
                / injection.InjectionMass;
            var differenceCalPerMole = Energy.ConvertFromJoule(differenceJoulesPerMole, EnergyUnit.Cal);
            maximumDifference = Math.Max(maximumDifference, differenceCalPerMole);
        }

        // A floating-point-only difference would be around 1e-11 cal/mol. The
        // observed ~0.126 cal/mol is the documented analytic-average versus
        // trapezoidal finite-injection mismatch, so this is deliberately not a
        // forward-model acceptance assertion.
        Assert.InRange(maximumDifference, minimumCalPerMole, maximumCalPerMole);
    }

    static double MacroscopicKd2Micromolar => KdA2Micromolar + KdB2Micromolar;

    static double MacroscopicKd1Micromolar =>
        KdA1Micromolar * KdB2Micromolar / MacroscopicKd2Micromolar;

    static double MacroscopicDh1KcalPerMole =>
        (DhA1KcalPerMole * KdB2Micromolar + DhB1KcalPerMole * KdA2Micromolar)
        / MacroscopicKd2Micromolar;

    static double MacroscopicDh2KcalPerMole =>
        DhA1KcalPerMole + DhB2KcalPerMole - MacroscopicDh1KcalPerMole;

    static SequentialBindingSites ConfigureModel(ExperimentData experiment, double offsetKcalPerMole)
    {
        var model = new SequentialBindingSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.AddOrUpdateParameter(
            ParameterType.Affinity1, Math.Log10(1e6 / MacroscopicKd1Micromolar));
        model.Parameters.AddOrUpdateParameter(
            ParameterType.Affinity2, Math.Log10(1e6 / MacroscopicKd2Micromolar));
        model.Parameters.AddOrUpdateParameter(
            ParameterType.Enthalpy1, KcalPerMoleToJoulesPerMole(MacroscopicDh1KcalPerMole));
        model.Parameters.AddOrUpdateParameter(
            ParameterType.Enthalpy2, KcalPerMoleToJoulesPerMole(MacroscopicDh2KcalPerMole));
        model.Parameters.AddOrUpdateParameter(
            ParameterType.Offset, KcalPerMoleToJoulesPerMole(offsetKcalPerMole));
        return model;
    }

    static double KcalPerMoleToJoulesPerMole(double value) =>
        Energy.ConvertToJoule(value * 1000.0, EnergyUnit.Cal);

    static ExperimentData LoadExperiment(int run) => IntegratedHeatReader.ReadFile(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "PublishedBenchmarks",
        "nature2022-can-wt-sequential",
        $"can-wt-preq1-{run}-reference-predicted.dh"));

    sealed class FixedEnergyUnitPromptService : IImportPromptService
    {
        readonly EnergyUnit unit;

        public FixedEnergyUnitPromptService(EnergyUnit unit) => this.unit = unit;

        public EnergyUnitPromptResult AskForEnergyUnit(string filePath, string encounteredValue, bool allowQueueReuse) =>
            new(unit, useForRemainingFilesInQueue: false, isCancelled: false);
    }
}
