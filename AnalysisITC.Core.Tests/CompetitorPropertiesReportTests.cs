using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
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

    [Fact]
    public async Task ReportTestProjectExplainsManualInputsWithoutChangingSavedState()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "report_test.ftxtc"));
        var containers = await FTXTCReader.ReadStream(stream);
        var result = containers.OfType<AnalysisResult>()
            .Single(item => item.UniqueID == "738b45e0-9a9c-4f61-871c-4577be7471b2");
        var data = result.Solution.Solutions.Select(member => member.Data)
            .Single(item => item.Attributes.Any(attribute => attribute.Key == AttributeKey.CompetitorResult));
        var attribute = data.Attributes.Single(item => item.Key == AttributeKey.CompetitorResult);
        var source = containers.OfType<AnalysisResult>().Single(item => item.UniqueID == attribute.StringValue);
        DataManager.AddData(source);
        var before = attribute.Copy();
        var snapshot = result.ValiditySnapshot;
        var snapshotJson = snapshot.ToJson();
        var member = result.Solution.Solutions.Single(item => item.Data == data);
        Assert.False(member.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue);
        Assert.False(member.ModelOptions[AttributeKey.PreboundLigandEnthalpy].BoolValue);
        Assert.True(FloatWithError.IsNaN(attribute.CapturedAffinity));
        Assert.True(FloatWithError.IsNaN(attribute.CapturedEnthalpy));

        // Current fitting settings must not change the explanation of the saved fit.
        var currentModel = new CompetitiveBinding(data);
        currentModel.InitializeParameters(data);
        currentModel.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue = true;
        currentModel.ModelOptions[AttributeKey.PreboundLigandEnthalpy].BoolValue = true;
        data.Model = currentModel;

        var value = CompetitorProperties(AnalysisReportBuilder.Build(result));

        Assert.Equal(ManualReason + " (selected source result \"" + source.Name + "\")", value);
        Assert.DoesNotContain("Kd =", value);
        Assert.DoesNotContain("Unavailable", value);
        AssertAttributeUnchanged(before, attribute);
        Assert.Same(snapshot, result.ValiditySnapshot);
        Assert.Equal(snapshotJson, result.ValiditySnapshot.ToJson());
        Assert.False(member.ModelOptions[AttributeKey.PreboundLigandAffinity].BoolValue);
        Assert.False(member.ModelOptions[AttributeKey.PreboundLigandEnthalpy].BoolValue);
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
    [InlineData(true)]
    [InlineData(false)]
    public void PartialCaptureKeepsUsableValueAndExplainsTheOther(bool hasAffinity)
    {
        var result = CreateResult("competitive");
        var attribute = ExperimentAttribute.CompetitorResultReference("removed-source");
        if (hasAffinity) attribute.CapturedAffinity = new FloatWithError(12e-9);
        else attribute.CapturedEnthalpy = new FloatWithError(0);
        result.Solution.Solutions.Single().Data.Attributes.Add(attribute);
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
        Assert.Contains("source result unavailable", value);
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
