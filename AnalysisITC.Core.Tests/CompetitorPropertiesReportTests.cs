using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class CompetitorPropertiesReportTests : IDisposable
{
    const string ManualReason = "Not captured: this fit used manually entered competitor properties.";
    const string MissingCaptureReason = "No usable competitor properties were saved for this fit.";
    readonly PreferencesState original = PreferencesState.FromSettings();

    public CompetitorPropertiesReportTests() => PreferencesState.Defaults().ApplyToSettings();

    public void Dispose()
    {
        original.ApplyToSettings();
        DataManager.Clear(DataClearMode.ResetSession);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReportTestProjectShowsCapturedPropertiesWithoutChangingSavedState(bool resave)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "report_test.ftxtc"));
        var containers = await FTXTCReader.ReadStream(stream);
        if (resave)
        {
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, containers.OfType<ExperimentData>(),
                containers.OfType<AnalysisResult>().ToList());
            package.Position = 0;
            containers = await FTXTCReader.ReadStream(package);
        }
        var result = containers.OfType<AnalysisResult>()
            .Single(item => item.UniqueID == "0682e19c-8fdf-4bed-ab48-a75e8ce289bd");
        var data = result.Solution.Solutions.Select(member => member.Data)
            .First(item => item.Attributes.Any(attribute => attribute.Key == AttributeKey.CompetitorResult));
        var attribute = data.Attributes.Single(item => item.Key == AttributeKey.CompetitorResult);
        var source = containers.OfType<AnalysisResult>().Single(item => item.UniqueID == attribute.StringValue);
        DataManager.AddData(source);
        var before = attribute.Copy();
        var snapshot = result.ValiditySnapshot;
        var snapshotJson = snapshot.ToJson();
        var member = result.Solution.Solutions.Single(item => item.Data == data);
        Assert.True(member.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue);
        Assert.True(member.ModelOptions[AttributeKey.PreboundLigandEnthalpy].BoolValue);
        Assert.False(FloatWithError.IsNaN(attribute.CapturedAffinity));
        Assert.False(FloatWithError.IsNaN(attribute.CapturedEnthalpy));
        foreach (var fitted in result.Solution.Solutions)
        {
            var captured = fitted.Data.Attributes.Single(item => item.Key == AttributeKey.CompetitorResult);
            var saved = snapshot.Experiments.Single(item => item.ExperimentID == fitted.Data.UniqueID)
                .Attributes.Single(item => item.Key == AttributeKey.CompetitorResult);
            AssertFixtureValues(captured.CapturedAffinity, captured.CapturedEnthalpy);
            AssertFixtureValues(saved.CapturedAffinityWithError, saved.CapturedEnthalpyWithError);
        }

        // Current fitting settings must not change the properties reported for the saved fit.
        var currentModel = new CompetitiveBinding(data);
        currentModel.InitializeParameters(data);
        currentModel.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue = false;
        currentModel.ModelOptions[AttributeKey.PreboundLigandEnthalpy].BoolValue = false;
        data.Model = currentModel;

        var values = AnalysisReportBuilder.Build(result).Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportKeyValueBlock>().SelectMany(block => block.Items)
            .Where(item => item.Label == "Competitor properties").Select(item => item.Value).ToList();

        Assert.NotEmpty(values);
        Assert.All(values, value =>
        {
            var lines = value.Split('\n');
            Assert.Equal(3, lines.Length);
            Assert.Equal("From result \"" + source.Name + "\"", lines[0]);
            Assert.StartsWith("Kd = ", lines[1]);
            Assert.StartsWith("ΔH = ", lines[2]);
            Assert.DoesNotContain("Unavailable", value);
        });
        AssertAttributeUnchanged(before, attribute);
        Assert.Same(snapshot, result.ValiditySnapshot);
        Assert.Equal(snapshotJson, result.ValiditySnapshot.ToJson());
        Assert.True(member.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue);
        Assert.True(member.ModelOptions[AttributeKey.PreboundLigandEnthalpy].BoolValue);
    }

    [Fact]
    public async Task EarlierManualFitDoesNotReportCapturesFromLaterFit()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "report_test.ftxtc"));
        var containers = await FTXTCReader.ReadStream(stream);
        var result = containers.OfType<AnalysisResult>()
            .Single(item => item.UniqueID == "738b45e0-9a9c-4f61-871c-4577be7471b2");
        var before = result.ValiditySnapshot.ToJson();
        foreach (var member in result.Solution.Solutions)
        {
            Assert.False(member.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue);
            Assert.False(member.ModelOptions[AttributeKey.PreboundLigandEnthalpy].BoolValue);
            var attribute = member.Data.Attributes.Single(item => item.Key == AttributeKey.CompetitorResult);
            AssertFixtureValues(attribute.CapturedAffinity, attribute.CapturedEnthalpy);
        }

        var values = AnalysisReportBuilder.Build(result).Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportKeyValueBlock>().SelectMany(block => block.Items)
            .Where(item => item.Label == "Competitor properties").Select(item => item.Value).ToList();

        Assert.Equal(2, values.Count);
        Assert.All(values, value => Assert.Equal(ManualReason + " (source result unavailable)", value));
        Assert.Equal(before, result.ValiditySnapshot.ToJson());
    }

    // Recorded SI values in report_test.ftxtc, independent of evaluation and formatting code.
    static void AssertFixtureValues(FloatWithError affinity, FloatWithError enthalpy)
    {
        Assert.Equal(1.5758228767824683e-6, affinity.Value);
        Assert.Equal(5.987203537335024e-8, affinity.SD);
        Assert.Equal(1.5090659511994787e-6, affinity.Lower);
        Assert.Equal(1.6440766121577854e-6, affinity.Upper);
        Assert.Equal(-32778.20240751902, enthalpy.Value);
        Assert.Equal(308.3138385831739, enthalpy.SD);
        Assert.Equal(-33126.32267871806, enthalpy.Lower);
        Assert.Equal(-32429.890807366522, enthalpy.Upper);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SourceConsumingFitIdentifiesMissingCapture(bool affinityFromAttributes, bool enthalpyFromAttributes)
    {
        var result = CreateResult("competitive");
        var member = result.Solution.Solutions.Single();
        member.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue = affinityFromAttributes;
        member.ModelOptions[AttributeKey.PreboundLigandEnthalpy].BoolValue = enthalpyFromAttributes;
        var attribute = ExperimentAttribute.CompetitorResultReference("removed-source");
        member.Data.Attributes.Add(attribute);
        var before = attribute.Copy();
        // The experiment's current model is unrelated to this saved member's inputs.
        member.Data.Model = new CompetitiveBinding(member.Data);

        var value = CompetitorProperties(AnalysisReportBuilder.Build(result));

        Assert.Equal(MissingCaptureReason + " (source result unavailable)", value);
        AssertAttributeUnchanged(before, attribute);
    }

    [Fact]
    public void SavedMissingCaptureDoesNotFallBackToCurrentAttributeOrSource()
    {
        var source = CreateResult();
        source.Name = "Source summary exists";
        DataManager.AddData(source);
        var result = CreateResult("competitive");
        var member = result.Solution.Solutions.Single();
        member.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue = true;
        var attribute = ExperimentAttribute.CompetitorResultReference(source.UniqueID);
        attribute.CapturedAffinity = new FloatWithError(double.NaN);
        attribute.CapturedEnthalpy = new FloatWithError(double.NaN);
        member.Data.Attributes.Add(attribute);
        var saved = ExperimentAttributeSnapshot.Capture(attribute);
        result.ValiditySnapshot.Experiments.Single().Attributes.Add(saved);
        attribute.CapturedAffinity = new FloatWithError(12e-9);
        attribute.CapturedEnthalpy = new FloatWithError(-35_000);
        var before = attribute.Copy();

        var value = CompetitorProperties(AnalysisReportBuilder.Build(result));

        Assert.Equal(MissingCaptureReason + " (selected source result \"Source summary exists\")", value);
        AssertAttributeUnchanged(before, attribute);
        Assert.Same(saved, result.ValiditySnapshot.Experiments.Single().Attributes.Single());
        Assert.True(double.IsNaN(saved.CapturedAffinity));
        Assert.True(double.IsNaN(saved.CapturedEnthalpy));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbsentCaptureDoesNotUseCurrentValues(bool missingSnapshot)
    {
        var result = CreateResult("competitive");
        var member = result.Solution.Solutions.Single();
        member.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue = true;
        member.ModelOptions[AttributeKey.PreboundLigandEnthalpy].BoolValue = true;
        var attribute = ExperimentAttribute.CompetitorResultReference("removed-source");
        attribute.CapturedAffinity = new FloatWithError(12e-9);
        attribute.CapturedEnthalpy = new FloatWithError(-35_000);
        member.Data.Attributes.Add(attribute);
        if (missingSnapshot) result.SetValiditySnapshot(null);
        var before = attribute.Copy();

        Assert.Equal(MissingCaptureReason + " (source result unavailable)",
            CompetitorProperties(AnalysisReportBuilder.Build(result)));
        AssertAttributeUnchanged(before, attribute);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PartialCaptureKeepsUsableValueAndExplainsTheOther(bool hasAffinity)
    {
        var result = CreateResult("competitive");
        var attribute = ExperimentAttribute.CompetitorResultReference("removed-source");
        if (hasAffinity) attribute.CapturedAffinity = new FloatWithError(12e-9);
        else attribute.CapturedEnthalpy = new FloatWithError(0);
        result.Solution.Solutions.Single().Data.Attributes.Add(attribute);
        var saved = ExperimentAttributeSnapshot.Capture(attribute);
        if (hasAffinity) saved.CapturedEnthalpy = double.NaN;
        else saved.CapturedAffinity = double.NaN;
        result.ValiditySnapshot.Experiments.Single().Attributes.Add(saved);
        var before = attribute.Copy();

        var value = CompetitorProperties(AnalysisReportBuilder.Build(result,
            new AnalysisReportOptions { EnergyUnitFamily = EnergyUnitFamily.Joules }));

        if (hasAffinity)
        {
            Assert.Contains("Kd = 12 nM", value);
            Assert.Contains("ΔH = No usable captured value", value);
        }
        else
        {
            Assert.Contains("Kd = No usable captured value", value);
            Assert.Contains("ΔH = 0", value);
            Assert.Contains("J/mol", value);
        }
        Assert.StartsWith("Source result unavailable\nKd = ", value);
        Assert.DoesNotContain("Unavailable", value);
        AssertAttributeUnchanged(before, attribute);
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    public void NonFiniteCapturesReceiveTheMissingCaptureExplanation(double nonFinite)
    {
        var result = CreateResult("competitive");
        var member = result.Solution.Solutions.Single();
        member.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue = true;
        var attribute = ExperimentAttribute.CompetitorResultReference("removed-source");
        attribute.CapturedAffinity = new FloatWithError(nonFinite);
        attribute.CapturedEnthalpy = new FloatWithError(nonFinite);
        member.Data.Attributes.Add(attribute);
        result.ValiditySnapshot.Experiments.Single().Attributes.Add(ExperimentAttributeSnapshot.Capture(attribute));

        Assert.Equal(MissingCaptureReason + " (source result unavailable)",
            CompetitorProperties(AnalysisReportBuilder.Build(result)));
    }

    [Fact]
    public void NoncompetitiveFitExplainsThatCompetitorPropertiesWereNotUsed()
    {
        var result = CreateResult();
        result.Solution.Solutions.Single().Data.Attributes.Add(ExperimentAttribute.CompetitorResultReference(null));

        Assert.Equal("Not captured: this fit did not use competitor properties. (source result not recorded)",
            CompetitorProperties(AnalysisReportBuilder.Build(result)));
    }

    [Fact]
    public void SupportingExperimentExplainsFitTimeCaptureEvenWithAnAttachedFit()
    {
        var result = CreateResult();
        var supporting = InjectionProcessingMethodTests.FittedModel("competitive").Data;
        var attribute = ExperimentAttribute.CompetitorResultReference("removed-source");
        supporting.Attributes.Add(attribute);
        var before = attribute.Copy();
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        report.SetSupportingExperimentIds(new[] { supporting.UniqueID });

        var document = AnalysisReportBuilder.Build(report, _ => result, _ => supporting);

        Assert.Equal("Not captured: source properties are saved when used by a fit. (source result unavailable)",
            CompetitorProperties(document));
        AssertAttributeUnchanged(before, attribute);
    }

    [Fact]
    public void CondensedDetailsUseTheSavedRepeatedMembersExplanation()
    {
        var first = CreateResult();
        var repeated = CreateResult("competitive");
        var data = repeated.Solution.Solutions.Single().Data;
        data.SetID(first.Solution.Solutions.Single().Data.UniqueID);
        data.Attributes.Add(ExperimentAttribute.CompetitorResultReference("removed-source"));
        var report = new AnalysisReport();
        report.SetResultIds(new[] { first.UniqueID, repeated.UniqueID });

        var document = AnalysisReportBuilder.Build(report,
            id => id == first.UniqueID ? first : repeated, _ => null,
            new AnalysisReportOptions { CondenseRepeatedExperiments = true });
        var condensed = document.Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details — condensed");

        Assert.Equal(ManualReason + " (source result unavailable)",
            Assert.Single(condensed.Items, item => item.Label == "Competitor properties").Value);
    }

    static string CompetitorProperties(AnalysisReportDocument document) => Assert.Single(
        document.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
            .SelectMany(block => block.Items), item => item.Label == "Competitor properties").Value;

    static void AssertAttributeUnchanged(ExperimentAttribute before, ExperimentAttribute after)
    {
        Assert.True(ExperimentAttributeSnapshot.Capture(before).EquivalentTo(ExperimentAttributeSnapshot.Capture(after)));
        Assert.Equal(FloatWithError.IsNaN(before.ParameterValue), FloatWithError.IsNaN(after.ParameterValue));
        Assert.Equal(FloatWithError.IsNaN(before.CapturedAffinity), FloatWithError.IsNaN(after.CapturedAffinity));
        Assert.Equal(FloatWithError.IsNaN(before.CapturedEnthalpy), FloatWithError.IsNaN(after.CapturedEnthalpy));
    }

    static AnalysisResult CreateResult(string modelId = "one-c100-v0.01")
    {
        var member = InjectionProcessingMethodTests.FittedModel(modelId);
        member.Data.SetID(Guid.NewGuid().ToString("N"));
        var model = new GlobalModel(new() { member })
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        model.Parameters.AddIndivdualParameter(member.Parameters);
        var solution = new GlobalSolution(new GlobalSolver { Model = model }, new() { member.Solution },
            member.Solution.Convergence, reconstructBootstrap: false);
        model.Solution = solution;
        var result = new AnalysisResult(solution);
        result.SetID(Guid.NewGuid().ToString("N"));
        return result;
    }
}
