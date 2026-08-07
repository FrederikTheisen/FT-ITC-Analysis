using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [CollectionDefinition("Import validation", DisableParallelization = true)]
    public sealed class ImportValidationCollectionDefinition
    {
    }

    [Collection("Import validation")]
    public sealed class ImportValidatorTests : IDisposable
    {
        readonly RecordingValidationPromptService promptService = new RecordingValidationPromptService();

        public ImportValidatorTests()
        {
            PlatformServices.RegisterDataValidationPromptService(promptService);
            AppSettings.AutomaticallyDiscardOrphanInjectionsOnLoad = true;
        }

        public void Dispose()
        {
            PlatformServices.RegisterDataValidationPromptService(null);
            PlatformServices.RegisterSettingsStore(null);
            AppSettings.AutomaticallyDiscardOrphanInjectionsOnLoad = true;
        }

        [Fact]
        public void AutomaticCleanupRemovesOnlyOutOfBoundsMarkersAndReprocessesConcentrations()
        {
            var experiment = Experiment(
                Injection(-1, -1, 50e-6),
                Injection(0, 0, 1e-6),
                Injection(1, 20, 1e-6),
                Injection(2, 100, 1e-6),
                Injection(3, 101, 50e-6));
            var reports = new List<AutomaticImportActionReport>();

            var valid = ImportValidator.ValidateData(experiment, allowAutomaticActions: true, reports);

            Assert.True(valid);
            Assert.Equal(new[] { 0, 1, 2 }, experiment.Injections.Select(injection => injection.ID).ToArray());
            Assert.Equal(new[] { 0f, 20f, 100f }, experiment.Injections.Select(injection => injection.Time).ToArray());
            Assert.Equal(0.00001998, experiment.Injections[1].ActualTitrantConcentration, 10);
            var report = Assert.Single(reports);
            Assert.Equal(2, report.DiscardedOrphanInjectionCount);
            Assert.Empty(promptService.Messages);
        }

        [Fact]
        public void DisabledPreferenceLeavesOrphansForTheExistingPrompt()
        {
            AppSettings.AutomaticallyDiscardOrphanInjectionsOnLoad = false;
            promptService.Action = DataValidationPromptAction.Keep;
            var experiment = Experiment(Injection(0, -1), Injection(1, 20));
            var reports = new List<AutomaticImportActionReport>();

            var valid = ImportValidator.ValidateData(experiment, allowAutomaticActions: true, reports);

            Assert.True(valid);
            Assert.Equal(2, experiment.Injections.Count);
            Assert.Empty(reports);
            Assert.Single(promptService.Messages);
            Assert.Contains("outside the recorded thermogram range", promptService.Messages[0]);
        }

        [Fact]
        public void DisabledPreferenceCanRemoveOrphansThroughAttemptFix()
        {
            AppSettings.AutomaticallyDiscardOrphanInjectionsOnLoad = false;
            promptService.Action = DataValidationPromptAction.AttemptFix;
            var experiment = Experiment(Injection(0, -1), Injection(1, 20));
            var reports = new List<AutomaticImportActionReport>();

            var valid = ImportValidator.ValidateData(experiment, allowAutomaticActions: true, reports);

            Assert.True(valid);
            Assert.Equal(1, Assert.Single(experiment.Injections).ID);
            Assert.Empty(reports);
            Assert.Single(promptService.Messages);
        }

        [Fact]
        public void RemovingEveryOrphanContinuesToTheNoInjectionsPrompt()
        {
            promptService.Action = DataValidationPromptAction.Discard;
            var experiment = Experiment(Injection(0, -2), Injection(1, 102));
            var reports = new List<AutomaticImportActionReport>();

            var valid = ImportValidator.ValidateData(experiment, allowAutomaticActions: true, reports);

            Assert.False(valid);
            Assert.Empty(experiment.Injections);
            Assert.Equal(2, Assert.Single(reports).DiscardedOrphanInjectionCount);
            Assert.Single(promptService.Messages);
            Assert.Contains("No injections were found", promptService.Messages[0]);
        }

        [Fact]
        public void AutomaticCleanupIsSuppressedForSavedIntegratedAndTandemData()
        {
            promptService.Action = DataValidationPromptAction.Keep;
            var reports = new List<AutomaticImportActionReport>();

            var savedProjectExperiment = Experiment(Injection(0, -1), Injection(1, 20));
            Assert.True(ImportValidator.ValidateData(savedProjectExperiment, allowAutomaticActions: false, reports));

            var integratedExperiment = Experiment(Injection(0, -1), Injection(1, 20));
            integratedExperiment.DataSourceFormat = ITCDataFormat.IntegratedHeats;
            Assert.True(ImportValidator.ValidateData(integratedExperiment, allowAutomaticActions: true, reports));

            var tandemExperiment = Experiment(Injection(0, -1), Injection(1, 20));
            tandemExperiment.ReplaceSegments(new[] { new TandemExperimentSegment(0, 0.001, 0) });
            Assert.True(ImportValidator.ValidateData(tandemExperiment, allowAutomaticActions: true, reports));

            Assert.Empty(reports);
            Assert.Equal(2, savedProjectExperiment.Injections.Count);
            Assert.Equal(2, integratedExperiment.Injections.Count);
            Assert.Equal(2, tandemExperiment.Injections.Count);
            Assert.Equal(3, promptService.Messages.Count);
        }

        [Fact]
        public void AutomaticActionStatusSummarizesSingleAndMultipleExperiments()
        {
            Assert.Equal(
                "Automatically discarded 1 orphan injection while loading First.",
                DataReader.BuildAutomaticImportActionStatus(new[] { new AutomaticImportActionReport("First", 1) }));
            Assert.Equal(
                "Automatically discarded 4 orphan injections across 2 experiments.",
                DataReader.BuildAutomaticImportActionStatus(new[]
                {
                    new AutomaticImportActionReport("First", 1),
                    new AutomaticImportActionReport("Second", 3),
                }));
            Assert.Equal("", DataReader.BuildAutomaticImportActionStatus(Array.Empty<AutomaticImportActionReport>()));
        }

        [Fact]
        public void SettingPersistsAndResetRestoresEnabledDefault()
        {
            PlatformServices.RegisterSettingsStore(new InMemorySettingsStore());
            AppSettings.Reset();
            Assert.True(AppSettings.AutomaticallyDiscardOrphanInjectionsOnLoad);

            AppSettings.AutomaticallyDiscardOrphanInjectionsOnLoad = false;
            AppSettings.Save();
            AppSettings.AutomaticallyDiscardOrphanInjectionsOnLoad = true;
            AppSettings.Load();

            Assert.False(AppSettings.AutomaticallyDiscardOrphanInjectionsOnLoad);
            AppSettings.Reset();
            Assert.True(AppSettings.AutomaticallyDiscardOrphanInjectionsOnLoad);
        }

        static ExperimentData Experiment(params InjectionSpec[] injectionSpecs)
        {
            var experiment = new ExperimentData("validator-test.itc")
            {
                DataSourceFormat = ITCDataFormat.ITC200,
                TargetTemperature = 25,
                MeasuredTemperature = 25,
                CellVolume = 0.001,
                CellConcentration = new FloatWithError(0.001),
                SyringeConcentration = new FloatWithError(0.01),
            };

            for (var time = 0; time <= 100; time += 10)
                experiment.DataPoints.Add(new DataPoint(time, 0, 25));

            foreach (var spec in injectionSpecs)
            {
                experiment.Injections.Add(InjectionData.FromPEAQFile(
                    experiment,
                    spec.ID,
                    include: true,
                    time: spec.Time,
                    volume: spec.Volume,
                    delay: 20,
                    duration: 1,
                    temperature: 25));
            }

            return experiment;
        }

        static InjectionSpec Injection(int id, double time, double volume = 1e-6) =>
            new InjectionSpec(id, time, volume);

        readonly struct InjectionSpec
        {
            public InjectionSpec(int id, double time, double volume)
            {
                ID = id;
                Time = time;
                Volume = volume;
            }

            public int ID { get; }
            public double Time { get; }
            public double Volume { get; }
        }

        sealed class RecordingValidationPromptService : IDataValidationPromptService
        {
            public List<string> Messages { get; } = new List<string>();
            public DataValidationPromptAction Action { get; set; } = DataValidationPromptAction.Keep;

            public DataValidationPromptResult AskValidationIssue(
                string title,
                string message,
                bool canFix,
                bool requiresInput)
            {
                Messages.Add(message ?? "");
                return new DataValidationPromptResult(Action);
            }
        }
    }
}
