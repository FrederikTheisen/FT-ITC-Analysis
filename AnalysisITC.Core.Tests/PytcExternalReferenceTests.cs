using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    const double PracticalErrorFractionOfPeak = 0.0001; // Historical default-arithmetic audit only; never a validation criterion.
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

    internal static JsonElement[] References()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(DirectoryPath, "reference.json")));
        Assert.Equal("d9ccde3f04e35a3d821ff37a4ad42e62a048d4ac", json.RootElement.GetProperty("source").GetProperty("commit").GetString());
        return json.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToArray();
    }
    public static IEnumerable<object[]> Cases() => References()
        .Select(c => new object[] { c.GetProperty("id").GetString() });

    [Theory]
    [MemberData(nameof(Cases))]
    public void RecordsDefaultExternalArithmeticWithoutUsingItAsThePrecisionReference(string id)
    {
        var result = Compare(Reference(id));
        Record(id, result);
        Assert.True(double.IsFinite(result.ErrorFractionOfPeak));
    }

    internal static JsonElement PrecisionReference()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(DirectoryPath, "precision-reference.json")));
        var reference = document.RootElement.Clone();
        Assert.Equal(Hash(Path.Combine(DirectoryPath, "reference.json")), reference.GetProperty("reference_sha256").GetString());
        Assert.Equal(Hash(Path.Combine(DirectoryPath, "precision_reference.py")), reference.GetProperty("generator_sha256").GetString());
        return reference;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ConvergedExternalHeatsAgreeAtFloatingPointPrecision(string id)
    {
        var runs = PrecisionReference().GetProperty("runs").EnumerateArray().ToArray();
        var expected = Numbers(runs[0].GetProperty("curves"), id);
        var result = Compare(Reference(id), expected);
        output.WriteLine(JsonSerializer.Serialize(result));
        Assert.True(result.RoundoffGoalMet, $"{id}: error/peak={result.ErrorFractionOfPeak:G17}; floating-point criterion failed.");
    }

    [Fact]
    public void RecordsConvergedExternalComparisons()
    {
        var runs = PrecisionReference().GetProperty("runs").EnumerateArray().ToArray();
        var results = References().Select(c => Compare(c, Numbers(runs[0].GetProperty("curves"), c.GetProperty("id").GetString()))).ToArray();
        Record("precision-comparisons", new { RoundoffMultiplier = 512, Epsilon = 2.220446049250313e-16,
            ReferenceSha256 = Hash(Path.Combine(DirectoryPath, "precision-reference.json")), Results = results });
        Assert.All(results, result => Assert.True(result.RoundoffGoalMet, result.Id));
    }

    [Fact]
    public void PrecisionReferencesRemainConvergedWhenArithmeticIsTightenedAgain()
    {
        var runs = PrecisionReference().GetProperty("runs").EnumerateArray().ToArray();
        Assert.Equal(new[] { 2e-24, 2e-27 }, runs.Select(r => r.GetProperty("xtol_molar").GetDouble()).ToArray());
        Assert.Equal(new[] { 80, 100 }, runs.Select(r => r.GetProperty("competitive_decimal_digits").GetInt32()).ToArray());
        var curves = runs[0].GetProperty("curves").EnumerateObject().ToArray();
        Assert.Equal(52, curves.Length);
        foreach (var curve in curves)
        {
            var first = Numbers(runs[0].GetProperty("curves"), curve.Name);
            var second = Numbers(runs[1].GetProperty("curves"), curve.Name);
            var peak = second.Max(Math.Abs);
            Assert.All(first.Zip(second), pair => Assert.InRange(Math.Abs(pair.First-pair.Second), 0,
                Roundoff*(peak+Math.Abs(pair.Second))));
        }
    }

    [Fact]
    public void RecordsDefaultExternalComparisons()
    {
        var results = References().Select(Compare).ToArray();
        Record("comparisons", new { PracticalErrorFractionOfPeak, RoundoffMultiplier = 512, Epsilon = 2.220446049250313e-16,
            RoundoffGoal = "abs(error) <= 512*epsilon*(peak_abs_heat + abs(expected))",
            ReferenceSha256 = Hash(Path.Combine(DirectoryPath, "reference.json")), Results = results });
        Assert.Equal(38, results.Length);
        // Audit the default upstream arithmetic separately from the strict precision tests.
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

    Comparison Compare(JsonElement sample) => Compare(sample, null);

    Comparison Compare(JsonElement sample, double[] precisionHeats)
    {
        var model = CreateModel(sample);
        var expected = precisionHeats ?? Numbers(sample, "heats_joules");
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
    [InlineData("two-kd50-500")]
    [InlineData("two-realistic")]
    [InlineData("sequential-2")]
    [InlineData("sequential-3")]
    [InlineData("sequential-4")]
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
            // Fast objectives can finish 1,000 calls before background tiered JIT
            // compilation settles. Keep runtime defaults and warm for at least 0.5 s.
            var warmupTimer = Stopwatch.StartNew();
            var warmupEvaluations = 0;
            while (warmupEvaluations < 1000 || warmupTimer.ElapsedMilliseconds < 500)
            {
                model.LossFunction(parameters, true);
                warmupEvaluations++;
            }
            var objectiveSamples = new List<double>();
            var allocationSamples = new List<double>();
            var objectiveTimer = new Stopwatch();
            for (var trial = 0; trial < 7; trial++)
            {
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                objectiveTimer.Restart();
                for (var repeat = 0; repeat < 100; repeat++) model.LossFunction(parameters, true);
                objectiveTimer.Stop();
                allocationSamples.Add((GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / 100.0);
                objectiveSamples.Add(objectiveTimer.Elapsed.TotalMilliseconds/100);
            }
            object Fit(out double milliseconds)
            {
                var fit = Prepared();
                // Deterministic synthetic observations are performance inputs, not external validation.
                var heats = fit.Data.Injections.Select(i => fit.Evaluate(i.ID)).ToArray();
                var sd = 0.005 * heats.Max(Math.Abs);
                var random = new Random(18719);
                foreach (var injection in fit.Data.Injections)
                {
                    var z = Math.Sqrt(-2 * Math.Log(Math.Max(1e-10, random.NextDouble())))
                        * Math.Cos(2 * Math.PI * random.NextDouble());
                    injection.SetPeakArea(new FloatWithError(heats[injection.ID] + sd * z, sd));
                }
                foreach (var parameter in fit.Parameters.Table.Values)
                {
                    if (parameter.Key == ParameterType.Offset) parameter.Update(0, true);
                    else parameter.Update(parameter.Value * (DumasInjectionHeatTests.Affinities.Contains(parameter.Key) ? 1.01 : .95));
                }
                var solver = new Solver { Model = fit, SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
                    ErrorEstimationMethod = ErrorEstimationMethod.None, UseErrorWeightedFitting = true,
                    MaxOptimizerIterations = 20000, Silent = true };
                var timer = Stopwatch.StartNew();
                var convergence = solver.Solve();
                timer.Stop();
                milliseconds = timer.Elapsed.TotalMilliseconds;
                Assert.True(convergence.Success, convergence.Message);
                return new { Parameters = fit.Parameters.Table.ToDictionary(p => p.Key.ToString(), p => p.Value.Value),
                    Heats = fit.Data.Injections.Select(i => fit.Evaluate(i.ID)).ToArray(),
                    Observations = fit.Data.Injections.Select(i => i.PeakArea.Value).ToArray(),
                    Loss = fit.LossFunction(fit.Parameters.GetFittedParameterArray(), true) };
            }
            Fit(out _);
            var fitSamples = new List<double>();
            var fitResults = new List<object>();
            for (var trial = 0; trial < 3; trial++)
            {
                fitResults.Add(Fit(out var milliseconds));
                fitSamples.Add(milliseconds);
            }
            // Keep reference-report labels independent of the application's UI wording.
            measurements.Add(new { Method = method == DilutionMethod.DiscreteDisplacement ? "pytc" : method.DisplayName(),
                ObjectiveWarmupEvaluations = warmupEvaluations,
                ObjectiveMilliseconds = objectiveSamples.OrderBy(x => x).ElementAt(3),
                ObjectiveMillisecondsRange = new[] { objectiveSamples.Min(), objectiveSamples.Max() },
                ObjectiveAllocatedBytes = allocationSamples.OrderBy(x => x).ElementAt(3),
                ObjectiveAllocatedBytesRange = new[] { allocationSamples.Min(), allocationSamples.Max() },
                FitMilliseconds = fitSamples.OrderBy(x => x).ElementAt(1),
                FitMillisecondsRange = new[] { fitSamples.Min(), fitSamples.Max() },
                FitResults = fitResults });
        }
        Record(id+"-performance", new { Kind = "Benchmark, not forward validation or parameter recovery",
            Runtime = RuntimeInformation.FrameworkDescription, RuntimeVersion = Environment.Version.ToString(),
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(), OS = RuntimeInformation.OSDescription,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
            TieredPGO = Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "default",
            MinimumObjectiveWarmup = 1000, MinimumObjectiveWarmupMilliseconds = 500,
            ObjectiveBatches = 7, ObjectivesPerBatch = 100,
            FitWarmup = 1, FitSamples = 3, ErrorWeighted = true,
            Algorithm = "LevenbergMarquardt", OptimizerTolerance = AppSettings.OptimizerTolerance,
            Measurements = measurements });
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
