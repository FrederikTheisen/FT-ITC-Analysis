using System;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Viewer;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class SaltAnalysisTests
{
    [Theory]
    [InlineData(Salt.NaCl, 4)]
    [InlineData(Salt.MgCl2, 2)]
    [InlineData(Salt.Na2SO4, 4)]
    [InlineData(Salt.MgSO4, -2)]
    [InlineData(Salt.KCl, 0)]
    public async Task ConcentrationConventionRecoversIndependentPowerLaw(Salt salt, double exponent)
    {
        // Each doubling of concentration multiplies Kd by 2^exponent.
        // Input values do not use the analysis's coordinate or regression helpers.
        double[] concentrations = { .025, .05, .1, .2 };
        var result = Create(concentrations);
        for (var i = 0; i < concentrations.Length; i++)
        {
            result.Solution.Solutions[i].Data.Attributes.Single(a => a.Key == AttributeKey.Salt).IntValue = (int)salt;
            var kd = 1e-7 * Math.Pow(2, i * exponent);
            result.Solution.Solutions[i].Parameters[ParameterType.Affinity1] = new(-Math.Log10(kd));
        }
        result.UpdateSolution(result.Solution);
        var analysis = result.ElectrostaticsAnalysis;
        Assert.True(await analysis.PerformAnalysisAsync());
        Assert.Equal(exponent, analysis.CounterIonRelease.Value, 10);
        var points = analysis.GetDataPoints(ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease);
        Assert.Equal(concentrations.Length, points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            Assert.Equal(Math.Log(concentrations[i]), points[i].Item1, 12);
            Assert.Equal(Math.Log(1e-7) + i * exponent * Math.Log(2), points[i].Item2.Value, 11);
        }
    }

    [Theory]
    [InlineData("mixture")]
    [InlineData("identity")]
    [InlineData("missing")]
    [InlineData("negative")]
    [InlineData("nonfinite")]
    public async Task ConcentrationConventionRejectsWholeSeriesWithoutDroppingMember(string invalid)
    {
        var result = Create(new[] { .025, .05, .1, .2 });
        var attributes = result.Solution.Solutions[0].Data.Attributes;
        var salt = attributes.Single(a => a.Key == AttributeKey.Salt);
        if (invalid == "mixture") attributes.Add(SaltEntry(Salt.MgCl2, .01));
        else if (invalid == "identity") salt.IntValue = (int)Salt.KCl;
        else if (invalid == "missing") attributes.Remove(salt);
        else salt.ParameterValue = new(invalid == "negative" ? -.025 : double.NaN);
        // Keep the established outer gate captured before the metadata change,
        // so this exercises the slope component's own whole-collection check.
        var analysis = result.ElectrostaticsAnalysis;
        Assert.True(await analysis.PerformAnalysisAsync());
        Assert.False(analysis.HasCounterIonResult);
        Assert.Null(analysis.CounterIonReleaseFit);
        Assert.Equal(0, analysis.CounterIonReleaseIterations);
        Assert.Empty(analysis.GetDataPoints(ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease));
        Assert.True(analysis.HasIonicStrengthResult);
    }

    [Fact]
    public async Task ConcentrationConventionAddsSameSaltAndPreservesZeroAndRepeatedObservations()
    {
        var result = Create(new[] { 0d, .025, .05, .05, .1 });
        var expected = new[] { .05, .1, .1, .2 };
        foreach (var member in result.Solution.Solutions)
        {
            var c = member.Data.Attributes.Single(a => a.Key == AttributeKey.Salt).ParameterValue.Value;
            member.Data.Attributes.Add(SaltEntry(Salt.NaCl, c));
            member.Data.Attributes.Add(SaltEntry(Salt.KCl, 0));
            member.Parameters[ParameterType.Affinity1] = new(c == 0 ? 7 : -Math.Log10(1e-7 * Math.Pow(2 * c / .05, 4)));
        }
        result.UpdateSolution(result.Solution);
        Assert.True(await result.ElectrostaticsAnalysis.PerformAnalysisAsync());
        Assert.Equal(4, result.ElectrostaticsAnalysis.CounterIonRelease.Value, 10);
        var points = result.ElectrostaticsAnalysis.GetDataPoints(ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease);
        Assert.Equal(expected.Select(c => Math.Log(c)), points.Select(p => p.Item1), new DoublePrecisionComparer(12));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcentrationConventionVersionOneDropsOnlyObsoleteSlopeAndCanRecalculate(bool missingVersion)
    {
        var result = Create(new[] { .05, .1, .15 });
        var analysis = result.ElectrostaticsAnalysis;
        analysis.RestoreResult(new(new(2e-6, 1e-7), new(2), new(0)),
            new(new(4, .2, 3.7, 4.5), new(-12, .1), 0),
            8, 7, DateTime.UtcNow, ErrorEstimationMethod.None, false);
        var report = new AnalysisReport { AuthorComments = "Retain the saved discussion." };
        report.SetResultIds(new[] { result.UniqueID });
        var prompt = AnalysisInterpretationPromptBuilder.Build(AnalysisInterpretationPackageBuilder.Build(report, result));
        report.ApproveInterpretation(new AnalysisInterpretationRecord
        {
            InputFingerprint = prompt.InputFingerprint,
            EvidenceFingerprintScheme = AnalysisInterpretationPromptBuilder.EvidenceFingerprintScheme,
            InterpretationMarkdown = "## Overall interpretation\nStored scientific discussion.",
        });
        Assert.Equal(AnalysisInterpretationFreshness.Current, AnalysisInterpretationService.EvaluateFreshness(report, result).Status);
        using var saved = new MemoryStream();
        await FTXTCWriter.WriteStream(saved, result.Solution.Solutions.Select(member => member.Data), new[] { result }, reports: new[] { report });
        Assert.Equal(2, ReadElectrostaticsPayload(saved)["schemaVersion"].GetValue<int>());
        using var old = FTXTCFormatTests.RewriteAuthenticatedPackage(saved, (path, bytes) =>
        {
            if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
            var root = JsonNode.Parse(bytes);
            if (missingVersion) root["advancedAnalyses"]["electrostatics"].AsObject().Remove("schemaVersion");
            else root["advancedAnalyses"]["electrostatics"]["schemaVersion"] = 1;
            return Encoding.UTF8.GetBytes(root.ToJsonString(FTXTCFormat.JsonOptions));
        }, schemaMinor: 6);
        foreach (var policy in new[] { FtxtcReadPolicy.Strict, FtxtcReadPolicy.RecoverUsableContent })
        {
            old.Position = 0;
            var read = await FTXTCReader.ReadWithRecovery(old, policy);
            Assert.Empty(read.Issues);
            var restored = Assert.Single(read.Containers.OfType<AnalysisResult>());
            Assert.Equal(result.Solution.Solutions.Count, restored.Solution.Solutions.Count);
            for (var i = 0; i < result.Solution.Solutions.Count; i++)
                foreach (var parameter in result.Solution.Solutions[i].Parameters)
                    SameEstimate(parameter.Value, restored.Solution.Solutions[i].Parameters[parameter.Key]);
            var restoredReport = Assert.Single(read.Reports);
            Assert.Equal(report.AuthorComments, restoredReport.AuthorComments);
            Assert.Equal(report.ApprovedInterpretation.InterpretationMarkdown, restoredReport.ApprovedInterpretation.InterpretationMarkdown);
            Assert.Equal(AnalysisInterpretationFreshness.Stale, AnalysisInterpretationService.EvaluateFreshness(restoredReport, restored).Status);
            SameEstimate(analysis.Kd0, restored.ElectrostaticsAnalysis.Kd0);
            Assert.Equal(8, restored.ElectrostaticsAnalysis.CompletedIterations);
            Assert.False(restored.ElectrostaticsAnalysis.HasCounterIonResult);
            Assert.Equal(0, restored.ElectrostaticsAnalysis.CounterIonReleaseIterations);
            Assert.Contains("Recalculate", restored.ElectrostaticsAnalysis.CounterIonUnavailableReason);
            using var resaved = await Save(restored);
            Assert.Null(ReadElectrostaticsPayload(resaved)["counterIonReleaseFit"]);
            Assert.True(await restored.ElectrostaticsAnalysis.PerformAnalysisAsync());
            Assert.True(restored.ElectrostaticsAnalysis.HasCounterIonResult);
            using var recalculated = await Save(restored);
            Assert.Equal(2, ReadElectrostaticsPayload(recalculated)["schemaVersion"].GetValue<int>());
        }
    }

    [Fact]
    public void ConcentrationConventionPreservesIntervalsAcrossReportViewerAndEvidence()
    {
        var result = Create(new[] { .05, .1, .15 });
        foreach (var solution in result.Solution.Solutions)
            solution.Parameters[ParameterType.Affinity1] = new(6, .1, 5.8, 6.4);
        var analysis = result.ElectrostaticsAnalysis;
        analysis.RestoreResult(new(new(2e-6), new(2), new(0)), new(new(4, .2, 3.7, 4.5), new(-12, .1), 0),
            8, 7, DateTime.UtcNow, ErrorEstimationMethod.None, false);
        var points = analysis.GetSaltDependencePoints();
        var options = new AnalysisReportOptions();
        options.AdvancedSections.Add(new(AnalysisReportAdvancedSectionKind.CounterIonRelease));
        var plot = Assert.Single(AnalysisReportBuilder.Build(result, options).Sections.SelectMany(section => section.Blocks)
            .OfType<AnalysisReportPlotBlock>(), item => item.Title == "Salt dependence (NaCl)");
        Assert.Equal("ln(Salt concentration / M)", plot.XAxisTitle);
        Assert.Equal("ln(Kd / M)", plot.YAxisTitle);
        var reportPoints = Assert.Single(plot.Series, series => series.Kind == AnalysisReportPlotSeriesKind.Points).Points;
        var viewer = (ViewerElectrostaticsDto)typeof(ViewerDocumentReader).GetMethod("BuildElectrostatics", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { analysis });
        var viewerPlot = Assert.Single(viewer.Plots, item => item.Key == "counter-ion-release");
        Assert.Equal(plot.Title, viewerPlot.Title);
        Assert.Equal(plot.XAxisTitle, viewerPlot.XAxisLabel);
        Assert.Equal(plot.YAxisTitle, viewerPlot.YAxisLabel);
        Assert.Equal("Salt-dependence slope", viewer.SaltDependenceLabel);
        var viewerPoints = Assert.Single(viewerPlot.Series, series => series.Kind == "points");
        for (var i = 0; i < points.Count; i++)
        {
            Assert.Equal(Math.Log(new[] { .05, .1, .15 }[i]), points[i].X, 12);
            Assert.Equal(-6 * Math.Log(10), points[i].Y.Value, 12);
            Assert.Equal(-6.4 * Math.Log(10), points[i].Y.Lower, 12);
            Assert.Equal(-5.8 * Math.Log(10), points[i].Y.Upper, 12);
            Assert.Equal(points[i].X, reportPoints[i].X);
            Assert.Equal(points[i].X, viewerPoints.X[i]);
            Assert.Equal(points[i].Y.Lower, reportPoints[i].Lower);
            Assert.Equal(points[i].Y.Upper, reportPoints[i].Upper);
            Assert.Equal(points[i].Y.Lower, viewerPoints.Lower[i]);
            Assert.Equal(points[i].Y.Upper, viewerPoints.Upper[i]);
        }
        var report = new AnalysisReport(); report.SetResultIds(new[] { result.UniqueID });
        var evidence = Assert.Single(AnalysisInterpretationPackageBuilder.Build(report, result).Result.AdvancedAnalyses, item => item.Type == "electrostatics");
        Assert.Equal("ln-salt-concentration", evidence.XAxis);
        Assert.Equal("ln-dissociation-constant", evidence.YAxis);
        Assert.Equal("salt-dependence-slope", evidence.SlopeParameter);
        var slope = Assert.Single(evidence.Values, item => item.Id == "salt-dependence-slope");
        Assert.Equal(4, slope.Value); Assert.Equal(3.7, slope.Confidence95Lower); Assert.Equal(4.5, slope.Confidence95Upper);
    }

    [Fact]
    public async Task ConcentrationConventionKeepsIonicStrengthPolicyIndependent()
    {
        var result = Create(new[] { .025, .05, .1, .2 }, buffer: true);
        foreach (var solution in result.Solution.Solutions)
            solution.Data.Attributes.Single(a => a.Key == AttributeKey.Salt).IntValue = (int)Salt.MgCl2;
        result.UpdateSolution(result.Solution);
        Assert.True(await result.ElectrostaticsAnalysis.PerformAnalysisAsync());
        var withoutBuffer = result.ElectrostaticsAnalysis;
        var slope = withoutBuffer.CounterIonRelease.Value;
        Assert.Equal(new[] { .075, .15, .3, .6 }, result.Solution.Solutions.Select(member => withoutBuffer.GetPlotIonicStrength(member.Data)), new DoublePrecisionComparer(12));
        AppSettings.IncludeBufferInIonicStrengthCalc = true;
        result.UpdateSolution(result.Solution);
        Assert.True(await result.ElectrostaticsAnalysis.PerformAnalysisAsync());
        Assert.Equal(slope, result.ElectrostaticsAnalysis.CounterIonRelease.Value, 12);
        Assert.All(result.Solution.Solutions, member => Assert.Equal(
            withoutBuffer.GetPlotIonicStrength(member.Data) + BufferContribution,
            result.ElectrostaticsAnalysis.GetPlotIonicStrength(member.Data), 12));
    }

    static ExperimentAttribute SaltEntry(Salt salt, double concentration)
    {
        var attribute = ExperimentAttribute.FromKey(AttributeKey.Salt);
        attribute.IntValue = (int)salt;
        attribute.ParameterValue = new(concentration);
        return attribute;
    }
}
