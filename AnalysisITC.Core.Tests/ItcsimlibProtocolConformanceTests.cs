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
/// External protocol-conformance references generated without FT-ITC code.
/// One-site and independent-site equilibria use pinned itcsimlib models; the
/// remaining physical equilibria use standalone mass balances. All cases use
/// the declared MicroCal concentration and injection-heat protocol.
/// </summary>
[Collection("Published model reproduction")]
public sealed class ItcsimlibProtocolConformanceTests : IDisposable
{
    static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "ScientificValidation", "ItcSimlibProtocolConformance");
    static readonly CaseSpec[] Cases = {
        new("one-site-exothermic", AnalysisModel.OneSetOfSites,
            "a5a33c722e638f8fdb1d566bde73043d18cfb178a9150a7c02161df40082f2d8",
            new[] { 1.1 }, new[] { 6.2 }, new[] { -32000.0 }, "itcsimlib.OneMode"),
        new("two-independent-sites", AnalysisModel.TwoSetsOfSites,
            "5ca898532a8004e7328ecacdd4431be332363fda77d16bf24d6bd45b33baea9f",
            new[] { 1.0, 1.0 }, new[] { 6.4, 5.0 }, new[] { -25000.0, 15000.0 }, "itcsimlib.NModes"),
        new("competitive-binding", AnalysisModel.CompetitiveBinding,
            "d4f3d6b1d4aaa910d9a72dce3a16da5853fd508232adffd0e77a38c380669ee3",
            new[] { 1.15 }, new[] { 7.0 }, new[] { -18000.0 }, "independent-two-ligand-mass-balance",
            new CompetitorSpec(30e-6, 6.2, -8000.0)),
        new("sequential-2", AnalysisModel.SequentialBindingSites,
            "519d7ebdb5d179951ddc2b56942c4529151336416f598e9749687b695e486c83",
            Array.Empty<double>(), new[] { 6.4, 5.7 }, new[] { -30000.0, 20000.0 }, "independent-macroscopic-binding-polynomial"),
        new("sequential-3", AnalysisModel.SequentialBindingSites,
            "861381ea4bfca3d6817d2f6b6825999c331cbd69eec1203221262c4e737d0ab5",
            Array.Empty<double>(), new[] { 6.4, 5.7, 5.0 }, new[] { -30000.0, 20000.0, -18000.0 }, "independent-macroscopic-binding-polynomial"),
        new("sequential-4", AnalysisModel.SequentialBindingSites,
            "27d3176af4327def5ac3958eb44e0e00a81df1fedb720b403087f5639264a31d",
            Array.Empty<double>(), new[] { 6.4, 5.7, 5.0, 4.3 }, new[] { -30000.0, 20000.0, -18000.0, 14000.0 }, "independent-macroscopic-binding-polynomial"),
        new("dissociation", AnalysisModel.Dissociation,
            "5aeb3588d6d6c3ede12ea09dec263fd5c7ee6255dcd5d4549b728b34322cbcfa",
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

    public static IEnumerable<object[]> ForwardCases() => Cases.Select(item => new object[] { item.Id });

    [Theory]
    [MemberData(nameof(ForwardCases))]
    public void ProtocolConformantExternalHeatMatchesEveryFtItcModel(string id)
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

        var peakHeat = data.Injections.Max(injection => Math.Abs(injection.PeakArea.Value));
        var maximumError = data.Injections.Max(injection =>
            Math.Abs(model.Evaluate(injection.ID) - injection.PeakArea.Value));
        Record(id, spec, data, model, maximumError, peakHeat);

        // The only allowed discrepancy is external solver/floating-point order.
        Assert.InRange(maximumError / peakHeat, 0.0, 2e-10);
    }

    static void Record(string id, CaseSpec spec, ExperimentData data, Model model, double maximumError, double peakHeat)
    {
        var destination = Environment.GetEnvironmentVariable("FTITC_PROTOCOL_CONFORMANCE_RESULTS");
        if (string.IsNullOrWhiteSpace(destination)) return;
        Directory.CreateDirectory(destination);
        var rows = data.Injections.Select(injection => new {
            injection = injection.ID + 1,
            observed_joules = injection.PeakArea.Value,
            ftitc_predicted_joules = model.Evaluate(injection.ID),
            residual_joules = model.Evaluate(injection.ID) - injection.PeakArea.Value,
        });
        var result = new {
            id,
            model = spec.ModelType.ToString(),
            generator = spec.Generator,
            injection_count = data.Injections.Count,
            peak_heat_joules = peakHeat,
            maximum_error_joules = maximumError,
            maximum_relative_error = maximumError / peakHeat,
            rows,
        };
        File.WriteAllText(Path.Combine(destination, id + ".json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
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
