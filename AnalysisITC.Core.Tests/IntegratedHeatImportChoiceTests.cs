using System;
using System.IO;
using System.Linq;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection(FileTypeFixtureCollectionDefinition.Name)]
public sealed class IntegratedHeatImportChoiceTests : IDisposable
{
    private readonly bool originalReprocess = AppSettings.ReprocessIntegratedHeatDataOnLoad;
    private readonly PromptService prompt = new();

    public IntegratedHeatImportChoiceTests()
    {
        IntegratedHeatReader.BeginImportQueue();
        PlatformServices.RegisterImportPromptService(prompt);
    }

    public void Dispose()
    {
        AppSettings.ReprocessIntegratedHeatDataOnLoad = originalReprocess;
        IntegratedHeatReader.EndImportQueue();
        PlatformServices.RegisterImportPromptService(null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DelimitedPromptChoiceControlsReprocessingWithoutChangingHeatsOrFirstInjection(bool reprocess)
    {
        prompt.ReprocessChoice = reprocess;
        var path = Fixture("FileTypeTests", "CURVE-1.aff");

        var experiment = IntegratedHeatReader.ReadFile(
            path,
            concentrationsAreMilliMolar: true,
            dilutionMethod: DilutionMethod.DiscreteDisplacement,
            reprocessIntegratedHeatData: !reprocess);

        Assert.Equal(reprocess, experiment.AppliedDilutionMethod.HasValue);
        Assert.False(experiment.Injections[0].Include);
        Assert.Equal(EnergyUnit.MicroCal, prompt.LastUnit);
        Assert.True(prompt.LastShowReprocessChoice);
        IntegratedHeatReader.BeginImportQueue();
        prompt.ReprocessChoice = !reprocess;
        var counterpart = IntegratedHeatReader.ReadFile(
            path,
            concentrationsAreMilliMolar: true,
            dilutionMethod: DilutionMethod.DiscreteDisplacement,
            reprocessIntegratedHeatData: reprocess);
        Assert.Equal(
            experiment.Injections.Select(injection => injection.RawPeakArea.Value),
            counterpart.Injections.Select(injection => injection.RawPeakArea.Value));
        if (reprocess)
        {
            var retention = 1 - experiment.Injections[0].Volume / experiment.CellVolume;
            var expectedCell = experiment.CellConcentration.Value * retention;
            var expectedTitrant = experiment.SyringeConcentration.Value * (1 - retention);
            Assert.Equal(expectedCell, experiment.Injections[0].ActualCellConcentration, 10);
            Assert.Equal(expectedTitrant / expectedCell, experiment.Injections[0].Ratio, 10);
        }
        else
        {
            Assert.Equal(0.0562e-3, experiment.Injections[0].ActualCellConcentration, 8);
        }
    }

    [Fact]
    public void DhQueueReuseDoesNotChooseReprocessingForFollowingDelimitedFiles()
    {
        prompt.Responses.Enqueue(new(EnergyUnit.Joule, true, false, null));
        prompt.Responses.Enqueue(new(EnergyUnit.Cal, true, false, true));
        prompt.ReprocessChoice = false;

        var dhPath = Fixture("PublishedBenchmarks", "nature2022-can-wt-sequential", "can-wt-preq1-1-reference-predicted.dh");
        var tablePath = Fixture("FileTypeTests", "230908_PRLRlong_W392A_run1.dat");
        var nextTablePath = Fixture("FileTypeTests", "CURVE-1.aff");

        var dh = IntegratedHeatReader.ReadFile(dhPath);
        var firstTable = IntegratedHeatReader.ReadFile(tablePath);
        var nextTable = IntegratedHeatReader.ReadFile(nextTablePath);

        Assert.NotNull(dh);
        Assert.NotEmpty(dh.Injections);
        var retention = 1 - dh.Injections[0].Volume / dh.CellVolume;
        Assert.Equal(dh.CellConcentration.Value * retention, dh.Injections[0].ActualCellConcentration, 10);
        Assert.Equal(dh.SyringeConcentration.Value * (1 - retention) / (dh.CellConcentration.Value * retention), dh.Injections[0].Ratio, 10);
        Assert.False(prompt.Calls[0].ShowReprocessChoice);
        Assert.True(prompt.Calls[1].ShowReprocessChoice);
        Assert.Equal(EnergyUnit.Joule, prompt.Calls[1].ReusedUnit);
        Assert.Equal(2, prompt.Calls.Count);
        Assert.True(firstTable.AppliedDilutionMethod.HasValue);
        Assert.True(nextTable.AppliedDilutionMethod.HasValue);
        Assert.False(firstTable.Injections[0].Include);
        Assert.False(nextTable.Injections[0].Include);
    }

    [Fact]
    public void DelimitedChoiceIsAskedAgainWhenQueueReuseIsNotSelected()
    {
        prompt.Responses.Enqueue(new(EnergyUnit.Joule, false, false, false));
        prompt.Responses.Enqueue(new(EnergyUnit.Joule, false, false, true));

        var dat = IntegratedHeatReader.ReadFile(Fixture("FileTypeTests", "230908_PRLRlong_W392A_run1.dat"));
        var aff = IntegratedHeatReader.ReadFile(Fixture("FileTypeTests", "CURVE-1.aff"));

        Assert.Equal(2, prompt.Calls.Count);
        Assert.Null(dat.AppliedDilutionMethod);
        Assert.True(aff.AppliedDilutionMethod.HasValue);
    }

    private static string Fixture(params string[] parts)
    {
        var path = Path.Combine(new[] { AppContext.BaseDirectory, "Fixtures" }.Concat(parts).ToArray());
        Assert.True(File.Exists(path), $"Fixture not found: {path}");
        return path;
    }

    private sealed class PromptService : IIntegratedHeatImportPromptService
    {
        public bool ReprocessChoice { get; set; }
        public EnergyUnit LastUnit { get; private set; }
        public bool LastShowReprocessChoice { get; private set; }
        public System.Collections.Generic.Queue<EnergyUnitPromptResult> Responses { get; } = new();
        public System.Collections.Generic.List<(bool ShowReprocessChoice, bool DefaultReprocess, EnergyUnit? ReusedUnit)> Calls { get; } = new();

        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse)
            => AskForEnergyUnit(fileName, encounteredValue, allowQueueReuse, false, false, null);

        public EnergyUnitPromptResult AskForEnergyUnit(
            string fileName,
            string encounteredValue,
            bool allowQueueReuse,
            bool showReprocessChoice,
            bool defaultReprocess,
            EnergyUnit? reusedUnit)
        {
            Calls.Add((showReprocessChoice, defaultReprocess, reusedUnit));
            LastShowReprocessChoice = showReprocessChoice;
            var result = Responses.Count > 0
                ? Responses.Dequeue()
                : new EnergyUnitPromptResult(EnergyUnit.MicroCal, false, false, showReprocessChoice ? ReprocessChoice : null);
            if (result.Unit.HasValue) LastUnit = result.Unit.Value;
            return result;
        }
    }
}
