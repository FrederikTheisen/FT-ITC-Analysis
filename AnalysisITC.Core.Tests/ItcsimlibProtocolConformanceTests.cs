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

namespace AnalysisITC.Core.Tests;

/// <summary>
/// Current-implementation regression expectations captured from FT-ITC.
/// One-site and independent-site equilibria use pinned itcsimlib models; the
/// remaining physical equilibria use local mass balances. This adapter is not
/// independent external forward-model validation. All cases use
/// the declared case parameters. The checked predictions below were recorded
/// from the current FT-ITC implementation and are regression values only.
/// </summary>
[Collection("Published model reproduction")]
public sealed class ItcsimlibProtocolConformanceTests : IDisposable
{
    static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "ScientificValidation", "ItcSimlibProtocolConformance");
    static readonly string PredictionFixturePath = Path.Combine(FixtureDirectory,
        "current-implementation-predictions.json");
    static readonly CaseSpec[] Cases = {
        new("one-site-exothermic", AnalysisModel.OneSetOfSites,
            "528f97c676237957af2ff50addc39cdd0099130f83c1ddc40b927e416de5273c",
            new[] { 1.1 }, new[] { 6.2 }, new[] { -32000.0 }, "itcsimlib.OneMode"),
        new("two-independent-sites", AnalysisModel.TwoSetsOfSites,
            "4adfe70b9430a4408ebd42db6f914cca4bfa968e1baa7f4f068baff78cc27c8f",
            new[] { 1.0, 1.0 }, new[] { 6.4, 5.0 }, new[] { -25000.0, 15000.0 }, "itcsimlib.NModes"),
        new("competitive-binding", AnalysisModel.CompetitiveBinding,
            "78da989ee15d6c9842c9ddcbfc0ed3c422a741f794502ab169e53b178471cd4c",
            new[] { 1.15 }, new[] { 7.0 }, new[] { -18000.0 }, "independent-two-ligand-mass-balance",
            new CompetitorSpec(30e-6, 6.2, -8000.0)),
        new("sequential-2", AnalysisModel.SequentialBindingSites,
            "0d7e6e9a44cb4bdf9bf2c170b0b99b4db168eaf33526845bb285c9dfb23784df",
            Array.Empty<double>(), new[] { 6.4, 5.7 }, new[] { -30000.0, 20000.0 }, "independent-macroscopic-binding-polynomial"),
        new("sequential-3", AnalysisModel.SequentialBindingSites,
            "5e09dfe94ff1def667fd5888b2c17ec9290721c01d82dbcde20cd4e6010038ce",
            Array.Empty<double>(), new[] { 6.4, 5.7, 5.0 }, new[] { -30000.0, 20000.0, -18000.0 }, "independent-macroscopic-binding-polynomial"),
        new("sequential-4", AnalysisModel.SequentialBindingSites,
            "ec7b638e6f5735be8940b9d64d1eea536922d8468add31952c9c260b269d9b18",
            Array.Empty<double>(), new[] { 6.4, 5.7, 5.0, 4.3 }, new[] { -30000.0, 20000.0, -18000.0, 14000.0 }, "independent-macroscopic-binding-polynomial"),
        new("dissociation", AnalysisModel.Dissociation,
            "032ffad1fe847ca4fadc26f6533214642966452e9e5f5bda26be356e2cf9f650",
            Array.Empty<double>(), new[] { 5.6 }, new[] { -24000.0 }, "independent-dimerization-mass-balance"),
    };
    readonly PreferencesState original = PreferencesState.FromSettings();

    public ItcsimlibProtocolConformanceTests()
    {
        PreferencesState.Defaults().ApplyToSettings();
        AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
        IntegratedHeatReader.BeginImportQueue();
        PlatformServices.RegisterImportPromptService(new MicrocalPrompt());
    }

    public void Dispose()
    {
        IntegratedHeatReader.EndImportQueue();
        PlatformServices.RegisterImportPromptService(null);
        original.ApplyToSettings();
    }

    public static IEnumerable<object[]> RegressionCases() => Cases.Select(item => new object[] { item.Id });

    [Theory]
    [MemberData(nameof(RegressionCases))]
    public void CurrentMicroCalPredictionRegressionMatchesEveryFtItcModel(string id)
    {
        var spec = Cases.Single(item => item.Id == id);
        var path = Path.Combine(FixtureDirectory, id + ".DH");
        Assert.Equal(spec.Sha256,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());

        var data = IntegratedHeatReader.ReadFile(path);
        Assert.NotNull(data);
        foreach (var injection in data.Injections)
            injection.Include = true;
        var model = CreateModel(data, spec);

        // The full prediction vectors were recorded from the current FT-ITC
        // implementation. They are regression checks, not external reference heats.
        var expected = ReadExpectedPredictions(id);
        Assert.Equal(data.Injections.Count, expected.Length);
        foreach (var injection in data.Injections)
        {
            var expectedHeat = expected[injection.ID];
            var actualHeat = model.Evaluate(injection.ID);
            Assert.InRange(Math.Abs(actualHeat - expectedHeat), 0.0,
                Math.Max(1e-18, Math.Abs(expectedHeat) * 1e-11));
        }
    }

    static double[] ReadExpectedPredictions(string id)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PredictionFixturePath));
        var caseNode = document.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == id);
        return caseNode.GetProperty("predicted_heat_joules").EnumerateArray()
            .Select(item => item.GetDouble()).ToArray();
    }

    static Model CreateModel(ExperimentData data, CaseSpec spec)
    {
        Model model = spec.ModelType switch {
            AnalysisModel.OneSetOfSites => new OneSetOfSites(data),
            AnalysisModel.TwoSetsOfSites => new TwoSetsOfSites(data),
            AnalysisModel.CompetitiveBinding => new CompetitiveBinding(data),
            AnalysisModel.SequentialBindingSites => new SequentialBindingSites(data),
            AnalysisModel.Dissociation => new Dissociation(data),
            _ => throw new InvalidOperationException("Unsupported protocol-conformance model"),
        };
        model.InitializeParameters(data);
        if (model is SequentialBindingSites)
        {
            model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = spec.LogKa.Length;
            model.ApplyModelOptions();
        }
        for (var index = 0; index < spec.LogKa.Length; index++)
        {
            model.Parameters.Table[Parameter("Affinity", index)].Update(spec.LogKa[index]);
            model.Parameters.Table[Parameter("Enthalpy", index)].Update(spec.Enthalpy[index]);
        }
        for (var index = 0; index < spec.N.Length; index++)
            model.Parameters.Table[Parameter("Nvalue", index)].Update(spec.N[index]);
        model.Parameters.Table[ParameterType.Offset].Update(0.0);
        if (model is CompetitiveBinding && spec.Competitor is not null)
        {
            model.ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue = new FloatWithError(spec.Competitor.Concentration, 0);
            model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue = new FloatWithError(spec.Competitor.LogKa, 0);
            model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue = new FloatWithError(spec.Competitor.Enthalpy, 0);
        }
        data.Model = model;
        return model;
    }

    static ParameterType Parameter(string prefix, int index) => Enum.Parse<ParameterType>(prefix + (index + 1));

    sealed class MicrocalPrompt : IImportPromptService
    {
        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse) =>
            new(EnergyUnit.MicroCal, useForRemainingFilesInQueue: false, isCancelled: false);
    }

    sealed record CompetitorSpec(double Concentration, double LogKa, double Enthalpy);
    sealed record CaseSpec(string Id, AnalysisModel ModelType, string Sha256, double[] N,
        double[] LogKa, double[] Enthalpy, string Generator, CompetitorSpec Competitor = null);
}
