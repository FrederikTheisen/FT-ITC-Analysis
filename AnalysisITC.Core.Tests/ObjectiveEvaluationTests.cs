using System;
using System.Collections.Generic;
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
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Solver events")]
public sealed partial class ObjectiveEvaluationTests : IDisposable
{
    readonly PreferencesState original = PreferencesState.FromSettings();

    public ObjectiveEvaluationTests()
    {
        PreferencesState.Defaults().ApplyToSettings();
        SolverInterface.TerminateAnalysisFlag.Lower();
        IntegratedHeatReader.BeginImportQueue();
        PlatformServices.RegisterImportPromptService(new MicrocalPrompt());
    }

    public void Dispose()
    {
        SolverInterface.TerminateAnalysisFlag.Lower();
        IntegratedHeatReader.EndImportQueue();
        PlatformServices.RegisterImportPromptService(null);
        original.ApplyToSettings();
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (var family in new[] { "PytcBookkeeping", "TwoSiteStoichiometry", "DumasBookkeeping" })
        foreach (var sample in References(family))
        foreach (var method in new[] { DilutionMethod.MicroCal, DilutionMethod.Exponential, DilutionMethod.DiscreteDisplacement })
            yield return new object[] { family, sample.GetProperty("id").GetString(), method };
    }

    static JsonElement[] References(string family) => family switch
    {
        "PytcBookkeeping" => PytcExternalReferenceTests.References(),
        "TwoSiteStoichiometry" => TwoSiteStoichiometryForwardTests.References(),
        "DumasBookkeeping" => DumasInjectionHeatTests.ReadCases(),
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    internal static Model CreateModel(string family, string id)
    {
        var sample = References(family).Single(c => c.GetProperty("id").GetString() == id);
        return family switch
        {
            "PytcBookkeeping" => PytcExternalReferenceTests.CreateModel(sample),
            "TwoSiteStoichiometry" => TwoSiteStoichiometryForwardTests.CreateModel(sample),
            "DumasBookkeeping" => DumasInjectionHeatTests.CreateModel(sample),
            _ => throw new ArgumentOutOfRangeException(nameof(family)),
        };
    }

    internal static void SetMethod(Model model, DilutionMethod method)
    {
        var data = model.Data;
        if (!data.IsTandemExperiment)
            RawDataReader.ProcessInjections(data, method);
        else if (data.AppliedDilutionMethod != method)
        {
            var initial = new InjectionConcentrationState(data.CellConcentration, 0);
            var current = initial;
            var retention = 1.0;
            var volume = 0.0;
            foreach (var injection in data.Injections)
            {
                var segment = data.Segments.FirstOrDefault(s => s.FirstInjectionID == injection.ID);
                if (segment != null)
                {
                    initial = new InjectionConcentrationState(segment.SegmentInitialActiveCellConc, segment.SegmentInitialActiveTitrantConc);
                    current = initial;
                    retention = 1.0;
                    volume = 0.0;
                }
                if (method == DilutionMethod.DiscreteDisplacement)
                {
                    retention *= InjectionDisplacementCalculator.DiscreteDisplacementRetention(data.CellVolume, injection.Volume);
                    current = InjectionDisplacementCalculator.DiscreteDisplacementState(initial, data.SyringeConcentration, retention);
                }
                else
                    current = InjectionDisplacementCalculator.AdvanceState(method, data.CellVolume,
                        data.SyringeConcentration, current, volume, injection.Volume);
                volume += injection.Volume;
                InjectionDisplacementCalculator.ApplyToInjection(data, injection, current);
            }
            data.AppliedDilutionMethod = method;
        }
        data.HeatMethod = InjectionBookkeeping.HeatMethodFor(method);
        model.HeatMethod = data.HeatMethod;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ObjectivesExactlyMatchUncachedPredictionsAcrossReferenceFixtures(string family, string id, DilutionMethod method)
    {
        var model = CreateModel(family, id);
        SetMethod(model, method);
        model.ApplyModelOptions();
        var heats = model.Data.Injections.Select(i => model.Evaluate(i.ID)).ToArray();
        var residuals = AssertObjectiveParity(model, false);
        var weightedResiduals = AssertObjectiveParity(model, true);
        // This is a regression snapshot, never an independently generated scientific reference.
        var snapshot = new { Heats = Bits(heats), Residuals = Bits(residuals), WeightedResiduals = Bits(weightedResiduals) };
        var file = family + "-" + id + "-" + method + ".json";
        var json = JsonSerializer.Serialize(snapshot);
        var baseline = Environment.GetEnvironmentVariable("FTITC_OBJECTIVE_BASELINE");
        if (!string.IsNullOrWhiteSpace(baseline)) Assert.Equal(File.ReadAllText(Path.Combine(baseline, file)), json);
        var destination = Environment.GetEnvironmentVariable("FTITC_OBJECTIVE_RESULTS");
        if (!string.IsNullOrWhiteSpace(destination))
        {
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, file), json);
        }
    }

    static long[] Bits(IEnumerable<double> values) => values.Select(BitConverter.DoubleToInt64Bits).ToArray();

    internal static double[] AssertObjectiveParity(Model model, bool weighted)
    {
        model.ApplyModelOptions();
        var included = model.Data.Injections.Where(i => i.Include).ToArray();
        var expected = included.Select(i =>
        {
            double residual = i.PeakArea - model.Evaluate(i.ID);
            if (weighted) residual /= Model.GetSigmaForWeighting(i, included);
            return residual;
        }).ToArray();
        Assert.All(expected, r => Assert.True(double.IsFinite(r)));
        var expectedLoss = 0.0;
        foreach (var residual in expected) expectedLoss += residual * residual;
        var parameters = model.Parameters.GetFittedParameterArray();
        Assert.Equal(BitConverter.DoubleToInt64Bits(expectedLoss), BitConverter.DoubleToInt64Bits(model.LossFunction(parameters, weighted)));
        Assert.True(model.TryLossFunction(parameters, weighted, out var loss));
        Assert.Equal(BitConverter.DoubleToInt64Bits(expectedLoss), BitConverter.DoubleToInt64Bits(loss));
        Assert.Equal(Bits(expected), Bits(model.LossFunctionResiduals(parameters, weighted)));
        Assert.True(model.TryLossFunctionResiduals(parameters, weighted, out var residuals));
        Assert.Equal(Bits(expected), Bits(residuals));
        return expected;
    }

    sealed class MicrocalPrompt : IImportPromptService
    {
        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse)
            => new(EnergyUnit.MicroCal, useForRemainingFilesInQueue: false, isCancelled: false);
    }
}
