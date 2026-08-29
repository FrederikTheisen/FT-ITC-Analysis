using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [Collection(FileTypeFixtureCollectionDefinition.Name)]
    public sealed class MicroCalExporterTests
    {
        const double ReferenceNdhCalPerMole = -27545.21246;
        const double FirstFitCalPerMole = -29237.19319;
        const double SecondFitCalPerMole = -28907.44499;

        [Fact]
        public void LinesUseCanonicalUnitsAndCurrentRowResidual()
        {
            var data = CreateReferenceExperiment(withSolution: true);
            var lines = Exporter.BuildMicroCalLines(data);

            Assert.Equal("DH,INJV,Xt,Mt,XMt,NDH,DY,Fit", lines[0]);
            Assert.Equal(4, lines.Count);

            var first = Split(lines[1]);
            AssertClose(-0.23725, Parse(first[0]));
            AssertClose(2.0, Parse(first[1]));
            AssertClose(0.0, Parse(first[2]));
            AssertClose(0.0045, Parse(first[3]));
            AssertClose(data.Injections[0].Ratio, Parse(first[4]));
            Assert.Equal("--", first[5]);
            Assert.Equal("--", first[6]);
            AssertClose(FirstFitCalPerMole, Parse(first[7]));

            var second = Split(lines[2]);
            AssertClose(-11.018084984, Parse(second[0]));
            AssertClose(8.0, Parse(second[1]));
            AssertClose(1000.0 * data.Injections[0].ActualTitrantConcentration, Parse(second[2]));
            AssertClose(1000.0 * data.Injections[0].ActualCellConcentration, Parse(second[3]));
            AssertClose(data.Injections[1].Ratio, Parse(second[4]));
            AssertClose(ReferenceNdhCalPerMole, Parse(second[5]));
            AssertClose(ReferenceNdhCalPerMole - SecondFitCalPerMole, Parse(second[6]));
            AssertClose(SecondFitCalPerMole, Parse(second[7]));

            var terminal = Split(lines[3]);
            Assert.Equal(string.Empty, terminal[0]);
            Assert.Equal("--", terminal[1]);
            AssertClose(1000.0 * data.Injections[1].ActualTitrantConcentration, Parse(terminal[2]));
            AssertClose(1000.0 * data.Injections[1].ActualCellConcentration, Parse(terminal[3]));
            Assert.Equal("--", terminal[4]);
            Assert.All(terminal.Skip(5), Assert.Empty);
        }

        [Fact]
        public void UnsolvedExperimentKeepsMeasuredValuesAndOmitsFitColumns()
        {
            var data = CreateReferenceExperiment(withSolution: false);
            var second = Split(Exporter.BuildMicroCalLines(data)[2]);

            AssertClose(-11.018084984, Parse(second[0]));
            AssertClose(ReferenceNdhCalPerMole, Parse(second[5]));
            Assert.Equal("--", second[6]);
            Assert.Equal("--", second[7]);
        }

        [Fact]
        public void InvalidHeatAndMassNeverEmitNonFiniteTokens()
        {
            var data = CreateReferenceExperiment(withSolution: true);
            data.SyringeConcentration = new FloatWithError(0);
            data.Injections[1].SetPeakArea(new FloatWithError(double.NaN));
            data.Injections[1].Ratio = double.PositiveInfinity;
            data.Injections[1].ActualCellConcentration = double.NaN;
            data.Injections[1].ActualTitrantConcentration = double.NegativeInfinity;

            var lines = Exporter.BuildMicroCalLines(data);
            Assert.DoesNotContain("NaN", string.Join("\n", lines), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Infinity", string.Join("\n", lines), StringComparison.OrdinalIgnoreCase);

            var second = Split(lines[2]);
            Assert.Equal("--", second[0]);
            Assert.Equal("--", second[4]);
            Assert.Equal("--", second[5]);
            Assert.Equal("--", second[6]);
            Assert.Equal("--", second[7]);
        }

        [Fact]
        public void OutputIsInvariantAcrossDecimalCommaCultures()
        {
            var data = CreateReferenceExperiment(withSolution: true);
            var previousCulture = CultureInfo.CurrentCulture;
            var previousUiCulture = CultureInfo.CurrentUICulture;

            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
                var expected = Exporter.BuildMicroCalLines(data);

                foreach (var cultureName in new[] { "en-US", "da-DK", "de-DE" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
                    Assert.Equal(expected, Exporter.BuildMicroCalLines(data));
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
                CultureInfo.CurrentUICulture = previousUiCulture;
            }
        }

        [Fact]
        public void ScientificNotationRetainsSmallAndLargeHeatValues()
        {
            var data = CreateReferenceExperiment(withSolution: false);
            const double smallMicrocalories = -1.234567890123456e-20;
            const double largeMicrocalories = 1.234567890123456e20;
            data.Injections[0].SetPeakArea(new FloatWithError(
                Energy.ConvertToJoule(smallMicrocalories, EnergyUnit.MicroCal)));
            data.Injections[1].SetPeakArea(new FloatWithError(
                Energy.ConvertToJoule(largeMicrocalories, EnergyUnit.MicroCal)));

            var lines = Exporter.BuildMicroCalLines(data);
            var firstDh = Split(lines[1])[0];
            var secondDh = Split(lines[2])[0];

            Assert.Contains("E", firstDh, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("E", secondDh, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                Energy.ConvertFromJoule(data.Injections[0].PeakArea.Value, EnergyUnit.MicroCal),
                Parse(firstDh));
            Assert.Equal(
                Energy.ConvertFromJoule(data.Injections[1].PeakArea.Value, EnergyUnit.MicroCal),
                Parse(secondDh));
        }

        [Fact]
        public void GeneratedDhAndNdhPathsRoundTripAbsoluteHeat()
        {
            var data = CreateReferenceExperiment(withSolution: false);
            var lines = Exporter.BuildMicroCalLines(data);
            var fullPath = TemporaryPath("microcal-export-dh");
            var ndhPath = TemporaryPath("microcal-export-ndh");
            var validation = new FixedValidationPromptService("50 uM");

            File.WriteAllLines(fullPath, lines);
            var second = Split(lines[2]);
            File.WriteAllLines(ndhPath, new[]
            {
                "INJV,NDH",
                $"{Split(lines[1])[1]},--",
                $"{second[1]},{second[5]}",
            });

            IntegratedHeatReader.BeginImportQueue();
            PlatformServices.RegisterImportPromptService(new MicroCalImportPromptService());
            PlatformServices.RegisterDataValidationPromptService(validation);
            try
            {
                var fromDh = IntegratedHeatReader.ReadFile(
                    fullPath,
                    concentrationsAreMilliMolar: true,
                    dilutionMethod: DilutionMethod.MicroCal,
                    reprocessIntegratedHeatData: false);
                Assert.NotNull(fromDh);
                Assert.Equal(data.Injections.Count, fromDh.Injections.Count);
                for (var index = 0; index < data.Injections.Count; index++)
                    AssertClose(data.Injections[index].PeakArea.Value, fromDh.Injections[index].PeakArea.Value);

                var fromNdh = IntegratedHeatReader.ReadFile(
                    ndhPath,
                    concentrationsAreMilliMolar: true,
                    dilutionMethod: DilutionMethod.MicroCal,
                    reprocessIntegratedHeatData: false);
                Assert.NotNull(fromNdh);
                Assert.True(double.IsNaN(fromNdh.Injections[0].PeakArea.Value));
                AssertClose(data.Injections[1].PeakArea.Value, fromNdh.Injections[1].PeakArea.Value);
            }
            finally
            {
                IntegratedHeatReader.EndImportQueue();
                PlatformServices.RegisterImportPromptService(null);
                PlatformServices.RegisterDataValidationPromptService(null);
                File.Delete(fullPath);
                File.Delete(ndhPath);
            }
        }

        static ExperimentData CreateReferenceExperiment(bool withSolution)
        {
            var data = new ExperimentData("sedphat-export-reference.itc")
            {
                CellConcentration = new FloatWithError(4.5e-6),
                SyringeConcentration = new FloatWithError(50e-6),
                CellVolume = 1.4141e-3,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };

            var first = new InjectionData(data, 0, 2e-6, data.SyringeConcentration * 2e-6, include: false);
            first.SetPeakArea(new FloatWithError(Energy.ConvertToJoule(-0.23725, EnergyUnit.MicroCal)));
            data.Injections.Add(first);

            var second = new InjectionData(data, 1, 8e-6, data.SyringeConcentration * 8e-6, include: true);
            var secondHeatCal = ReferenceNdhCalPerMole * second.InjectionMass;
            second.SetPeakArea(new FloatWithError(Energy.ConvertToJoule(secondHeatCal * 1_000_000.0, EnergyUnit.MicroCal)));
            data.Injections.Add(second);
            RawDataReader.ProcessInjectionsMicroCal(data);

            if (withSolution)
            {
                var predictions = new Dictionary<int, double>
                {
                    [0] = Energy.ConvertToJoule(FirstFitCalPerMole, EnergyUnit.Cal) * first.InjectionMass,
                    [1] = Energy.ConvertToJoule(SecondFitCalPerMole, EnergyUnit.Cal) * second.InjectionMass,
                };
                var model = new ExportProbeModel(data, predictions);
                data.Model = model;
                model.Solution = SolutionInterface.FromModel(
                    model,
                    SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            }

            return data;
        }

        static string[] Split(string line) => line.Split(new[] { ',' }, StringSplitOptions.None);

        static double Parse(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

        static void AssertClose(double expected, double actual)
        {
            var tolerance = Math.Max(1e-24, Math.Abs(expected) * 1e-12);
            Assert.InRange(actual, expected - tolerance, expected + tolerance);
        }

        static string TemporaryPath(string prefix) => Path.Combine(
            Path.GetTempPath(),
            $"{prefix}-{Guid.NewGuid():N}.dat");

        sealed class ExportProbeModel : Model
        {
            readonly IReadOnlyDictionary<int, double> predictions;

            public ExportProbeModel(ExperimentData data, IReadOnlyDictionary<int, double> predictions) : base(data)
            {
                this.predictions = predictions;
            }

            public override double Evaluate(int injectionindex, bool withoffset = true) => predictions[injectionindex];
        }

        sealed class MicroCalImportPromptService : IImportPromptService
        {
            public EnergyUnitPromptResult AskForEnergyUnit(
                string fileName,
                string encounteredValue,
                bool allowQueueReuse)
            {
                var unit = Path.GetFileName(fileName).StartsWith("microcal-export-ndh-", StringComparison.Ordinal)
                    ? EnergyUnit.Cal
                    : EnergyUnit.MicroCal;
                return new EnergyUnitPromptResult(unit, useForRemainingFilesInQueue: false, isCancelled: false);
            }
        }

        sealed class FixedValidationPromptService : IDataValidationPromptService
        {
            readonly string concentration;

            public FixedValidationPromptService(string concentration)
            {
                this.concentration = concentration;
            }

            public DataValidationPromptResult AskValidationIssue(
                string title,
                string message,
                bool canFix,
                bool requiresInput,
                bool allowKeep = true) =>
                new DataValidationPromptResult(DataValidationPromptAction.AttemptFix, concentration);
        }
    }
}
