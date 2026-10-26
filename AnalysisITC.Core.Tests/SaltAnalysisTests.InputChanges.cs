using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Viewer;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed partial class SaltAnalysisTests
{
    [Fact]
    public async Task EditedConcentrationsInvalidateBeforeSaveAndRecalculateFromFourToTwo()
    {
        var result = PowerLawResult();
        var analysis = result.ElectrostaticsAnalysis;
        Assert.True(await analysis.PerformAnalysisAsync());
        Assert.Equal(4, analysis.CounterIonRelease.Value, 10);
        for (var i = 0; i < 4; i++)
        {
            var concentration = .025 * Math.Pow(4, i);
            EditSaltInputs(result.Solution.Solutions[i].Data, attributes =>
                attributes.Single(a => a.Key == AttributeKey.Salt).ParameterValue = new(concentration));
        }

        // Read raw outputs first: callers cannot bypass availability and get the old fit.
        Assert.Null(analysis.CounterIonReleaseFit);
        Assert.True(FloatWithError.IsNaN(analysis.CounterIonRelease));
        Assert.Null(analysis.IonicStrengthDependenceFit);
        Assert.Equal(0, analysis.CounterIonReleaseIterations);
        Assert.Equal(0, analysis.CompletedIterations);
        Assert.False(analysis.Calculated);
        Assert.Equal("Inputs changed. Recalculate the analysis.", analysis.CounterIonUnavailableReason);
        Assert.Equal(AnalysisResultValidity.Valid, result.ValidityReport.Status);
        AssertCurrentSaltConsumers(result, false, false);

        using var saved = await Save(result);
        Assert.Null(ReadElectrostaticsPayload(saved));
        saved.Position = 0;
        var restored = Assert.Single((await FTXTCReader.ReadWithRecovery(saved, FtxtcReadPolicy.Strict)).Containers.OfType<AnalysisResult>());
        Assert.False(restored.ElectrostaticsAnalysis.Calculated);
        Assert.Equal(AnalysisResultValidity.Valid, restored.ValidityReport.Status);
        Assert.True(await restored.ElectrostaticsAnalysis.PerformAnalysisAsync());
        // Kd grows sixteenfold per step; concentration now grows fourfold: slope = 2.
        Assert.Equal(2, restored.ElectrostaticsAnalysis.CounterIonRelease.Value, 10);
        using var recalculated = await Save(restored);
        var reopened = Assert.Single((await FTXTCReader.ReadWithRecovery(recalculated, FtxtcReadPolicy.Strict)).Containers.OfType<AnalysisResult>());
        Assert.Equal(2, reopened.ElectrostaticsAnalysis.CounterIonRelease.Value, 10);
    }

    [Theory]
    [InlineData("explicit-strength", false, true)]
    [InlineData("salt-with-explicit-strength", true, false)]
    [InlineData("buffer-included", false, true)]
    [InlineData("buffer-excluded", true, true)]
    [InlineData("pH-included", false, true)]
    [InlineData("pH-excluded", true, true)]
    [InlineData("tiny-salt-change", false, false)]
    [InlineData("identity", true, false)]
    [InlineData("mixture", false, false)]
    [InlineData("missing", false, false)]
    [InlineData("insufficient", false, false)]
    [InlineData("affinity-uncertainty", false, false)]
    [InlineData("affinity-interval", false, false)]
    public async Task InputChangesInvalidateOnlyDependentComponents(string change, bool ionic, bool slope)
    {
        AppSettings.IncludeBufferInIonicStrengthCalc = change == "buffer-included" || change == "pH-included";
        var result = Create(new[] { .025, .05, .1, .2 },
            explicitStrength: change.Contains("explicit-strength") || change == "explicit-strength", buffer: true);
        var analysis = result.ElectrostaticsAnalysis;
        Assert.True(await analysis.PerformAnalysisAsync());
        var data = result.Solution.Solutions[0].Data;
        if (change == "affinity-uncertainty")
            result.Solution.Solutions[0].Parameters[ParameterType.Affinity1] = new(result.Solution.Solutions[0].Parameters[ParameterType.Affinity1].Value, .1);
        else if (change == "affinity-interval")
        {
            var affinity = result.Solution.Solutions[0].Parameters[ParameterType.Affinity1].Value;
            result.Solution.Solutions[0].Parameters[ParameterType.Affinity1] = new(affinity, 0, affinity - .1, affinity + .2);
        }
        else if (change == "insufficient")
            foreach (var member in result.Solution.Solutions)
                EditSaltInputs(member.Data, attributes => attributes.Single(a => a.Key == AttributeKey.Salt).ParameterValue = new(.05));
        else
            EditSaltInputs(data, attributes =>
            {
                var salt = attributes.Single(a => a.Key == AttributeKey.Salt);
                switch (change)
                {
                    case "explicit-strength": attributes.Single(a => a.Key == AttributeKey.IonicStrength).ParameterValue = new(.03); break;
                    case "salt-with-explicit-strength": salt.ParameterValue = new(.03); break;
                    case "buffer-included":
                    case "buffer-excluded": attributes.Single(a => a.Key == AttributeKey.Buffer).ParameterValue = new(.02); break;
                    case "pH-included":
                    case "pH-excluded": attributes.Single(a => a.Key == AttributeKey.Buffer).DoubleValue = 7.6; break;
                    case "tiny-salt-change": salt.ParameterValue = new(.025 + 1e-13); break;
                    case "identity": salt.IntValue = (int)Salt.KCl; break;
                    case "mixture": attributes.Add(SaltEntry(Salt.KCl, .01)); break;
                    case "missing": attributes.Remove(salt); break;
                }
            });

        AssertCurrentSaltConsumers(result, ionic, slope);
        Assert.Equal(ionic, analysis.IonicStrengthDependenceFit != null);
        Assert.Equal(slope, analysis.CounterIonReleaseFit != null);
        Assert.Equal(ionic ? 8 : 0, analysis.CompletedIterations);
        Assert.Equal(slope ? 8 : 0, analysis.CounterIonReleaseIterations);
        using var saved = await Save(result);
        var restored = Assert.Single((await FTXTCReader.ReadWithRecovery(saved, FtxtcReadPolicy.Strict)).Containers.OfType<AnalysisResult>());
        Assert.Equal(ionic, restored.ElectrostaticsAnalysis?.HasIonicStrengthResult == true);
        Assert.Equal(slope, restored.ElectrostaticsAnalysis?.HasCounterIonResult == true);
    }

    [Fact]
    public async Task EquivalentInputsAndDisplayChangesPreserveCompletedOutputs()
    {
        var result = Create(new[] { .025, .05, .1, .2 }, buffer: true);
        var analysis = result.ElectrostaticsAnalysis;
        Assert.True(await analysis.PerformAnalysisAsync());
        var fit = analysis.CounterIonReleaseFit;
        var ionicFit = analysis.IonicStrengthDependenceFit;
        var data = result.Solution.Solutions[0].Data;
        Assert.False(data.UpdateDetailAttributes(data.Attributes.Select(a => a.Copy())));
        EditSaltInputs(data, attributes =>
        {
            attributes.Single(a => a.Key == AttributeKey.Salt).ParameterValue = new(.0125);
            attributes.Add(SaltEntry(Salt.NaCl, .0125));
            attributes.Reverse();
        });
        data.Name = "renamed";
        data.Comments = "unrelated";
        AppSettings.IncludeBufferInIonicStrengthCalc = true;
        AppSettings.MinimumIonSpanForFitting = 1;
        Assert.Same(fit, analysis.CounterIonReleaseFit);
        Assert.Same(ionicFit, analysis.IonicStrengthDependenceFit);
    }

    [Fact]
    public async Task ReorderingSaltEntriesDoesNotChangeEffectiveFloatingPointInputs()
    {
        var result = Create(new[] { .6, .7, .8, .9 });
        var data = result.Solution.Solutions[0].Data;
        data.Attributes.Clear();
        foreach (var concentration in new[] { .1, .2, .3 }) data.Attributes.Add(SaltEntry(Salt.NaCl, concentration));
        var analysis = result.ElectrostaticsAnalysis;
        Assert.True(await analysis.PerformAnalysisAsync());
        var slope = analysis.CounterIonReleaseFit;
        var ionic = analysis.IonicStrengthDependenceFit;
        EditSaltInputs(data, attributes => attributes.Reverse());
        Assert.Same(slope, analysis.CounterIonReleaseFit);
        Assert.Same(ionic, analysis.IonicStrengthDependenceFit);
    }

    [Fact]
    public async Task EveryResultSharingAnExperimentChecksItsOwnInputs()
    {
        var first = PowerLawResult();
        var second = new AnalysisResult(first.Solution);
        Assert.True(await first.ElectrostaticsAnalysis.PerformAnalysisAsync());
        Assert.True(await second.ElectrostaticsAnalysis.PerformAnalysisAsync());
        EditSaltInputs(first.Solution.Solutions[0].Data, attributes =>
            attributes.Single(a => a.Key == AttributeKey.Salt).ParameterValue = new(.03));
        Assert.False(first.ElectrostaticsAnalysis.HasCounterIonResult);
        Assert.False(second.ElectrostaticsAnalysis.HasCounterIonResult);
    }

    [Fact]
    public async Task ExplicitRunRefreshesEligibilityAfterMetadataEdit()
    {
        var result = PowerLawResult();
        var analysis = result.ElectrostaticsAnalysis;
        Assert.True(await analysis.PerformAnalysisAsync());
        foreach (var member in result.Solution.Solutions)
            EditSaltInputs(member.Data, attributes => attributes.Single(a => a.Key == AttributeKey.Salt).ParameterValue = new(.05));
        Assert.False(await analysis.PerformAnalysisAsync());
        Assert.False(result.IsElectrostaticsAnalysisDependenceEnabled);
        for (var i = 0; i < 4; i++)
        {
            var concentration = .025 * Math.Pow(4, i);
            EditSaltInputs(result.Solution.Solutions[i].Data, attributes =>
                attributes.Single(a => a.Key == AttributeKey.Salt).ParameterValue = new(concentration));
        }
        Assert.True(await analysis.PerformAnalysisAsync());
        Assert.True(result.IsElectrostaticsAnalysisDependenceEnabled);
        Assert.Equal(2, analysis.CounterIonRelease.Value, 10);
    }

    [Fact]
    public async Task MetadataEditsCanEnableAnInitiallyUnavailableAnalysis()
    {
        var result = Create(new[] { .05, .05, .05 });
        Assert.Null(result.ElectrostaticsAnalysis);
        EditSaltInputs(result.Solution.Solutions[0].Data, attributes => attributes.Single(a => a.Key == AttributeKey.Salt).ParameterValue = new(.025));
        EditSaltInputs(result.Solution.Solutions[2].Data, attributes => attributes.Single(a => a.Key == AttributeKey.Salt).ParameterValue = new(.1));
        Assert.True(result.IsElectrostaticsAnalysisDependenceEnabled);
        Assert.NotNull(result.ElectrostaticsAnalysis);
        Assert.True(await result.ElectrostaticsAnalysis.PerformAnalysisAsync());
    }

    [Fact]
    public async Task SaveChecksRestoredInputsWithoutFirstQueryingOutputs()
    {
        var result = PowerLawResult();
        Assert.True(await result.ElectrostaticsAnalysis.PerformAnalysisAsync());
        using var original = await Save(result);
        var restored = Assert.Single((await FTXTCReader.ReadWithRecovery(original, FtxtcReadPolicy.Strict)).Containers.OfType<AnalysisResult>());
        Assert.False(restored.IsModified);
        EditSaltInputs(restored.Solution.Solutions[0].Data, attributes => attributes.Single(a => a.Key == AttributeKey.Salt).ParameterValue = new(.03));
        using var saved = await Save(restored);
        Assert.Null(ReadElectrostaticsPayload(saved));
    }

    [Theory]
    [InlineData("success", false)]
    [InlineData("cancel", false)]
    [InlineData("failure", false)]
    [InlineData("success", true)]
    [InlineData("cancel", true)]
    [InlineData("failure", true)]
    public async Task InputEditDuringRunCannotPublishOrRestoreOldOutputs(string outcome, bool explicitStrength)
    {
        var result = PowerLawResult(explicitStrength);
        var analysis = new EditedDuringSaltRun(result, outcome);
        analysis.RestoreResult(new(new(1e-6), new(2), new(0)), new(new(4), new(-12), 0),
            8, 8, DateTime.UtcNow, ErrorEstimationMethod.None, false);
        try
        {
            Assert.Equal(outcome == "success", await analysis.PerformAnalysisAsync());
            Assert.False(analysis.HasCounterIonResult);
            Assert.Equal(explicitStrength, analysis.HasIonicStrengthResult);
            Assert.Null(analysis.CounterIonReleaseFit);
            Assert.Equal(explicitStrength ? 8 : 0, analysis.CompletedIterations);
            Assert.Equal(explicitStrength, analysis.CompletedAtUtc.HasValue);
        }
        finally { ResultAnalysisController.TerminateAnalysisFlag.Lower(); }
    }

    sealed class EditedDuringSaltRun(AnalysisResult result, string outcome) : ElectrostaticsAnalysis(result)
    {
        protected override void Calculate()
        {
            base.Calculate();
            EditSaltInputs(Data.Solution.Solutions[0].Data, attributes =>
                attributes.Single(a => a.Key == AttributeKey.Salt).ParameterValue = new(.03));
            if (outcome == "cancel") ResultAnalysisController.TerminateAnalysisFlag.Raise();
            if (outcome == "failure") throw new InvalidOperationException("Input changed before failed completion.");
        }
    }

    static AnalysisResult PowerLawResult(bool explicitStrength = false)
    {
        var result = Create(new[] { .025, .05, .1, .2 }, explicitStrength: explicitStrength);
        for (var i = 0; i < 4; i++)
            result.Solution.Solutions[i].Parameters[ParameterType.Affinity1] = new(-Math.Log10(1e-7 * Math.Pow(16, i)));
        result.UpdateSolution(result.Solution);
        return result;
    }

    static void EditSaltInputs(ExperimentData data, Action<System.Collections.Generic.List<ExperimentAttribute>> edit)
    {
        var attributes = data.Attributes.Select(a => a.Copy()).ToList();
        edit(attributes);
        if (data.UpdateDetailAttributes(attributes)) data.UpdateProcessing();
    }

    static void AssertCurrentSaltConsumers(AnalysisResult result, bool ionic, bool slope)
    {
        var analysis = result.ElectrostaticsAnalysis;
        Assert.Equal(ionic, analysis.HasIonicStrengthResult);
        Assert.Equal(slope, analysis.HasCounterIonResult);
        var sections = AnalysisReportBuilder.GetAvailableAdvancedSections(result).Select(s => s.Request.Kind).ToArray();
        Assert.Equal(ionic, sections.Contains(AnalysisReportAdvancedSectionKind.DebyeHuckel));
        Assert.Equal(slope, sections.Contains(AnalysisReportAdvancedSectionKind.CounterIonRelease));
        var options = new AnalysisReportOptions();
        options.AdvancedSections.Add(new(AnalysisReportAdvancedSectionKind.DebyeHuckel));
        options.AdvancedSections.Add(new(AnalysisReportAdvancedSectionKind.CounterIonRelease));
        var plots = AnalysisReportBuilder.Build(result, options).Sections.SelectMany(s => s.Blocks).OfType<AnalysisReportPlotBlock>();
        Assert.Equal(ionic, plots.Any(p => p.Title == "Debye-Huckel dependence" && p.Series.Any(s => s.Kind == AnalysisReportPlotSeriesKind.Line)));
        Assert.Equal(slope, plots.Any(p => p.Title.StartsWith("Salt dependence") && p.Series.Any(s => s.Kind == AnalysisReportPlotSeriesKind.Line)));
        var report = new AnalysisReport();
        report.SetResultIds(new[] { result.UniqueID });
        var evidence = AnalysisInterpretationPackageBuilder.Build(report, result).Result.AdvancedAnalyses.SingleOrDefault(a => a.Type == "electrostatics");
        Assert.Equal(ionic, evidence?.IonicStrengthAvailable == true);
        Assert.Equal(slope, evidence?.CounterIonReleaseAvailable == true);
        var viewer = (ViewerElectrostaticsDto)typeof(ViewerDocumentReader).GetMethod("BuildElectrostatics", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { analysis });
        Assert.Equal(ionic, viewer.Kd0Micromolar != null);
        Assert.Equal(slope, viewer.CounterIonRelease != null);
        Assert.Equal(ionic, viewer.Plots.Single(p => p.Key == "debye-huckel").Series.Any(s => s.Kind == "line"));
        Assert.Equal(slope, viewer.Plots.Single(p => p.Key == "counter-ion-release").Series.Any(s => s.Kind == "line"));
    }
}
