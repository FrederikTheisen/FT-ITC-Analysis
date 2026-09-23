using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using Xunit;
using Xunit.Abstractions;

namespace AnalysisITC.Core.Tests;

[Collection("Solver events")]
public sealed class DumasInjectionHeatTests : IDisposable
{
    readonly PreferencesState original = PreferencesState.FromSettings();
    readonly ITestOutputHelper output;
    internal static readonly ParameterType[] Affinities = { ParameterType.Affinity1, ParameterType.Affinity2, ParameterType.Affinity3, ParameterType.Affinity4 };
    internal static readonly ParameterType[] Enthalpies = { ParameterType.Enthalpy1, ParameterType.Enthalpy2, ParameterType.Enthalpy3, ParameterType.Enthalpy4 };

    public DumasInjectionHeatTests(ITestOutputHelper output)
    {
        this.output = output;
        PreferencesState.Defaults().ApplyToSettings();
    }
    public void Dispose() => original.ApplyToSettings();

    public static IEnumerable<object[]> Cases() => ReadCases().Select(c => new object[] { c.GetProperty("id").GetString() });
    internal static JsonElement[] ReadCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "ScientificValidation", "DumasBookkeeping", "reference.json")));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToArray();
    }
    internal static JsonElement Reference(string id) => ReadCases().Single(c => c.GetProperty("id").GetString() == id);
    internal static double[] Numbers(JsonElement item, string property) => item.GetProperty(property).EnumerateArray().Select(v => v.GetDouble()).ToArray();

    [Theory]
    [MemberData(nameof(Cases))]
    public void AgreesWithIndependentContinuousMixingReference(string id)
    {
        var reference = Reference(id);
        var model = CreateModel(reference);
        var expected = Numbers(reference, "heats_joules");
        var peak = expected.Max(Math.Abs);
        var actual = model.Data.Injections.Select(i => model.Evaluate(i.ID)).ToArray();
        var error = actual.Zip(expected, (a, e) => Math.Abs(a - e)).Max() / peak;
        output.WriteLine($"{id}: maximum error / peak = {error:G9}");
        Assert.InRange(reference.GetProperty("reference_refinement_fraction_of_peak").GetDouble(), 0, 1e-8);
        if (!reference.TryGetProperty("stress_only", out _)) Assert.InRange(error, 0, 0.001);
        Assert.All(actual, heat => Assert.True(double.IsFinite(heat)));

        // No evolving heat/concentration state: reverse and excluded-shot evaluation is identical.
        model.Data.Injections[1].Include = false;
        AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
        foreach (var i in Enumerable.Range(0, expected.Length).Reverse()) Assert.Equal(actual[i], model.Evaluate(i));
        var random = new Random(31);
        foreach (var i in Enumerable.Range(0, expected.Length).OrderBy(_ => random.Next()))
            Assert.Equal(actual[i], model.Evaluate(i));
    }

    [Fact]
    public void SimpsonIntegratesACubicPathWithExactlyThreeContentEvaluations()
    {
        const double u = 0.3;
        var calls = 0;
        var heat = InjectionHeatCalculator.Dumas(1, u, 0,
            new InjectionConcentrationState(1, 0), new InjectionConcentrationState(Math.Exp(-u), 0),
            (cell, _) => { calls++; return Math.Pow(-Math.Log(cell), 3); });
        // Q(x)=x^3; q=Q(u)-Q(0)+integral_0^u Q(x) dx.
        Assert.Equal(Math.Pow(u, 3) + Math.Pow(u, 4) / 4, heat, 14);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void ConstantCellAndIncomingHeatContentConserveEnergy()
    {
        var state = new InjectionConcentrationState(0, 2);
        Assert.Equal(0, InjectionHeatCalculator.Dumas(4, 0.2, 2, state, state, (_, _) => 12, 3), 14);
        Assert.Throws<ArgumentOutOfRangeException>(() => InjectionHeatCalculator.Dumas(0, 1, 2, state, state, (_, _) => 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => InjectionHeatCalculator.Dumas(4, -1, 2, state, state, (_, _) => 0));
    }

    [Theory]
    [InlineData("one-c100-v0.01")]
    [InlineData("one-endothermic")]
    [InlineData("two-site")]
    [InlineData("competitive")]
    [InlineData("dissociation")]
    [InlineData("sequential-2")]
    public void RecoversParametersFromIndependentIdentifiableData(string id)
    {
        var model = CreateModel(Reference(id));
        var expected = model.Parameters.Table.ToDictionary(p => p.Key, p => p.Value.Value);
        foreach (var parameter in model.Parameters.Table.Values)
        {
            if (parameter.Key == ParameterType.Offset) parameter.Update(expected[parameter.Key], true);
            else parameter.Update(parameter.Value * (Affinities.Contains(parameter.Key) ? 1.01 : 0.95));
        }
        var solver = new Solver
        {
            Model = model, SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
            ErrorEstimationMethod = ErrorEstimationMethod.None, UseErrorWeightedFitting = true,
            MaxOptimizerIterations = 20000, Silent = true,
        };
        var convergence = solver.Solve();
        Assert.True(convergence.Success, convergence.Message);
        foreach (var parameter in model.Parameters.Table.Values.Where(p => !p.IsLocked))
        {
            var truth = expected[parameter.Key];
            var relative = Affinities.Contains(parameter.Key)
                ? Math.Abs(Math.Pow(10, parameter.Value - truth) - 1)
                : Math.Abs(parameter.Value / truth - 1);
            output.WriteLine($"{id}/{parameter.Key}: truth={truth:G9}, fitted={parameter.Value:G9}, relative error={relative:G6}");
            Assert.InRange(relative, 0, 0.02);
        }
    }

    [Theory]
    [InlineData("one-c100-v0.01")]
    [InlineData("two-site")]
    [InlineData("sequential-4")]
    [Trait("Category", "Performance")]
    public void ReportsFixedSimpsonEvaluationCost(string id)
    {
        var model = CreateModel(Reference(id));
        var parameters = model.Parameters.GetFittedParameterArray();
        double Measure(InjectionHeatMethod method)
        {
            model.HeatMethod = method;
            for (var warm = 0; warm < 20; warm++) model.LossFunction(parameters, false);
            var samples = new List<double>();
            for (var trial = 0; trial < 7; trial++)
            {
                var timer = Stopwatch.StartNew();
                for (var repeat = 0; repeat < 100; repeat++) model.LossFunction(parameters, false);
                samples.Add(timer.Elapsed.TotalMilliseconds / 100);
            }
            return samples.OrderBy(v => v).ElementAt(3);
        }
        var legacy = Measure(InjectionHeatMethod.MicroCal);
        var dumas = Measure(InjectionHeatMethod.IdealContinuousMixing);
        output.WriteLine($"{id}: legacy exponential {legacy:G6} ms/objective; Dumas {dumas:G6} ms/objective; ratio {dumas / legacy:G4}");
        Assert.True(double.IsFinite(dumas)); // Timing is reported, not a flaky CI threshold.
    }

    [Theory]
    [InlineData("one-c100-v0.01")]
    [InlineData("two-site")]
    [Trait("Category", "Performance")]
    public void ReportsRepresentativeEndToEndFitCost(string id)
    {
        double Fit(InjectionHeatMethod method)
        {
            // Same independent observations, starts, concentration law and settings.
            // Solver paths may differ because the two heat models are not identical.
            var model = CreateModel(Reference(id));
            model.HeatMethod = model.Data.HeatMethod = method;
            foreach (var parameter in model.Parameters.Table.Values)
            {
                if (parameter.Key == ParameterType.Offset) parameter.Update(parameter.Value, true);
                else parameter.Update(parameter.Value * (Affinities.Contains(parameter.Key) ? 1.01 : 0.95));
            }
            var solver = new Solver
            {
                Model = model, SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
                ErrorEstimationMethod = ErrorEstimationMethod.None, UseErrorWeightedFitting = true,
                MaxOptimizerIterations = 20000, Silent = true,
            };
            var timer = Stopwatch.StartNew();
            var convergence = solver.Solve();
            timer.Stop();
            Assert.True(convergence.Success, convergence.Message);
            return timer.Elapsed.TotalMilliseconds;
        }
        Fit(InjectionHeatMethod.MicroCal);
        Fit(InjectionHeatMethod.IdealContinuousMixing);
        var legacy = new List<double>();
        var dumas = new List<double>();
        for (var repeat = 0; repeat < 5; repeat++)
        {
            legacy.Add(Fit(InjectionHeatMethod.MicroCal));
            dumas.Add(Fit(InjectionHeatMethod.IdealContinuousMixing));
        }
        var legacyMedian = legacy.OrderBy(v => v).ElementAt(2);
        var dumasMedian = dumas.OrderBy(v => v).ElementAt(2);
        output.WriteLine($"{id}: legacy exponential {legacyMedian:G6} ms/fit; Dumas {dumasMedian:G6} ms/fit; ratio {dumasMedian / legacyMedian:G4}");
    }

    internal static Model CreateModel(JsonElement reference)
    {
        var data = new ExperimentData(reference.GetProperty("id").GetString())
        {
            CellVolume = reference.GetProperty("cell_liters").GetDouble(),
            CellConcentration = new(reference.GetProperty("cell_molar").GetDouble()),
            SyringeConcentration = new(reference.GetProperty("syringe_molar").GetDouble()),
            MeasuredTemperature = 25,
        };
        var volumes = Numbers(reference, "injection_liters");
        var heats = Numbers(reference, "heats_joules");
        for (var i = 0; i < volumes.Length; i++)
        {
            var injection = new InjectionData(data, i, volumes[i], volumes[i] * data.SyringeConcentration.Value, true);
            injection.SetPeakArea(new FloatWithError(heats[i], 1e-9));
            data.Injections.Add(injection);
        }
        RawDataReader.ProcessInjections(data, DilutionMethod.Exponential);
        if (reference.TryGetProperty("segments", out var segments))
        {
            foreach (var segment in segments.EnumerateArray())
                data.AddSegment(new TandemExperimentSegment(segment.GetProperty("first_injection").GetInt32(),
                    segment.GetProperty("cell_molar").GetDouble(), segment.GetProperty("titrant_molar").GetDouble()));
            var before = new InjectionConcentrationState(data.CellConcentration, 0);
            foreach (var injection in data.Injections)
            {
                var start = data.Segments.FirstOrDefault(s => s.FirstInjectionID == injection.ID);
                if (start != null) before = new InjectionConcentrationState(start.SegmentInitialActiveCellConc, start.SegmentInitialActiveTitrantConc);
                before = InjectionDisplacementCalculator.AdvanceState(DilutionMethod.Exponential, data.CellVolume,
                    data.SyringeConcentration, before, 0, injection.Volume);
                InjectionDisplacementCalculator.ApplyToInjection(data, injection, before);
            }
        }
        Model model = reference.GetProperty("model").GetString() switch
        {
            "one-site" => new OneSetOfSites(data), "two-site" => new TwoSetsOfSites(data),
            "competitive" => new CompetitiveBinding(data), "sequential" => new SequentialBindingSites(data),
            "dissociation" => new Dissociation(data), _ => throw new InvalidOperationException(),
        };
        model.InitializeParameters(data);
        var parameters = reference.GetProperty("parameters");
        var logs = Numbers(parameters, "logka");
        var hs = Numbers(parameters, "enthalpy");
        if (model is SequentialBindingSites)
        {
            model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = logs.Length;
            model.ApplyModelOptions();
        }
        for (var i = 0; i < logs.Length; i++)
        {
            model.Parameters.Table[Affinities[i]].Update(logs[i]);
            model.Parameters.Table[Enthalpies[i]].Update(hs[i]);
        }
        var ns = Numbers(parameters, "n");
        for (var i = 0; i < ns.Length; i++) model.Parameters.Table[i == 0 ? ParameterType.Nvalue1 : ParameterType.Nvalue2].Update(ns[i]);
        model.Parameters.Table[ParameterType.Offset].Update(parameters.GetProperty("offset").GetDouble());
        if (reference.TryGetProperty("competitor", out var competitor))
        {
            model.ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue = new(competitor.GetProperty("concentration").GetDouble());
            model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue = new(competitor.GetProperty("logka").GetDouble());
            model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue = new(competitor.GetProperty("enthalpy").GetDouble());
        }
        data.Model = model;
        return model;
    }
}
