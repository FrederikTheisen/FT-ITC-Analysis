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
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;
using Xunit.Abstractions;

namespace AnalysisITC.Core.Tests;

/// <summary>Native integrated heats; declared parameters; no optimizer or raw thermograms.</summary>
[Collection("Solver events")]
public sealed class TwoSiteStoichiometryForwardTests : IDisposable
{
    const double Roundoff = 512 * 2.220446049250313e-16;
    static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ScientificValidation", "TwoSiteStoichiometry");
    readonly PreferencesState original = PreferencesState.FromSettings();
    readonly ITestOutputHelper output;

    public TwoSiteStoichiometryForwardTests(ITestOutputHelper output)
    {
        this.output = output;
        PreferencesState.Defaults().ApplyToSettings();
        IntegratedHeatReader.BeginImportQueue();
        PlatformServices.RegisterImportPromptService(new MicrocalPrompt());
    }

    public void Dispose()
    {
        IntegratedHeatReader.EndImportQueue();
        PlatformServices.RegisterImportPromptService(null);
        original.ApplyToSettings();
    }

    static JsonElement Manifest()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "reference.json")));
        return document.RootElement.Clone();
    }

    internal static JsonElement[] References() => Manifest().GetProperty("cases").EnumerateArray().ToArray();
    public static IEnumerable<object[]> Cases() => References().Select(c => new object[] { c.GetProperty("id").GetString() });
    static double[] Numbers(JsonElement item, string name) => item.GetProperty(name).EnumerateArray().Select(v => v.GetDouble()).ToArray();
    static double Value(JsonElement item, string name) => item.GetProperty(name).GetDouble();
    static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    [Theory]
    [MemberData(nameof(Cases))]
    public void NativePolynomialExactlyRepresentsUnequalIndependentSiteCapacities(string id)
    {
        var sample = References().Single(c => c.GetProperty("id").GetString() == id);
        var ns = Numbers(sample, "n");
        Assert.NotEqual(1, ns[0]);
        Assert.NotEqual(1, ns[1]);
        Assert.NotEqual(ns[0], ns[1]);
        var counts = Numbers(sample, "counts");
        var factor = Value(sample, "factor");
        Assert.Equal(ns, counts.Select(c => factor*c).ToArray());
        var kd = Numbers(sample, "kd_molar");
        var h = Numbers(sample, "enthalpy");
        var native = sample.GetProperty("upstream_parameters");
        // Closed-form factorized partition, ligand occupancy and heat expectation;
        // independent of the generator's enumeration of microscopic states.
        foreach (var free in new[] { kd.Min()*.01, kd.Min(), kd.Max(), kd.Max()*100 })
        {
            var partition = 1.0;
            var boundNumerator = 0.0;
            var heatNumerator = 0.0;
            for (var j = 1; j <= counts.Sum(); j++)
            {
                var weight = Value(native, $"beta{j}")*Math.Pow(free, j);
                partition += weight;
                boundNumerator += j*weight;
                heatNumerator += Value(native, $"dH{j}")*4.184*weight;
            }
            var expectedPartition = Math.Pow(1+free/kd[0], counts[0])*Math.Pow(1+free/kd[1], counts[1]);
            var occupancy = kd.Select(k => free/(k+free)).ToArray();
            Assert.InRange(Math.Abs(partition/expectedPartition-1), 0, 2e-14);
            Assert.InRange(Math.Abs(factor*boundNumerator/partition - ns[0]*occupancy[0]-ns[1]*occupancy[1]), 0, 2e-14*ns.Sum());
            var expectedHeat = ns[0]*h[0]*occupancy[0]+ns[1]*h[1]*occupancy[1];
            Assert.InRange(Math.Abs(factor*heatNumerator/partition-expectedHeat), 0, 2e-14*(ns[0]*Math.Abs(h[0])+ns[1]*Math.Abs(h[1])));
        }
    }

    [Fact]
    public void RepresentativeCurveSamplesBothHeatTransitionsWithEqualInjections()
    {
        var sample = References().Single(c => c.GetProperty("id").GetString() == "fractional-low-capacity");
        var shots = Numbers(sample, "shots_microliters");
        Assert.All(shots, shot => Assert.Equal(0.5, shot));
        var components = sample.GetProperty("site_heats_joules").EnumerateArray()
            .Select(c => c.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
        Assert.Equal(2, components.Length);
        foreach (var component in components)
        {
            Assert.Equal(shots.Length, component.Length);
            var magnitude = component.Select(Math.Abs).ToArray();
            var peak = magnitude.Max();
            var peakIndex = Array.IndexOf(magnitude, peak);
            // Heat transitions, rather than the whole rising occupancy range:
            // at least ten sampled heats on each declining 90%-to-10% limb.
            var points = magnitude.Skip(peakIndex).Count(q => q > .1*peak && q < .9*peak);
            Assert.True(points >= 10, $"Only {points} injection heats resolve the site transition.");
            Assert.InRange(magnitude.Last()/peak, 0, .02);
        }
        var total = Numbers(sample, "heats_joules");
        var scale = total.Max(Math.Abs);
        for (var i = 0; i < total.Length; i++)
            Assert.InRange(Math.Abs(components[0][i]+components[1][i]-total[i]), 0, Roundoff*scale);
    }

    [Fact]
    public void RecordsScientificOutcomesWithoutTreatingMismatchAsValidationSuccess()
    {
        var manifest = Manifest();
        Assert.Equal("d9ccde3f04e35a3d821ff37a4ad42e62a048d4ac", manifest.GetProperty("source").GetProperty("commit").GetString());
        Assert.Equal(manifest.GetProperty("generator_sha256").GetString(), Hash(Path.Combine(Root, "generate_reference.py")));
        var comparisons = References().Select(c => Compare(c)).ToArray();
        Assert.Equal(14, comparisons.Length);
        Assert.All(comparisons, c => Assert.True(double.IsFinite(c.ErrorFractionOfPeak)));
        var json = JsonSerializer.Serialize(new { ReferenceSha256 = Hash(Path.Combine(Root, "reference.json")),
            Acceptance = "abs(error_i) <= 512*epsilon*(peak_abs_native_heat + abs(native_heat_i))",
            Results = comparisons }, new JsonSerializerOptions { WriteIndented = true });
        output.WriteLine(json);
        var destination = Environment.GetEnvironmentVariable("FTITC_TWO_SITE_RESULTS");
        if (!string.IsNullOrWhiteSpace(destination))
        {
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "comparisons.json"), json+"\n");
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RecordsDefaultExternalArithmeticWithoutUsingItAsThePrecisionReference(string id)
    {
        var result = Compare(References().Single(c => c.GetProperty("id").GetString() == id));
        output.WriteLine(JsonSerializer.Serialize(result));
        Assert.True(double.IsFinite(result.ErrorFractionOfPeak));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ConvergedExternalHeatsAgreeAtFloatingPointPrecision(string id)
    {
        // Equations and finite-injection protocol are unchanged; only the
        // external root tolerance is tightened to resolve double-precision heats.
        var precision = PytcExternalReferenceTests.PrecisionReference();
        Assert.Equal(Hash(Path.Combine(Root, "reference.json")), precision.GetProperty("stoichiometry_reference_sha256").GetString());
        var run = precision.GetProperty("runs")[0];
        Assert.Equal("double xtol = 2e-12; -> double xtol = 2e-24;", run.GetProperty("patch").GetString());
        var sample = References().Single(c => c.GetProperty("id").GetString() == id);
        var curve = Numbers(run.GetProperty("curves"), id);
        var result = Compare(sample, curve);
        output.WriteLine(JsonSerializer.Serialize(result));
        Assert.True(result.Passed, $"{id}: tightened native tolerance still misses roundoff; error/peak={result.ErrorFractionOfPeak:G17}.");
    }

    internal static TwoSetsOfSites CreateModel(JsonElement sample)
    {
        var path = Path.Combine(Root, sample.GetProperty("file").GetString());
        Assert.Equal(sample.GetProperty("sha256").GetString(), Hash(path));
        var data = IntegratedHeatReader.ReadFile(path, true, DilutionMethod.DiscreteDisplacement, true);
        Assert.NotNull(data);
        Assert.Empty(data.DataPoints);
        Assert.Empty(data.BaseLineCorrectedDataPoints);
        var initial = Value(sample, "initial_ligand_molar");
        var model = new TwoSetsOfSites(data);
        if (initial != 0)
        {
            data.AddSegment(new TandemExperimentSegment(0, data.CellConcentration, initial));
            InjectionProcessingMethodTests.SetMethod(model, DilutionMethod.DiscreteDisplacement);
        }
        model.InitializeParameters(data);
        var ns = Numbers(sample, "n");
        var logs = Numbers(sample, "logka");
        var h = Numbers(sample, "enthalpy");
        model.Parameters.Table[ParameterType.Nvalue1].Update(ns[0]);
        model.Parameters.Table[ParameterType.Nvalue2].Update(ns[1]);
        model.Parameters.Table[ParameterType.Affinity1].Update(logs[0]);
        model.Parameters.Table[ParameterType.Affinity2].Update(logs[1]);
        model.Parameters.Table[ParameterType.Enthalpy1].Update(h[0]);
        model.Parameters.Table[ParameterType.Enthalpy2].Update(h[1]);
        model.Parameters.Table[ParameterType.Offset].Update(0);
        data.Model = model;
        return model;
    }

    static Comparison Compare(JsonElement sample, double[] diagnosticHeats = null)
    {
        var model = CreateModel(sample);
        var data = model.Data;
        Assert.Equal(InjectionHeatMethod.DiscreteDisplacement, model.HeatMethod);
        var expected = Numbers(sample, "heats_joules");
        var shots = Numbers(sample, "shots_microliters");
        var cells = Numbers(sample, "cell_concentrations_molar");
        var ligands = Numbers(sample, "titrant_concentrations_molar");
        Assert.Equal(expected.Length, data.Injections.Count);
        Assert.InRange(Math.Abs(data.CellVolume/ (Value(sample, "cell_microliters")*1e-6)-1), 0, Roundoff);
        foreach (var injection in data.Injections)
        {
            injection.Include = true;
            Assert.InRange(Math.Abs(injection.PeakArea.Value-expected[injection.ID]), 0, Roundoff*expected.Max(Math.Abs));
            Assert.InRange(Math.Abs(injection.Volume-shots[injection.ID]*1e-6), 0, Roundoff*injection.Volume);
            Assert.InRange(Math.Abs(injection.ActualCellConcentration-cells[injection.ID+1]), 0, Roundoff*cells.Max());
            Assert.InRange(Math.Abs(injection.ActualTitrantConcentration-ligands[injection.ID+1]), 0, Roundoff*ligands.Max());
        }
        var actual = data.Injections.Select(i => model.Evaluate(i.ID, false)).ToArray();
        var referenceHeats = diagnosticHeats ?? expected;
        Assert.Equal(expected.Length, referenceHeats.Length);
        var peak = referenceHeats.Max(Math.Abs);
        var error = actual.Zip(referenceHeats, (a, e) => Math.Abs(a-e)).ToArray();
        return new(sample.GetProperty("id").GetString(), error.Select((e, i) => e <= Roundoff*(peak+Math.Abs(referenceHeats[i]))).All(p => p),
            error.Max(), peak, error.Max()/peak, referenceHeats, actual);
    }

    sealed record Comparison(string Id, bool Passed, double MaximumErrorJoules, double PeakHeatJoules,
        double ErrorFractionOfPeak, double[] ExpectedHeatsJoules, double[] ActualHeatsJoules);
    sealed class MicrocalPrompt : IImportPromptService
    {
        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse)
            => new(EnergyUnit.MicroCal, useForRemainingFilesInQueue: false, isCancelled: false);
    }
}
