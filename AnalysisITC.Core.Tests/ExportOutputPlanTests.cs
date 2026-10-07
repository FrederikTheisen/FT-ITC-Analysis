using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [Collection(FileTypeFixtureCollectionDefinition.Name)]
    public sealed class ExportOutputPlanTests
    {
        const string Folder = "export-folder";

        [Fact]
        public void GeneratedSuffixDoesNotTakeAnotherExperimentsRealName()
        {
            var settings = Settings(ExportType.Peaks, "review", "sample", "sample", "sample_1");

            Assert.Equal(
                new[] { "review_sample_2.csv", "review_sample_3.csv", "review_sample_1.csv" },
                PlannedFileNames(settings));
        }

        [Fact]
        public void UniqueNameThatLooksGeneratedIsKeptAndDuplicatesSkipIt()
        {
            var settings = Settings(ExportType.CSV, "review", "sample", "sample_2", "sample");

            Assert.Equal(
                new[] { "review_sample_1.csv", "review_sample_2.csv", "review_sample_3.csv" },
                PlannedFileNames(settings));
        }

        [Fact]
        public void NamesDifferingOnlyByCaseOrExtensionAreNumbered()
        {
            var settings = Settings(ExportType.MicroCal, "review", "A.itc", "a.csv", "b.itc");

            Assert.Equal(
                new[] { "review_A_1.dat", "review_a_2.dat", "review_b.dat" },
                PlannedFileNames(settings));
        }

        [Fact]
        public void BlankNamesAreNumberedAroundRealNames()
        {
            var settings = Settings(ExportType.Peaks, "review", "  ", "", "experiment_1");

            Assert.Equal(
                new[] { "review_experiment_2.csv", "review_experiment_3.csv", "review_experiment_1.csv" },
                PlannedFileNames(settings));
        }

        [Fact]
        public void SingleExperimentUsesBaseNameOnly()
        {
            var settings = Settings(ExportType.Peaks, "review", "sample");

            Assert.Equal(new[] { "review.csv" }, PlannedFileNames(settings));
        }

        [Fact]
        public void ThermogramExportPlansOnlyExperimentsWithThermograms()
        {
            var settings = Settings(ExportType.Data, "review", "with", "without");
            settings.Data[1].DataPoints = new List<DataPoint>();

            var outputs = Exporter.PlanOutputs(Folder, settings);

            Assert.Same(settings.Data[0], Assert.Single(outputs).data);
            Assert.Equal(Path.Combine(Folder, "review.csv"), outputs[0].path);
        }

        [Fact]
        public async Task CollidingBatchWritesEveryExperimentToItsOwnFile()
        {
            var folder = Path.Combine(Path.GetTempPath(), "ftitc-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var settings = Settings(ExportType.Data, "review", "sample", "sample", "sample_1");

                await Exporter.WriteOutputs(Exporter.PlanOutputs(folder, settings), settings);

                Assert.Equal(
                    new[] { "review_sample_1.csv", "review_sample_2.csv", "review_sample_3.csv" },
                    Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal));

                // Each experiment's thermogram carries its own batch position as power.
                Assert.Equal("1", PowerInFile(folder, "review_sample_2.csv"));
                Assert.Equal("2", PowerInFile(folder, "review_sample_3.csv"));
                Assert.Equal("3", PowerInFile(folder, "review_sample_1.csv"));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        static ExportAccessoryViewSettings Settings(ExportType export, string baseName, params string[] names)
        {
            return new ExportAccessoryViewSettings
            {
                Export = export,
                OutputBaseName = baseName,
                Data = names.Select((name, index) => Experiment(name, index + 1)).ToList(),
            };
        }

        static ExperimentData Experiment(string name, int power)
        {
            return new ExperimentData(name)
            {
                DataPoints = new List<DataPoint>
                {
                    new DataPoint(0, power),
                    new DataPoint(1, power),
                },
            };
        }

        static string[] PlannedFileNames(ExportAccessoryViewSettings settings)
        {
            return Exporter.PlanOutputs(Folder, settings)
                .Select(output =>
                {
                    Assert.Equal(Folder, Path.GetDirectoryName(output.path));
                    return Path.GetFileName(output.path);
                })
                .ToArray();
        }

        static string PowerInFile(string folder, string fileName)
        {
            var lines = File.ReadAllLines(Path.Combine(folder, fileName));
            Assert.Equal("time_s,power_w", lines[0]);
            return lines[1].Split(',')[1];
        }
    }
}
