using System;
using System.Collections.Generic;
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

/// <summary>
/// Integrated heats produced by the pinned, unmodified external pytc models.
/// The 2% comparison is a cross-convention agreement check, not an assertion
/// that pytc and FT-ITC implement identical finite-injection approximations.
/// </summary>
[Collection("Published model reproduction")]
public sealed class ExternalIntegratedHeatReferenceTests : IDisposable
{
    static readonly string[] CaseIds = {
        "one-site-exothermic", "one-site-endothermic", "two-independent-sites",
        "competitive", "sequential-2", "sequential-3", "sequential-4",
        "sequential-3-subshots-10", "sequential-3-subshots-100",
        "sequential-4-subshots-10", "sequential-4-subshots-100",
    };
    static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "ScientificValidation", "ExternalIntegratedHeats");
    readonly PreferencesState original = PreferencesState.FromSettings();
    readonly ITestOutputHelper output;

    public ExternalIntegratedHeatReferenceTests(ITestOutputHelper output)
    {
        this.output = output;
        PreferencesState.Defaults().ApplyToSettings();
        AppSettings.DilutionCalculationMethod = DilutionMethod.Exponential;
        AppSettings.OptimizerTolerance = 1;
        IntegratedHeatReader.BeginImportQueue();
        PlatformServices.RegisterImportPromptService(new MicrocalPrompt());
    }

    public void Dispose()
    {
        IntegratedHeatReader.EndImportQueue();
        PlatformServices.RegisterImportPromptService(null);
        original.ApplyToSettings();
    }

    public static IEnumerable<object[]> ForwardCases() => CaseIds.Select(id => new object[] { id });
    public static IEnumerable<object[]> FitCases()
    {
        foreach (var id in CaseIds.Where(id => id != "sequential-3" && id != "sequential-4"))
        foreach (var solver in new[] { SolverAlgorithm.LevenbergMarquardt, SolverAlgorithm.NelderMead })
        foreach (var start in new[] { -1, 1 })
            yield return new object[] { id, solver, start };
    }

    public static IEnumerable<object[]> NativeHigherStepCases()
    {
        foreach (var id in new[] { "sequential-3", "sequential-4" })
        foreach (var solver in new[] { SolverAlgorithm.LevenbergMarquardt, SolverAlgorithm.NelderMead })
        foreach (var start in new[] { -1, 1 })
            yield return new object[] { id, solver, start };
    }

    [Theory]
    [MemberData(nameof(ForwardCases))]
    public void OriginalExternalHeatsImportUnchangedAndAgreeAtGeneratingParameters(string id)
    {
        using var reference = ReadReference();
        var sample = FindCase(reference, id);
        var model = CreateModel(sample, startDirection: 0);
        var expectedHeats = Numbers(sample, "heats_joules");
        var maximumHeat = expectedHeats.Max(Math.Abs);
        var maximumError = model.Data.Injections.Max(injection =>
            Math.Abs(model.Evaluate(injection.ID) - expectedHeats[injection.ID]));
        var fractionalError = maximumError / maximumHeat;
        Record(id + "-forward", new { id, kind = "forward", maximumHeat, maximumError, fractionalError });
        Assert.InRange(fractionalError, 0,
            reference.RootElement.GetProperty("acceptance").GetProperty("forward_max_error_fraction_of_peak_heat").GetDouble());
    }

    [Theory]
    [MemberData(nameof(FitCases))]
    public void FitsExternalIntegratedHeatsFromDeclaredAlternativeStarts(
        string id, SolverAlgorithm algorithm, int startDirection)
        => CheckParameterRecovery(id, algorithm, startDirection);

    [ExternalProtocolDiagnosticTheory]
    [Trait("Category", "ExternalProtocolDiagnostic")]
    [MemberData(nameof(NativeHigherStepCases))]
    public void NativePytcHigherStepProtocolRetainsStrictRecoveryDiagnostic(
        string id, SolverAlgorithm algorithm, int startDirection)
        => CheckParameterRecovery(id, algorithm, startDirection);

    void CheckParameterRecovery(string id, SolverAlgorithm algorithm, int startDirection)
    {
        using var reference = ReadReference();
        var sample = FindCase(reference, id);
        var model = CreateModel(sample, startDirection);
        var initial = ParameterValues(model);
        var convergence = new Solver {
            Model = model, SolverAlgorithm = algorithm,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = true, MaxOptimizerIterations = 20000, Silent = true,
            SolverToleranceModifier = algorithm == SolverAlgorithm.NelderMead ? 1e-4 : 1,
        }.Solve();

        var expected = sample.GetProperty("expected");
        var count = Numbers(expected, "logka").Length;
        var actualN = ReadParameters(model, "Nvalue", Numbers(expected, "n").Length);
        var actualLogKa = ReadParameters(model, "Affinity", count);
        var actualH = ReadParameters(model, "Enthalpy", count);
        var targetN = Numbers(expected, "n");
        var targetLogKa = Numbers(expected, "logka");
        var targetH = Numbers(expected, "enthalpy_j_per_mol");
        // Independent site labels may be interchanged; compare the complete
        // site tuples in affinity order. Sequential step labels stay ordered.
        if (model is TwoSetsOfSites && actualLogKa[0] < actualLogKa[1])
        {
            Array.Reverse(actualN);
            Array.Reverse(actualLogKa);
            Array.Reverse(actualH);
        }
        var nErrors = actualN.Select((value, i) => Math.Abs(value / targetN[i] - 1)).ToArray();
        var kaErrors = actualLogKa.Select((value, i) => Math.Abs(Math.Pow(10, value - targetLogKa[i]) - 1)).ToArray();
        var hErrors = actualH.Select((value, i) => Math.Abs(value / targetH[i] - 1)).ToArray();
        var offset = model.Parameters.Table[ParameterType.Offset].Value;
        var rmsd = Math.Sqrt(model.Data.Injections.Average(injection =>
            Math.Pow(model.Evaluate(injection.ID) - injection.PeakArea.Value, 2)));
        Record($"{id}-{algorithm}-{startDirection}", new {
            id, kind = "fit", algorithm = algorithm.ToString(), startDirection,
            success = convergence.Success, message = convergence.Message,
            initial, fitted = ParameterValues(model), nErrors, kaErrors, hErrors, offset, rmsd,
        });
        var acceptance = reference.RootElement.GetProperty("acceptance");
        Assert.True(convergence.Success, convergence.Message);
        Assert.All(nErrors, error => Assert.InRange(error, 0, acceptance.GetProperty("fitted_n_relative").GetDouble()));
        Assert.All(kaErrors, error => Assert.InRange(error, 0, acceptance.GetProperty("fitted_ka_relative").GetDouble()));
        Assert.All(hErrors, error => Assert.InRange(error, 0, acceptance.GetProperty("fitted_enthalpy_relative").GetDouble()));
        Assert.InRange(Math.Abs(offset), 0, acceptance.GetProperty("fitted_offset_absolute_j_per_mol").GetDouble());
    }

    static Model CreateModel(JsonElement sample, int startDirection)
    {
        var path = Path.Combine(FixtureDirectory, sample.GetProperty("file").GetString());
        Assert.Equal(sample.GetProperty("sha256").GetString(),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        var data = IntegratedHeatReader.ReadFile(path);
        Assert.NotNull(data);
        Assert.Empty(data.DataPoints);
        Assert.Empty(data.BaseLineCorrectedDataPoints);
        Assert.Equal(sample.GetProperty("cell_liters").GetDouble(), data.CellVolume, 12);
        Assert.Equal(sample.GetProperty("cell_molar").GetDouble(), data.CellConcentration.Value, 12);
        Assert.Equal(sample.GetProperty("syringe_molar").GetDouble(), data.SyringeConcentration.Value, 12);
        var heats = Numbers(sample, "heats_joules");
        var volumes = Numbers(sample, "injection_liters");
        Assert.Equal(heats.Length, data.Injections.Count);
        foreach (var injection in data.Injections)
        {
            Assert.Equal(heats[injection.ID], injection.PeakArea.Value, 14);
            Assert.Equal(volumes[injection.ID], injection.Volume, 12);
            // This is an ideal noiseless reference, including the first shot.
            // Uniform SD only scales the numerical objective, not its optimum.
            injection.Include = true;
            injection.SetPeakArea(new FloatWithError(injection.PeakArea.Value, 1e-10));
        }
        Model model = sample.GetProperty("model").GetString() switch {
            "OneSetOfSites" => new OneSetOfSites(data),
            "TwoSetsOfSites" => new TwoSetsOfSites(data),
            "CompetitiveBinding" => new CompetitiveBinding(data),
            "SequentialBindingSites" => new SequentialBindingSites(data),
            _ => throw new InvalidOperationException("Unsupported external model reference"),
        };
        model.InitializeParameters(data);
        var expected = sample.GetProperty("expected");
        var logs = Numbers(expected, "logka");
        var hs = Numbers(expected, "enthalpy_j_per_mol");
        var ns = Numbers(expected, "n");
        if (model is SequentialBindingSites)
        {
            model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = logs.Length;
            model.ApplyModelOptions();
        }
        for (var i = 0; i < logs.Length; i++)
        {
            var direction = startDirection * (i % 2 == 0 ? 1 : -1);
            model.Parameters.Table[Parameter("Affinity", i)].Update(logs[i] + 0.15 * direction);
            model.Parameters.Table[Parameter("Enthalpy", i)].Update(hs[i] * (1 - 0.15 * direction));
        }
        for (var i = 0; i < ns.Length; i++)
            model.Parameters.Table[Parameter("Nvalue", i)].Update(ns[i] * (1 + 0.15 * startDirection));
        model.Parameters.Table[ParameterType.Offset].Update(100 * startDirection);
        if (model is CompetitiveBinding)
        {
            var competitor = sample.GetProperty("competitor");
            model.ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue = new(competitor.GetProperty("concentration_molar").GetDouble());
            model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue = new(competitor.GetProperty("logka").GetDouble());
            model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue = new(competitor.GetProperty("enthalpy_j_per_mol").GetDouble());
        }
        data.Model = model;
        return model;
    }

    void Record(string name, object result)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions {
            WriteIndented = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        });
        output.WriteLine(json);
        var destination = Environment.GetEnvironmentVariable("FTITC_EXTERNAL_VALIDATION_RESULTS");
        if (!string.IsNullOrEmpty(destination))
        {
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, name + ".json"), json + "\n");
        }
    }

    static JsonDocument ReadReference() => JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "reference.json")));
    static JsonElement FindCase(JsonDocument document, string id) => document.RootElement.GetProperty("cases")
        .EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
    static double[] Numbers(JsonElement element, string property) => element.GetProperty(property).EnumerateArray().Select(x => x.GetDouble()).ToArray();
    static ParameterType Parameter(string prefix, int index) => Enum.Parse<ParameterType>(prefix + (index + 1));
    static double[] ReadParameters(Model model, string prefix, int count) => Enumerable.Range(0, count)
        .Select(i => model.Parameters.Table[Parameter(prefix, i)].Value).ToArray();
    static Dictionary<string, double> ParameterValues(Model model) => model.Parameters.Table.ToDictionary(x => x.Key.ToString(), x => x.Value.Value);

    sealed class MicrocalPrompt : IImportPromptService
    {
        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse) =>
            new(EnergyUnit.MicroCal, useForRemainingFilesInQueue: false, isCancelled: false);
    }
}

/// <summary>
/// The untouched native pytc 3/4-step protocols miss the declared 2% parameter
/// comparison; injection refinement strongly reduces the discrepancy. Preserve an
/// executable strict comparison without treating the known mismatch as a
/// passing scientific validation or breaking ordinary regression runs.
/// </summary>
public sealed class ExternalProtocolDiagnosticTheoryAttribute : TheoryAttribute
{
    public ExternalProtocolDiagnosticTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("FTITC_RUN_EXTERNAL_PROTOCOL_DIAGNOSTICS") != "1")
            Skip = "Known cross-software injection-convention mismatch. Set FTITC_RUN_EXTERNAL_PROTOCOL_DIAGNOSTICS=1 to execute the eight strict comparisons; see ExternalIntegratedHeats/RESULTS.md.";
    }
}
