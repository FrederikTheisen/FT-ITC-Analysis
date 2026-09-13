using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace AnalysisITC.Core.Tests;

[Collection("Published model reproduction")]
public sealed class SequentialScientificReferenceTests : IDisposable
{
    readonly PreferencesState original = PreferencesState.FromSettings();
    readonly ITestOutputHelper output;
    public SequentialScientificReferenceTests(ITestOutputHelper output)
    {
        this.output = output;
        PreferencesState.Defaults().ApplyToSettings();
        AppSettings.OptimizerTolerance = 1;
    }

    public void Dispose() => original.ApplyToSettings();

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void StepwiseHeatsMatchIndependentDecimalReference(int count)
    {
        using var reference = ReadReference();
        var sample = Case(reference, count);
        var model = CreateModel(sample);
        SetParameters(model, sample, perturb: 0);
        var rows = sample.GetProperty("injections").EnumerateArray().ToArray();
        foreach (var injection in model.Data.Injections)
        {
            var expected = rows[injection.ID].GetProperty("heat_joules").GetDouble();
            // Independent 55-digit generator vs binary64 production evaluation;
            // 2e-12 J absolute permits cancellation near sign-changing heats.
            Assert.InRange(Math.Abs(model.Evaluate(injection.ID) - expected), 0, 2e-12);
        }
    }

    [Theory]
    [InlineData(2, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData(2, SolverAlgorithm.NelderMead)]
    [InlineData(3, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData(3, SolverAlgorithm.NelderMead)]
    [InlineData(4, SolverAlgorithm.LevenbergMarquardt)]
    [InlineData(4, SolverAlgorithm.NelderMead)]
    public void IndependentHigherStepHeatsRecoverAllAffinityAndEnthalpyCoordinates(int count, SolverAlgorithm algorithm)
    {
        using var reference = ReadReference();
        var sample = Case(reference, count);
        var model = CreateModel(sample);
        // All 2*count scientific coordinates are free; only the declared zero
        // dilution offset is fixed. Starts alternate +/-0.08 log10 Ka and
        // +/-8% dH. No production prediction generates any input heat.
        SetParameters(model, sample, perturb: .08);
        Assert.Equal(2 * count, model.Parameters.GetFittedParameters().Length);
        var convergence = new Solver
        {
            Model = model, SolverAlgorithm = algorithm,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = true, MaxOptimizerIterations = 20000, Silent = true,
            // Noiseless coupled higher-step fits need a stricter NM stopping
            // criterion than the interactive default. Recovery tolerances
            // stay fixed; this is numerical reference qualification.
            SolverToleranceModifier = algorithm == SolverAlgorithm.NelderMead ? 1e-4 : 1,
        }.Solve();
        Assert.True(convergence.Success, convergence.Message);
        var logs = sample.GetProperty("log10_ka").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var hs = sample.GetProperty("enthalpy_joules_per_mole").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        output.WriteLine($"{count} steps/{algorithm}: " + string.Join(", ", model.Parameters.Table.Select(x => $"{x.Key}={x.Value.Value:G12}")));
        foreach (var slot in ThermodynamicParameterSlots.Active(count))
        {
            // Predetermined recovery tolerance: 0.01 log10 Ka (~2.3%) and
            // 1% dH, allowing correlated nonlinear optimizer termination.
            Assert.InRange(Math.Abs(model.Parameters.Table[slot.Affinity].Value - logs[slot.Index - 1]), 0, .01);
            Assert.InRange(Math.Abs(model.Parameters.Table[slot.Enthalpy].Value / hs[slot.Index - 1] - 1), 0, .01);
        }
    }

    static JsonDocument ReadReference() => JsonDocument.Parse(File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "ScientificValidation", "Sequential", "reference.json")));

    static JsonElement Case(JsonDocument reference, int count) => reference.RootElement.GetProperty("cases")
        .EnumerateArray().Single(x => x.GetProperty("step_count").GetInt32() == count);

    static SequentialBindingSites CreateModel(JsonElement sample)
    {
        var data = new ExperimentData("independent-sequential-reference")
        {
            CellConcentration = new FloatWithError(sample.GetProperty("cell_molar").GetDouble()),
            SyringeConcentration = new FloatWithError(sample.GetProperty("syringe_molar").GetDouble()),
            CellVolume = sample.GetProperty("cell_liters").GetDouble(), MeasuredTemperature = 25, TargetTemperature = 25,
        };
        foreach (var row in sample.GetProperty("injections").EnumerateArray())
        {
            var id = row.GetProperty("id").GetInt32();
            var volume = row.GetProperty("volume_liters").GetDouble();
            var injection = new InjectionData(data, id, volume, data.SyringeConcentration * volume, include: true)
            {
                ActualCellConcentration = row.GetProperty("cell_molar").GetDouble(),
                ActualTitrantConcentration = row.GetProperty("ligand_molar").GetDouble(),
                Ratio = row.GetProperty("ligand_molar").GetDouble() / row.GetProperty("cell_molar").GetDouble(),
            };
            // Uniform positive SD only rescales the objective. Its optimum
            // is identical to unweighted fitting of this noiseless reference.
            injection.SetPeakArea(new FloatWithError(row.GetProperty("heat_joules").GetDouble(), 1e-10));
            data.Injections.Add(injection);
        }
        var model = new SequentialBindingSites(data);
        model.InitializeParameters(data);
        model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = sample.GetProperty("step_count").GetInt32();
        model.ApplyModelOptions();
        return model;
    }

    static void SetParameters(SequentialBindingSites model, JsonElement sample, double perturb)
    {
        var logs = sample.GetProperty("log10_ka").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var hs = sample.GetProperty("enthalpy_joules_per_mole").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        foreach (var slot in ThermodynamicParameterSlots.Active(model.SiteCount))
        {
            var direction = slot.Index % 2 == 0 ? -1 : 1;
            model.Parameters.Table[slot.Affinity].Update(logs[slot.Index - 1] + direction * perturb);
            model.Parameters.Table[slot.Enthalpy].Update(hs[slot.Index - 1] * (1 - direction * perturb));
        }
        model.Parameters.Table[ParameterType.Offset].Update(0, true);
    }
}
