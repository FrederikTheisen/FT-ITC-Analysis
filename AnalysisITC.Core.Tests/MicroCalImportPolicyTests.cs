using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Processing;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Import validation")]
public sealed class MicroCalImportPolicyTests : IDisposable
{
    readonly PreferencesState originalSettings = PreferencesState.FromSettings();
    readonly IDataValidationPromptService originalPrompt = PlatformServices.DataValidationPromptService;
    readonly Prompt prompt = new();
    readonly string directory = Path.Combine(Path.GetTempPath(), "ftitc-import-policy-" + Guid.NewGuid().ToString("N"));

    public MicroCalImportPolicyTests()
    {
        Directory.CreateDirectory(directory);
        DataManager.Clear(DataClearMode.ResetSession);
        AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
        PlatformServices.RegisterDataValidationPromptService(prompt);
    }

    public void Dispose()
    {
        DataManager.Clear(DataClearMode.ResetSession);
        PlatformServices.RegisterDataValidationPromptService(originalPrompt);
        originalSettings.ApplyToSettings();
        Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task StoppedRunImportsOnlyRecordedShotsBeforeBookkeepingAndReachesObjective()
    {
        var path = Write("stopped.itc", MicroCalText(plannedCount: 20, recordedIds: new[] { 0, 1, 2 }));

        var read = await DataReader.ReadPathsAsync(new[] { path });

        var experiment = Assert.Single(read.ImportedExperiments);
        Assert.Equal(new[] { 0, 1, 2 }, experiment.Injections.Select(injection => injection.ID));
        Assert.Equal(new[] { 40f, 120f, 200f }, experiment.Injections.Select(injection => injection.Time));
        Assert.Equal(new[] { false, true, true }, experiment.Injections.Select(injection => injection.Include));
        Assert.Empty(prompt.Calls);
        // Independent MicroCal manual expectations for 2 µL shots into 200 µL,
        // with initial cell concentration 40 µM and syringe concentration 500 µM.
        var expectedCell = new[] { 39.60199004975125e-6, 39.20792079207921e-6, 38.81773399014779e-6 };
        var expectedTitrant = new[] { 4.975e-6, 9.9e-6, 14.775e-6 };
        for (var i = 0; i < 3; i++)
        {
            Assert.InRange(Math.Abs(expectedCell[i] - experiment.Injections[i].ActualCellConcentration), 0, 1e-11);
            Assert.InRange(Math.Abs(expectedTitrant[i] - experiment.Injections[i].ActualTitrantConcentration), 0, 1e-11);
            experiment.Injections[i].SetIntegrationStartTime(0);
            experiment.Injections[i].SetIntegrationLengthByTime(22);
        }

        experiment.Processor.DiscardIntegratedPoints = true;
        experiment.Processor.InitializeBaseline(BaselineInterpolatorTypes.Polynomial);
        var baseline = Assert.IsType<PolynomialLeastSquaresInterpolator>(experiment.Processor.Interpolator);
        baseline.Degree = 1;
        baseline.ZLimit = 3;
        await experiment.Processor.ProcessData(showProgress: false);
        Assert.True(experiment.Processor.IntegrationCompleted);
        Assert.All(experiment.Injections, injection => Assert.True(injection.IsIntegrated));

        var model = CreateModel(experiment);
        Assert.All(experiment.Injections, injection => Assert.True(double.IsFinite(model.Evaluate(injection.ID))));
        Assert.All(model.LossFunctionResiduals(model.Parameters.GetFittedParameterArray(), false),
            residual => Assert.True(double.IsFinite(residual)));
    }

    [Fact]
    public void OrdinaryInterruptedFixtureOmitsOnlyTheLastUnrecordedShot()
    {
        var experiment = MicroCalITC200Reader.ReadPath(Fixture("dissociation_data_test.itc"));

        Assert.Equal(19, experiment.InjectionCount);
        Assert.Equal(Enumerable.Range(0, 19), experiment.Injections.Select(injection => injection.ID));
        Assert.All(experiment.Injections, injection => Assert.InRange(injection.Time,
            experiment.DataPoints.Min(point => point.Time), experiment.DataPoints.Max(point => point.Time)));
    }

    [Theory]
    [InlineData(false, "#2")]
    [InlineData(true, "#1")]
    public void UnmarkedEntryBeforeLaterRecordedShotIsRejected(bool missingFirst, string missingNumber)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            MicroCalText(20, missingFirst ? new[] { 1, 2 } : new[] { 0, 2 })));

        var error = Assert.Throws<FormatException>(() => MicroCalITC200Reader.ReadStream(stream, "gap.itc"));

        Assert.Contains("Injection " + missingNumber + " has no stream marker", error.Message);
        Assert.Contains("later injection was recorded", error.Message);
    }

    [Fact]
    public async Task MissingInternalMarkerSkipsOnlyThatFileAndDoesNotRenumberLaterShots()
    {
        var missing = Write("missing-marker.itc", MicroCalText(20, new[] { 0, 2 }));
        var complete = Write("complete.itc", MicroCalText(3, new[] { 0, 1, 2 }));

        var read = await DataReader.ReadPathsAsync(new[] { missing, complete });

        Assert.Equal(new[] { complete }, read.LoadedPaths);
        var accepted = Assert.Single(read.ImportedExperiments);
        Assert.Same(accepted, Assert.Single(DataManager.Data));
        Assert.Equal(new[] { 0, 1, 2 }, accepted.Injections.Select(injection => injection.ID));
        Assert.Empty(prompt.Calls);
    }

    [Fact]
    public async Task TruncatedLeadingTraceIsSkippedAndBatchContinuesWithoutChangingExistingData()
    {
        var original = File.ReadAllText(Fixture("ScientificValidation", "RawPipeline", "analytic-one-site.itc"));
        var truncated = Write("truncated.itc", RemoveSamplesBefore(original, 65));
        var valid = Write("complete.itc", original);
        var existing = MicroCalITC200Reader.ReadPath(valid);
        DataManager.AddData(new[] { existing });
        var existingInjections = existing.Injections.ToArray();
        var existingConcentrations = existing.Injections.Select(injection => injection.ActualTitrantConcentration).ToArray();

        // Keep returned by the test service must not override the mandatory rejection.
        var read = await DataReader.ReadPathsAsync(new[] { truncated, valid });

        Assert.Equal(new[] { valid }, read.LoadedPaths);
        var accepted = Assert.Single(read.ImportedExperiments);
        Assert.Equal(32, accepted.InjectionCount);
        Assert.Equal(Enumerable.Range(0, 32), accepted.Injections.Select(injection => injection.ID));
        Assert.Equal(existingInjections, existing.Injections);
        Assert.Equal(existingConcentrations, existing.Injections.Select(injection => injection.ActualTitrantConcentration));
        Assert.InRange(Math.Abs(accepted.Injections[1].ActualTitrantConcentration - 9.9e-6), 0, 1e-11);
        var rejection = prompt.Calls.First();
        Assert.False(rejection.AllowKeep);
        Assert.False(rejection.CanFix);
        Assert.Contains("outside the recorded thermogram range", rejection.Message);
        var model = CreateModel(accepted);
        Assert.True(double.IsFinite(model.Evaluate(31)));
        Assert.All(model.LossFunctionResiduals(model.Parameters.GetFittedParameterArray(), false),
            residual => Assert.True(double.IsFinite(residual)));
    }

    [Theory]
    [InlineData(DataValidationPromptAction.Keep, true)]
    [InlineData(DataValidationPromptAction.Discard, false)]
    public async Task NearEndChoiceIsHonoredThroughProductionImport(DataValidationPromptAction action, bool keep)
    {
        prompt.Action = action;
        var path = Write("near-end.itc", MicroCalText(20, new[] { 0 }, firstTime: 150, endTime: 155));

        var read = await DataReader.ReadPathsAsync(new[] { path });

        Assert.Equal(keep ? 1 : 0, read.ImportedExperiments.Count);
        if (keep) Assert.Equal(150, Assert.Single(Assert.Single(read.ImportedExperiments).Injections).Time);
        var call = Assert.Single(prompt.Calls);
        Assert.True(call.AllowKeep);
        Assert.False(call.CanFix);
    }

    [Fact]
    public async Task SavedRawSourceProjectKeepsOutOfRangeInjectionsAndSavedConcentrations()
    {
        using var raw = new MemoryStream(Encoding.UTF8.GetBytes(MicroCalText(3, new[] { 0, 1, 2 })));
        var saved = MicroCalITC200Reader.ReadStream(raw, "saved.itc");
        saved.DataPoints.RemoveAll(point => point.Time < 65);
        saved.Injections[1].ActualTitrantConcentration = 123e-6;
        var path = Path.Combine(directory, "saved.ftxtc");
        using (var package = File.Create(path))
            await FTXTCWriter.WriteStream(package, new[] { saved });

        var read = await DataReader.ReadPathsAsync(new[] { path });

        Assert.True(read.LoadedAny);
        var restored = Assert.Single(DataManager.Data);
        Assert.Equal(ITCDataFormat.ITC200, restored.DataSourceFormat);
        Assert.Equal(saved.Injections.Select(injection => injection.ID), restored.Injections.Select(injection => injection.ID));
        Assert.Equal(saved.Injections.Select(injection => injection.Time), restored.Injections.Select(injection => injection.Time));
        Assert.Equal(saved.Injections.Select(injection => injection.ActualTitrantConcentration),
            restored.Injections.Select(injection => injection.ActualTitrantConcentration));
        Assert.True(Assert.Single(prompt.Calls).AllowKeep);
        Assert.False(Assert.Single(prompt.Calls).CanFix);
    }

    [Fact]
    public void ConcatenatedFixtureKeepsItsRecordedShotsSegmentsAndPredictions()
    {
        using var raw = File.OpenRead(Fixture("Lenette112+113+114.itc"));
        var experiment = MicroCalITC200Reader.ReadStream(raw, "tandem.itc");

        Assert.Equal(39, experiment.InjectionCount);
        Assert.Equal(Enumerable.Range(0, 39), experiment.Injections.Select(injection => injection.ID));
        Assert.Equal(new[] { 0, 13, 26 }, experiment.Segments.Select(segment => segment.FirstInjectionID));
        Assert.Equal(3, experiment.Injections.Count(injection => !injection.Include));
        var model = CreateModel(experiment);
        Assert.All(experiment.Injections, injection => Assert.True(double.IsFinite(model.Evaluate(injection.ID))));
    }

    static OneSetOfSites CreateModel(ExperimentData experiment)
    {
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        experiment.Model = model;
        return model;
    }

    string Write(string fileName, string text)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, text);
        return path;
    }

    static string Fixture(params string[] parts) => Path.Combine(new[] { AppContext.BaseDirectory, "Fixtures" }.Concat(parts).ToArray());

    static string MicroCalText(int plannedCount, int[] recordedIds, int firstTime = 40, int endTime = 300)
    {
        var text = new StringBuilder("$ITC\n$ " + plannedCount + "\n$NOT\n$25\n$40\n$750\n$10\n$1\n$ADCGainCode: 0\n$False,True,True\n");
        for (var i = 0; i < plannedCount; i++) text.AppendLine("$2,2,80,1");
        text.AppendLine("#0\n#0.5\n#0.04\n#0.2\n?Interrupted run\n@0");
        for (var time = 0; time <= endTime; time++)
        {
            foreach (var id in recordedIds.Where(id => firstTime + 80 * id == time))
                text.AppendLine($"@{id + 1},2,2,{time}");
            var power = 1.0 - recordedIds.Where(id => firstTime + 80 * id <= time)
                .Sum(id => 10 * Math.Exp(-(time - firstTime - 80 * id) / 6.0));
            text.AppendLine(time.ToString(CultureInfo.InvariantCulture) + "," + power.ToString("R", CultureInfo.InvariantCulture) + ",25");
        }
        return text.ToString();
    }

    static string RemoveSamplesBefore(string text, double minimumTime)
    {
        var inStream = false;
        return string.Join("\n", text.Split('\n').Where(line =>
        {
            var trimmed = line.Trim();
            if (trimmed == "@0") { inStream = true; return true; }
            if (!inStream || trimmed.Length == 0 || trimmed.StartsWith("@")) return true;
            return double.Parse(trimmed.Split(',')[0], CultureInfo.InvariantCulture) >= minimumTime;
        }));
    }

    sealed class Prompt : IDataValidationPromptService
    {
        public DataValidationPromptAction Action { get; set; } = DataValidationPromptAction.Keep;
        public List<(string Message, bool CanFix, bool AllowKeep)> Calls { get; } = new();

        public DataValidationPromptResult AskValidationIssue(string title, string message, bool canFix, bool requiresInput, bool allowKeep = true)
        {
            Calls.Add((message, canFix, allowKeep));
            return new DataValidationPromptResult(Action);
        }
    }
}
