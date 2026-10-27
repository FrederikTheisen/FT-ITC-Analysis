using System;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class EntropyPropertyTests
{
    [Fact]
    public void SolutionDerivesGibbsAndEntropyWithIndependentUncertaintyPropagation()
    {
        var data = new ExperimentData("thermodynamic-reference.itc")
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(1e-3),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };
        var injection = new InjectionData(data, 0, 2e-6, 2e-9, true)
        {
            Ratio = 10,
            ActualCellConcentration = 10e-6,
            ActualTitrantConcentration = 100e-6,
        };
        injection.SetPeakArea(new FloatWithError(-1e-6));
        data.Injections.Add(injection);
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        var solution = (OneSetOfSites.ModelSolution)SolutionInterface.FromModel(model, null);
        const double log10Ka = 7.25;
        const double log10KaSd = 0.03;
        const double enthalpy = -30000;
        const double enthalpySd = 100;
        solution.Parameters[ParameterType.Affinity1] = new FloatWithError(log10Ka, log10KaSd);
        solution.Parameters[ParameterType.Enthalpy1] = new FloatWithError(enthalpy, enthalpySd);

        const double gasConstant = 8.31446261815324;
        const double temperatureK = 298.15;
        // Independent reference equations: G = -R*T*ln(10)*log10Ka and
        // sigmaG = R*T*ln(10)*sigmaLog10Ka.
        var expectedGibbs = -gasConstant * temperatureK * Math.Log(10.0) * log10Ka;
        var expectedGibbsSd = gasConstant * temperatureK * Math.Log(10.0) * log10KaSd;
        var expectedEntropy = enthalpy / temperatureK + gasConstant * Math.Log(10.0) * log10Ka;
        var expectedEntropySd = Math.Sqrt(Math.Pow(enthalpySd / temperatureK, 2)
            + Math.Pow(gasConstant * Math.Log(10.0) * log10KaSd, 2));

        AssertClose(expectedGibbs, solution.GibbsFreeEnergy.Value);
        AssertClose(expectedGibbsSd, solution.GibbsFreeEnergy.SD);
        AssertClose(expectedEntropy, solution.Entropy.Value);
        AssertClose(expectedEntropySd, solution.Entropy.SD);
    }

    static void AssertClose(double expected, double actual)
    {
        if (expected == 0)
        {
            Assert.Equal(0, actual);
            return;
        }

        Assert.InRange(Math.Abs(actual - expected) / Math.Abs(expected), 0, 2e-15);
    }

    [Theory]
    [InlineData(AnalysisModel.OneSetOfSites, -10000)]
    [InlineData(AnalysisModel.OneSetOfSites, -50000)]
    [InlineData(AnalysisModel.TwoSetsOfSites, -10000)]
    [InlineData(AnalysisModel.TwoSetsOfSites, -50000)]
    [InlineData(AnalysisModel.CompetitiveBinding, -10000)]
    [InlineData(AnalysisModel.CompetitiveBinding, -50000)]
    [InlineData(AnalysisModel.Dissociation, -10000)]
    [InlineData(AnalysisModel.Dissociation, -50000)]
    [InlineData(AnalysisModel.SequentialBindingSites, -10000)]
    [InlineData(AnalysisModel.SequentialBindingSites, -50000)]
    public void EntropyHasThermodynamicSignAndRetainsUncertainty(AnalysisModel type, double enthalpy)
    {
        var data = new ExperimentData("entropy.itc")
        {
            CellConcentration = new FloatWithError(10e-6),
            SyringeConcentration = new FloatWithError(1e-3),
            CellVolume = 1.4e-3,
            MeasuredTemperature = 25,
            TargetTemperature = 25,
        };
        var injection = new InjectionData(data, 0, 2e-6, 2e-9, true)
        {
            Ratio = 10,
            ActualCellConcentration = 10e-6,
            ActualTitrantConcentration = 100e-6,
        };
        injection.SetPeakArea(new FloatWithError(-1e-6));
        data.Injections.Add(injection);

        Model model = type switch
        {
            AnalysisModel.OneSetOfSites => new OneSetOfSites(data),
            AnalysisModel.TwoSetsOfSites => new TwoSetsOfSites(data),
            AnalysisModel.CompetitiveBinding => new CompetitiveBinding(data),
            AnalysisModel.Dissociation => new Dissociation(data),
            _ => new SequentialBindingSites(data),
        };
        model.InitializeParameters(data);
        if (model is SequentialBindingSites sequential)
        {
            sequential.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = 4;
            sequential.ApplyModelOptions();
        }

        var count = model is SequentialBindingSites ? 4 : model is TwoSetsOfSites ? 2 : 1;
        for (int step = 1; step <= count; step++)
        {
            var slot = ThermodynamicParameterSlots.ForStep(step);
            model.Parameters.Table[slot.Enthalpy].Update(enthalpy + 1000 * step);
            model.Parameters.Table[slot.Affinity].Update(6 + 0.25 * step);
        }
        var solution = SolutionInterface.FromModel(model, null);
        for (int step = 1; step <= count; step++)
        {
            var slot = ThermodynamicParameterSlots.ForStep(step);
            solution.Parameters[slot.Enthalpy] = new FloatWithError(enthalpy + 1000 * step, 298.15);
            Energy entropy = solution switch
            {
                OneSetOfSites.ModelSolution one => one.Entropy,
                TwoSetsOfSites.ModelSolution two => step == 1 ? two.Entropy1 : two.Entropy2,
                CompetitiveBinding.ModelSolution competitive => competitive.Entropy,
                Dissociation.ModelSolution dissociation => dissociation.Entropy,
                SequentialBindingSites.ModelSolution sites => sites.Entropy(step),
                _ => throw new InvalidOperationException(),
            };

            // Independent identity: ΔS = ΔH/T + R ln K, using the
            // application's stated gas constant and exact affinity inputs.
            var expected = (enthalpy + 1000 * step) / 298.15
                + 8.31446261815324 * Math.Log(10.0) * (6 + 0.25 * step);
            Assert.Equal(expected, entropy.Value, 10);
            // Only ΔH is uncertain: its 298.15 J/mol SD gives 1 J/(mol K).
            Assert.Equal(1, entropy.FloatWithError.SD, 12);
        }
    }
}
