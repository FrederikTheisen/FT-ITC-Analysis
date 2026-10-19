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
        }

        public void Dispose()
        {
            PlatformServices.RegisterDataValidationPromptService(null);
            PlatformServices.RegisterSettingsStore(null);
        }

        [Theory]
        [InlineData(-1, DataValidationPromptAction.Keep)]
        [InlineData(101, DataValidationPromptAction.Keep)]
        [InlineData(-1, DataValidationPromptAction.AttemptFix)]
        [InlineData(101, DataValidationPromptAction.Discard)]
        public void RawImportRejectsRecordedStartsOutsideTraceWithoutMutatingData(
            double outsideTime, DataValidationPromptAction action)
        {
            promptService.Action = action;
            var experiment = Experiment(Injection(0, outsideTime), Injection(1, 20));
            RawDataReader.ProcessInjections(experiment, DilutionMethod.MicroCal);
            var injections = experiment.Injections.ToArray();
            var concentrations = injections.Select(injection => injection.ActualTitrantConcentration).ToArray();
            // Mandatory coverage rejection must precede even the short-trace warning.
            experiment.DataPoints = new List<DataPoint> { new DataPoint(0, 0, 25), new DataPoint(100, 0, 25) };

            Assert.False(ImportValidator.ValidateData(experiment, isRawImport: true));

            Assert.Equal(injections, experiment.Injections);
            Assert.Equal(concentrations, experiment.Injections.Select(injection => injection.ActualTitrantConcentration));
            Assert.Equal(new[] { 0, 1 }, experiment.Injections.Select(injection => injection.ID));
            Assert.Contains("outside the recorded thermogram range", Assert.Single(promptService.Messages));
            Assert.False(Assert.Single(promptService.AllowKeepValues));
            Assert.False(Assert.Single(promptService.CanFixValues));
        }

        [Fact]
        public void RawImportAcceptsExactEndpointsIncludingZero()
        {
            var experiment = Experiment(Injection(0, 0), Injection(1, 20), Injection(2, 100));

            Assert.True(ImportValidator.ValidateData(experiment, isRawImport: true));

            Assert.Equal(new[] { 0f, 20f, 100f }, experiment.Injections.Select(injection => injection.Time));
            Assert.Empty(promptService.Messages);
        }

        [Fact]
        public void SavedProjectKeepPreservesRecordedInjectionsAndScientificValues()
        {
            var experiment = Experiment(Injection(0, -1), Injection(1, 20), Injection(2, 101));
            RawDataReader.ProcessInjections(experiment, DilutionMethod.MicroCal);
            experiment.Injections[1].ActualTitrantConcentration = 123e-6;
            var injections = experiment.Injections.ToArray();
            var concentrations = injections.Select(injection => injection.ActualTitrantConcentration).ToArray();

            Assert.True(ImportValidator.ValidateData(experiment));

            Assert.Equal(injections, experiment.Injections);
            Assert.Equal(concentrations, experiment.Injections.Select(injection => injection.ActualTitrantConcentration));
            Assert.True(Assert.Single(promptService.AllowKeepValues));
            Assert.False(Assert.Single(promptService.CanFixValues));
        }

        [Theory]
        [InlineData(DataValidationPromptAction.Keep, true)]
        [InlineData(DataValidationPromptAction.Discard, false)]
        public void NearEndWarningOffersOnlyKeepOrDiscard(DataValidationPromptAction action, bool expected)
        {
            promptService.Action = action;
            var experiment = Experiment(Injection(0, 150));
            experiment.DataPoints = Enumerable.Range(0, 156).Select(time => new DataPoint(time, 0, 25)).ToList();
            var injection = Assert.Single(experiment.Injections);

            Assert.Equal(expected, ImportValidator.ValidateData(experiment, isRawImport: true));

            Assert.Same(injection, Assert.Single(experiment.Injections));
            Assert.Equal(150, injection.Time);
            Assert.Contains("very end", Assert.Single(promptService.Messages));
            Assert.True(Assert.Single(promptService.AllowKeepValues));
            Assert.False(Assert.Single(promptService.CanFixValues));
        }

        [Fact]
        public void RawTandemImportCannotBypassCoverageRejection()
        {
            var experiment = Experiment(Injection(0, -1), Injection(1, 20));
            experiment.ReplaceSegments(new[] { new TandemExperimentSegment(0, 0.001, 0) });

            Assert.False(ImportValidator.ValidateData(experiment, isRawImport: true));
            Assert.Equal(2, experiment.Injections.Count);
            Assert.False(Assert.Single(promptService.AllowKeepValues));
        }

        [Fact]
        public void IntegratedHeatsDoNotRequireThermogramCoverage()
        {
            var experiment = Experiment(Injection(0, 200), Injection(1, 300));
            experiment.DataSourceFormat = ITCDataFormat.IntegratedHeats;

            Assert.True(ImportValidator.ValidateData(experiment, isRawImport: true));
            Assert.Equal(2, experiment.Injections.Count);
            Assert.Empty(promptService.Messages);
        }

        [Fact]
        public void IntegratedHeatMissingMetadataCanBeRepairedSequentially()
        {
            var experiment = Experiment(Injection(0, 10, 10e-6), Injection(1, 20, 10e-6));
            experiment.DataSourceFormat = ITCDataFormat.IntegratedHeats;
            experiment.CellVolume = double.NaN;
            experiment.CellConcentration = new FloatWithError(double.NaN);
            experiment.SyringeConcentration = new FloatWithError(double.NaN);
            experiment.PendingImportBookkeepingMethod = DilutionMethod.MicroCal;
            promptService.Responses.Enqueue(new DataValidationPromptResult(DataValidationPromptAction.AttemptFix, "1.4 mL"));
            promptService.Responses.Enqueue(new DataValidationPromptResult(DataValidationPromptAction.AttemptFix, "100 uM"));
            promptService.Responses.Enqueue(new DataValidationPromptResult(DataValidationPromptAction.AttemptFix, "4 mM"));

            var valid = ImportValidator.ValidateData(experiment);

            Assert.True(valid);
            Assert.Equal(1.4e-3, experiment.CellVolume, 12);
            Assert.Equal(100e-6, experiment.CellConcentration.Value, 12);
            Assert.Equal(4e-3, experiment.SyringeConcentration.Value, 12);
            Assert.Equal(DilutionMethod.MicroCal, experiment.AppliedDilutionMethod);
            Assert.Equal(3, promptService.Messages.Count);
            Assert.Contains("cell volume", promptService.Messages[0], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("cell concentration", promptService.Messages[1], StringComparison.OrdinalIgnoreCase);
            Assert.Contains("syringe concentration", promptService.Messages[2], StringComparison.OrdinalIgnoreCase);
            Assert.All(experiment.Injections, injection => Assert.True(double.IsFinite(injection.Ratio)));
        }

        [Theory]
        [InlineData("1400", 1.4e-3)]
        [InlineData("1400 uL", 1.4e-3)]
        [InlineData("1.4 mL", 1.4e-3)]
        [InlineData("0.0014 L", 1.4e-3)]
        public void IntegratedHeatCellVolumeRepairAcceptsDocumentedUnits(string input, double expectedLiters)
        {
            var experiment = Experiment(Injection(0, 10));
            experiment.DataSourceFormat = ITCDataFormat.IntegratedHeats;
            experiment.CellVolume = double.NaN;
            promptService.Responses.Enqueue(new DataValidationPromptResult(DataValidationPromptAction.AttemptFix, input));

            var valid = ImportValidator.ValidateData(experiment);

            Assert.True(valid);
            Assert.Equal(expectedLiters, experiment.CellVolume, 12);
            Assert.Single(promptService.Messages);
        }

        [Fact]
        public void IntegratedHeatMissingMetadataCanBeKeptForLaterRepair()
        {
            var experiment = Experiment(Injection(0, 10));
            experiment.DataSourceFormat = ITCDataFormat.IntegratedHeats;
            experiment.CellVolume = double.NaN;
            promptService.Action = DataValidationPromptAction.Keep;

            var valid = ImportValidator.ValidateData(experiment);

            Assert.True(valid);
            Assert.False(double.IsFinite(experiment.CellVolume));
            Assert.Single(promptService.Messages);
            Assert.Contains("can still be used", promptService.Messages[0]);
            Assert.True(Assert.Single(promptService.AllowKeepValues));
        }

        [Fact]
        public void ExistingSyringeBelowCellGuardStillRepairsIntegratedData()
        {
            var experiment = Experiment(Injection(0, 10));
            experiment.DataSourceFormat = ITCDataFormat.IntegratedHeats;
            experiment.CellConcentration = new FloatWithError(10e-3);
            experiment.SyringeConcentration = new FloatWithError(1e-3);
            promptService.Responses.Enqueue(new DataValidationPromptResult(DataValidationPromptAction.AttemptFix, "20 mM"));

            var valid = ImportValidator.ValidateData(experiment);

            Assert.True(valid);
            Assert.Equal(20e-3, experiment.SyringeConcentration.Value, 12);
            Assert.Single(promptService.Messages);
            Assert.Contains("appears to be lower", promptService.Messages[0]);
        }

        [Fact]
        public void ZeroCellConcentrationIsAcceptedForRawImports()
        {
            var experiment = Experiment(Injection(0, 10), Injection(1, 20));
            experiment.CellConcentration = new FloatWithError(0);

            var valid = ImportValidator.ValidateData(experiment);

            Assert.True(valid);
            Assert.Empty(promptService.Messages);
        }

        [Theory]
        [InlineData("cell", -1e-5)]
        [InlineData("cell", double.NaN)]
        [InlineData("syringe", -1e-5)]
        [InlineData("syringe", double.NaN)]
        [InlineData("volume", 0)]
        public void InvalidConcentrationMetadataIsReportedForRawImports(string field, double value)
        {
            var experiment = Experiment(Injection(0, 10), Injection(1, 20));
            if (field == "cell") experiment.CellConcentration = new FloatWithError(value);
            else if (field == "syringe") experiment.SyringeConcentration = new FloatWithError(value);
            else experiment.CellVolume = value;

            var valid = ImportValidator.ValidateData(experiment);

            Assert.True(valid);
            var message = Assert.Single(promptService.Messages);
            Assert.Contains(field == "volume" ? "cell volume" : field + " concentration", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void InjectionAfterThermogramGapIsReported()
        {
            var experiment = Experiment(Injection(0, 5), Injection(1, 55));
            experiment.DataPoints = Enumerable.Range(0, 101)
                .Where(time => time <= 10 || time >= 60)
                .Select(time => new DataPoint(time, 0, 25))
                .ToList();

            var valid = ImportValidator.ValidateData(experiment);

            Assert.True(valid);
            var message = Assert.Single(promptService.Messages);
            Assert.Contains("Injection #2 at 55 s", message);
            Assert.Contains("previous sample at 10 s", message);
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
            public List<bool> CanFixValues { get; } = new List<bool>();
            public List<bool> AllowKeepValues { get; } = new List<bool>();
            public Queue<DataValidationPromptResult> Responses { get; } = new Queue<DataValidationPromptResult>();
            public DataValidationPromptAction Action { get; set; } = DataValidationPromptAction.Keep;

            public DataValidationPromptResult AskValidationIssue(
                string title,
                string message,
                bool canFix,
                bool requiresInput,
                bool allowKeep = true)
            {
                Messages.Add(message ?? "");
                AllowKeepValues.Add(allowKeep);
                CanFixValues.Add(canFix);
                if (Responses.Count > 0) return Responses.Dequeue();
                return new DataValidationPromptResult(Action);
            }
        }
    }
}
