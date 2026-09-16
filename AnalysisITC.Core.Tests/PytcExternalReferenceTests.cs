using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;
using Xunit.Abstractions;

namespace AnalysisITC.Core.Tests;

[Collection("Solver events")]
public sealed class PytcExternalReferenceTests : IDisposable
{
    const double Roundoff = 512 * 2.220446049250313e-16;
    const double PracticalErrorFractionOfPeak = 0.0001; // User-selected 0.01%; roundoff remains a diagnostic goal.
    static readonly string DirectoryPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ScientificValidation", "PytcBookkeeping");
    readonly PreferencesState original = PreferencesState.FromSettings();
    readonly ITestOutputHelper output;
    public PytcExternalReferenceTests(ITestOutputHelper output)
    {
        this.output = output;
        PreferencesState.Defaults().ApplyToSettings();
        AppSettings.DilutionCalculationMethod = DilutionMethod.Exponential; // The explicit import selection must win.
        IntegratedHeatReader.BeginImportQueue();
        PlatformServices.RegisterImportPromptService(new MicrocalPrompt());
    }
    public void Dispose()
    {
        IntegratedHeatReader.EndImportQueue();
        PlatformServices.RegisterImportPromptService(null);
        original.ApplyToSettings();
    }

    static JsonElement[] References()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(DirectoryPath, "reference.json")));
        Assert.Equal("d9ccde3f04e35a3d821ff37a4ad42e62a048d4ac", json.RootElement.GetProperty("source").GetProperty("commit").GetString());
        return json.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToArray();
    }
    public static IEnumerable<object[]> Cases() => References()
        .Select(c => new object[] { c.GetProperty("id").GetString() });

    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeHeatsAgreeWithinPracticalTolerance(string id)
    {
        var result = Compare(Reference(id));
        Record(id, result);
        if (id.StartsWith("two-realistic", StringComparison.Ordinal)
            || id == "two-kd50-50"
            || id is "two-tight-scale-2x" or "two-tight-scale-10x"
            || id == "two-c2000-r10"
            || id is "sequential-2" or "sequential-3" or "sequential-4")
        {
            // These deliberately retained stress cases document known solver
            // discrepancies across the tight-to-moderate affinity range; they
            // must not be converted into practical passes.
            Assert.False(result.Passed, "The requested two-site diagnostic unexpectedly met the practical limit.");
            return;
        }
        Assert.True(result.Passed, $"{id}: error/peak={result.ErrorFractionOfPeak:G17}; exceeds the 0.01% practical limit.");
    }

    [Fact]
    public void ReportsEveryNativeComparisonWithSeparateAcceptanceAndRoundoffGoal()
    {
        var results = References().Select(Compare).ToArray();
        Record("comparisons", new { PracticalErrorFractionOfPeak, RoundoffMultiplier = 512, Epsilon = 2.220446049250313e-16,
            RoundoffGoal = "abs(error) <= 512*epsilon*(peak_abs_heat + abs(expected))",
            ReferenceSha256 = Hash(Path.Combine(DirectoryPath, "reference.json")), Results = results });
        Assert.Equal(38, results.Length);
        // Machine-precision agreement is a diagnostic goal, not an acceptance requirement.
        Assert.All(results, result => Assert.True(double.IsFinite(result.ErrorFractionOfPeak)));
    }

    [Fact]
    public void NativeTrajectoryImportInfersDiscreteMetadataWithoutUsingPreferences()
    {
        var sample = Reference("one-c10-small");
        var path = Path.Combine(DirectoryPath, sample.GetProperty("trajectory_file").GetString());
        AssertHash(path, sample.GetProperty("trajectory_sha256").GetString());
        var data = IntegratedHeatReader.ReadFile(path, true, DilutionMethod.DiscreteDisplacement, true);
        Assert.Equal(DilutionMethod.DiscreteDisplacement, data.AppliedDilutionMethod);
        Assert.Equal(InjectionHeatMethod.DiscreteDisplacement, data.HeatMethod);
        Assert.InRange(Math.Abs(data.CellVolume/200e-6-1), 0, 1e-12);
        var syringe = sample.GetProperty("syringe_molar").GetDouble();
        Assert.InRange(Math.Abs(data.SyringeConcentration.Value/syringe-1), 0, 1e-12);
        var heats = Numbers(sample, "heats_joules");
        foreach (var injection in data.Injections)
            Assert.InRange(Math.Abs(injection.PeakArea.Value-heats[injection.ID]), 0, 1e-17);
        Assert.Empty(data.DataPoints);
    }

    Comparison Compare(JsonElement sample)
    {
        var model = CreateModel(sample);
        var expected = Numbers(sample, "heats_joules");
        var actual = model.Data.Injections.Select(i => model.Evaluate(i.ID, false)).ToArray();
        var peak = expected.Max(Math.Abs);
        var errors = actual.Zip(expected, (a, e) => Math.Abs(a-e)).ToArray();
        var roundoffGoalMet = errors.Select((error, i) => error <= Roundoff*(peak+Math.Abs(expected[i]))).All(x => x);
        var pass = errors.Max() <= PracticalErrorFractionOfPeak*peak;
        var cells = Numbers(sample, "cell_concentrations_molar");
        var ligands = Numbers(sample, "titrant_concentrations_molar");
        foreach (var injection in model.Data.Injections)
        {
            Assert.InRange(Math.Abs(injection.ActualCellConcentration-cells[injection.ID+1]), 0, Roundoff*cells.Max());
            Assert.InRange(Math.Abs(injection.ActualTitrantConcentration-ligands[injection.ID+1]), 0, Roundoff*ligands.Max());
        }
        return new Comparison(sample.GetProperty("id").GetString(), pass, roundoffGoalMet, errors.Max(), peak, errors.Max()/peak,
            model.GetType().Name, sample.GetProperty("sha256").GetString(), expected, actual);
    }

    internal static Model CreateModel(JsonElement sample)
    {
        var path = Path.Combine(DirectoryPath, sample.GetProperty("file").GetString());
        AssertHash(path, sample.GetProperty("sha256").GetString());
        var data = IntegratedHeatReader.ReadFile(path, true, DilutionMethod.DiscreteDisplacement, true);
        Assert.Equal(InjectionHeatMethod.DiscreteDisplacement, data.HeatMethod);
        Assert.Empty(data.DataPoints);
        Assert.Empty(data.BaseLineCorrectedDataPoints);
        var expected = Numbers(sample, "heats_joules");
        var volumes = Numbers(sample, "injection_liters");
        foreach (var injection in data.Injections)
        {
            Assert.InRange(Math.Abs(injection.PeakArea.Value-expected[injection.ID]), 0, 1e-17);
            Assert.Equal(volumes[injection.ID], injection.Volume);
            injection.Include = true;
            injection.SetPeakArea(new FloatWithError(injection.PeakArea.Value, 1e-10));
        }
        Model model = sample.GetProperty("model").GetString() switch
        {
            "one-site" => new OneSetOfSites(data), "two-site" => new TwoSetsOfSites(data),
            "competitive" => new CompetitiveBinding(data),
            "sequential" => new SequentialBindingSites(data), _ => throw new InvalidOperationException(),
        };
        var initialLigand = sample.GetProperty("initial_ligand_molar").GetDouble();
        if (initialLigand != 0)
        {
            // Fixed, known segment initial conditions are inputs, not native predicted endpoints.
            data.AddSegment(new TandemExperimentSegment(0, data.CellConcentration, initialLigand));
            InjectionProcessingMethodTests.SetMethod(model, DilutionMethod.DiscreteDisplacement);
        }
        model.InitializeParameters(data);
        var parameters = sample.GetProperty("parameters");
        var logs = Numbers(parameters, "logka");
        var enthalpies = Numbers(parameters, "enthalpy");
        if (model is SequentialBindingSites)
        {
            model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = logs.Length;
            model.ApplyModelOptions();
        }
        for (var i = 0; i < logs.Length; i++)
        {
            model.Parameters.Table[DumasInjectionHeatTests.Affinities[i]].Update(logs[i]);
            model.Parameters.Table[DumasInjectionHeatTests.Enthalpies[i]].Update(enthalpies[i]);
        }
        var ns = Numbers(parameters, "n");
        if (ns.Length > 0) model.Parameters.Table[ParameterType.Nvalue1].Update(ns[0]);
        if (ns.Length > 1) model.Parameters.Table[ParameterType.Nvalue2].Update(ns[1]);
        model.Parameters.Table[ParameterType.Offset].Update(0);
        if (model is CompetitiveBinding)
        {
            var competitor = sample.GetProperty("competitor");
            model.ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue = new(competitor.GetProperty("concentration").GetDouble());
            model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue = new(competitor.GetProperty("logka").GetDouble());
            model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue = new(competitor.GetProperty("enthalpy").GetDouble());
        }
        data.Model = model;
        return model;
    }

    [Fact]
    public void TwoSiteReferencesUseTheProductionTwoSetModelAndExactIndependentSiteMapping()
    {
        var references = References().Where(c => c.GetProperty("model").GetString() == "two-site").ToArray();
        Assert.Equal(18, references.Length);
        foreach (var sample in references)
        {
            Assert.IsType<TwoSetsOfSites>(CreateModel(sample));
            Assert.Equal("BindingPolynomial", sample.GetProperty("upstream_model").GetString());
            Assert.Equal(new[] { 1.0, 1.0 }, Numbers(sample.GetProperty("parameters"), "n"));
            var constants = Numbers(sample.GetProperty("parameters"), "logka").Select(k => Math.Pow(10, k)).ToArray();
            var enthalpies = Numbers(sample.GetProperty("parameters"), "enthalpy").Select(h => h/4.184).ToArray();
            var native = sample.GetProperty("upstream_parameters");
            // Check the coefficient/state-enthalpy mapping independently of the generator.
            foreach (var free in new[] { 1e-8, 1e-6, 1e-4 })
            {
                var a = constants[0]*free;
                var b = constants[1]*free;
                var polynomial = 1 + native.GetProperty("beta1").GetDouble()*free
                    + native.GetProperty("beta2").GetDouble()*free*free;
                Assert.InRange(Math.Abs(polynomial/((1+a)*(1+b))-1), 0, 2e-14);
                var nativeEnthalpy = (native.GetProperty("dH1").GetDouble()*native.GetProperty("beta1").GetDouble()*free
                    + native.GetProperty("dH2").GetDouble()*native.GetProperty("beta2").GetDouble()*free*free)/polynomial;
                var independentEnthalpy = enthalpies[0]*a/(1+a) + enthalpies[1]*b/(1+b);
                var scale = enthalpies.Sum(Math.Abs);
                Assert.InRange(Math.Abs(nativeEnthalpy-independentEnthalpy)/scale, 0, 2e-14);
            }
        }
    }

    [Theory]
    [InlineData("one-exothermic")]
    [InlineData("sequential-2")]
    [Trait("Category", "Performance")]
    public void ReportsEvaluationAndEndToEndFitCostsSeparatelyFromValidation(string id)
    {
        var measurements = new List<object>();
        foreach (var method in new[] { DilutionMethod.MicroCal, DilutionMethod.Exponential, DilutionMethod.DiscreteDisplacement })
        {
            Model Prepared()
            {
                var model = CreateModel(Reference(id));
                RawDataReader.ProcessInjections(model.Data, method);
                model.HeatMethod = model.Data.HeatMethod;
                return model;
            }
            var model = Prepared();
            var parameters = model.Parameters.GetFittedParameterArray();
            for (var warm = 0; warm < 20; warm++) model.LossFunction(parameters, false);
            var objectiveSamples = new List<double>();
            for (var trial = 0; trial < 5; trial++)
            {
                var timer = Stopwatch.StartNew();
                for (var repeat = 0; repeat < 100; repeat++) model.LossFunction(parameters, false);
                objectiveSamples.Add(timer.Elapsed.TotalMilliseconds/100);
            }
            double Fit()
            {
                var fit = Prepared();
                foreach (var parameter in fit.Parameters.Table.Values)
                {
                    if (parameter.Key == ParameterType.Offset) parameter.Update(0, true);
                    else parameter.Update(parameter.Value * (DumasInjectionHeatTests.Affinities.Contains(parameter.Key) ? 1.01 : .95));
                }
                var timer = Stopwatch.StartNew();
                var convergence = new Solver { Model = fit, SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
                    ErrorEstimationMethod = ErrorEstimationMethod.None, UseErrorWeightedFitting = true,
                    MaxOptimizerIterations = 20000, Silent = true }.Solve();
                Assert.True(convergence.Success, convergence.Message);
                return timer.Elapsed.TotalMilliseconds;
            }
            Fit();
            var fitSamples = Enumerable.Range(0, 3).Select(_ => Fit()).OrderBy(x => x).ToArray();
            // Keep reference-report labels independent of the application's UI wording.
            measurements.Add(new { Method = method == DilutionMethod.DiscreteDisplacement ? "pytc" : method.DisplayName(),
                ObjectiveMilliseconds = objectiveSamples.OrderBy(x => x).ElementAt(2),
                FitMilliseconds = fitSamples[1] });
        }
        Record(id+"-performance", new { Kind = "Benchmark, not forward validation or parameter recovery", Measurements = measurements });
    }

    void Record(string id, object value)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(json);
        var destination = Environment.GetEnvironmentVariable("FTITC_PYTC_RESULTS");
        if (!string.IsNullOrWhiteSpace(destination))
        {
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, id+".json"), json+"\n");
        }
    }
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    static void AssertHash(string path, string expected) => Assert.Equal(expected, Hash(path));
    static JsonElement Reference(string id) => References().Single(c => c.GetProperty("id").GetString() == id);
    static double[] Numbers(JsonElement item, string property) => DumasInjectionHeatTests.Numbers(item, property);
    sealed record Comparison(string Id, bool Passed, bool RoundoffGoalMet, double MaximumErrorJoules, double PeakHeatJoules,
        double ErrorFractionOfPeak, string FtItcModel, string ReferenceFileSha256, double[] ExpectedHeatsJoules, double[] ActualHeatsJoules);
    sealed class MicrocalPrompt : IImportPromptService
    {
        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse)
            => new(EnergyUnit.MicroCal, useForRemainingFilesInQueue: false, isCancelled: false);
    }
}
