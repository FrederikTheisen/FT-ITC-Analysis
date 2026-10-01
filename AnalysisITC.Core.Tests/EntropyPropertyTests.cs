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
        var injection = new InjectionData(data, 0, 2e-6, 2e-9, true) { Ratio = 10 };
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
                + 8.3145 * Math.Log(10) * (6 + 0.25 * step);
            Assert.Equal(expected, entropy.Value, 10);
            // Only ΔH is uncertain: its 298.15 J/mol SD gives 1 J/(mol K).
            Assert.Equal(1, entropy.FloatWithError.SD, 12);
        }
    }
}
