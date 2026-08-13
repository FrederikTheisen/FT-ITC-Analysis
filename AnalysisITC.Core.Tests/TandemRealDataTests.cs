using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class TandemRealDataTests
    {
        [Theory]
        [InlineData("tandem-original.ftitc.gz")]
        [InlineData("tandem-process2.ftitc.gz")]
        public async Task HistoricalProjectsRestoreTheirSourcesAndSavedMerges(string fileName)
        {
            var experiments = await ReadExperiments(fileName);
            var sources = experiments.Take(3).ToList();
            var tandems = experiments.Skip(3).ToList();

            Assert.Equal(13, experiments.Count);
            Assert.Equal(new[] { 26, 26, 26 }, sources.Select(source => source.Injections.Count));
            Assert.Equal(new[] { 3959, 3959, 3958 }, sources.Select(source => source.DataPoints.Count));
            Assert.All(sources, source => Assert.False(source.IsTandemExperiment));
            Assert.Equal(10, tandems.Count);
            Assert.All(tandems, tandem =>
            {
                Assert.True(tandem.IsTandemExperiment);
                Assert.Equal(78, tandem.Injections.Count);
                Assert.Equal(11876, tandem.DataPoints.Count);
                Assert.Equal(new[] { 0, 26, 52 }, tandem.Segments.Select(segment => segment.FirstInjectionID));
            });
        }

        [Fact]
        public async Task CurrentMergeReproducesHistoricalBackMixingConcentrations()
        {
            var experiments = await ReadExperiments("tandem-process2.ftitc.gz");
            var sources = experiments.Take(3).ToList();

            AssertMergeMatchesSaved(sources, experiments, mixingFraction: null);
            AssertMergeMatchesSaved(sources, experiments, mixingFraction: 0.025);
            AssertMergeMatchesSaved(sources, experiments, mixingFraction: 0.10);
            var merged = AssertMergeMatchesSaved(sources, experiments, mixingFraction: 0.20);
            AssertMergeMatchesSaved(sources, experiments, mixingFraction: 0.40);

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { merged });
            package.Position = 0;
            var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<ExperimentData>());
            AssertTandemStateEqual(merged, restored);
        }

        [Fact]
        public async Task FullProcessingPipelineReproducesHistoricalMergedHeats()
        {
            var experiments = await ReadExperiments("tandem-process2.ftitc.gz");
            var sources = experiments.Take(3).ToList();
            foreach (var source in sources)
                await source.Processor.ProcessData(replace: false, invalidate: false, showProgress: false);

            var saved = FindSavedMerge(experiments, mixingFraction: 0.20);
            var merged = TandemConcatenation.ConcatTandemWithBackMixing(
                sources,
                new TandemConcatenation.BackMixingSettings
                {
                    UseBackMixingMethod = true,
                    DidRemoveOverflow = true,
                    DeadVolume = 80e-6,
                    MixingFraction = 0.20,
                });

            await merged.Processor.ProcessData(showProgress: false);

            Assert.All(merged.Injections, injection => Assert.True(injection.IsIntegrated));
            for (var index = 0; index < merged.Injections.Count; index++)
            {
                AssertClose(saved.Injections[index].RawPeakArea.Value, merged.Injections[index].RawPeakArea.Value, 1e-10);
                AssertClose(saved.Injections[index].RawPeakArea.SD, merged.Injections[index].RawPeakArea.SD, 1e-10);
            }
        }

        static ExperimentData AssertMergeMatchesSaved(
            List<ExperimentData> sources,
            IReadOnlyList<ExperimentData> experiments,
            double? mixingFraction)
        {
            var saved = FindSavedMerge(experiments, mixingFraction);
            ExperimentData actual;

            if (mixingFraction.HasValue)
            {
                actual = TandemConcatenation.ConcatTandemWithBackMixing(
                    sources,
                    new TandemConcatenation.BackMixingSettings
                    {
                        UseBackMixingMethod = true,
                        DidRemoveOverflow = true,
                        DeadVolume = 80e-6,
                        MixingFraction = mixingFraction.Value,
                    });
            }
            else
            {
                actual = TandemConcatenation.ConcatTandem(sources);
            }

            AssertTandemStateEqual(saved, actual);
            return actual;
        }

        static void AssertTandemStateEqual(ExperimentData expected, ExperimentData actual)
        {
            Assert.True(actual.IsTandemExperiment);
            Assert.Equal(expected.Injections.Count, actual.Injections.Count);
            Assert.Equal(expected.DataPoints.Count, actual.DataPoints.Count);
            Assert.Equal(expected.Segments.Count, actual.Segments.Count);

            for (var index = 0; index < actual.Segments.Count; index++)
            {
                var expectedSegment = expected.Segments[index];
                var actualSegment = actual.Segments[index];
                Assert.Equal(expectedSegment.FirstInjectionID, actualSegment.FirstInjectionID);
                AssertClose(expectedSegment.SegmentInitialActiveCellConc, actualSegment.SegmentInitialActiveCellConc, 1e-12);
                AssertClose(expectedSegment.SegmentInitialActiveTitrantConc, actualSegment.SegmentInitialActiveTitrantConc, 1e-12);
            }

            for (var index = 0; index < actual.Injections.Count; index++)
            {
                var expectedInjection = expected.Injections[index];
                var actualInjection = actual.Injections[index];
                Assert.Equal(expectedInjection.ID, actualInjection.ID);
                Assert.Equal(expectedInjection.Include, actualInjection.Include);
                Assert.Equal(expectedInjection.Time, actualInjection.Time);
                Assert.Equal(expectedInjection.Volume, actualInjection.Volume);
                AssertClose(expectedInjection.ActualCellConcentration, actualInjection.ActualCellConcentration, 1e-12);
                AssertClose(expectedInjection.ActualTitrantConcentration, actualInjection.ActualTitrantConcentration, 1e-12);
            }
        }

        static ExperimentData FindSavedMerge(
            IReadOnlyList<ExperimentData> experiments,
            double? mixingFraction)
        {
            var marker = mixingFraction.HasValue
                ? $"MixFrac={(100 * mixingFraction.Value).ToString("F1", CultureInfo.InvariantCulture)}%"
                : "no back-mixing";

            return Assert.Single(experiments, experiment =>
                experiment.IsTandemExperiment
                && experiment.Comments.Contains(marker, StringComparison.OrdinalIgnoreCase));
        }

        static async Task<List<ExperimentData>> ReadExperiments(string fileName)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tandem", fileName);
            await using var compressed = File.OpenRead(path);
            await using var stream = new GZipStream(compressed, CompressionMode.Decompress);
            var containers = await FTITCReader.ReadStream(stream, processProcessorData: false);
            return containers.OfType<ExperimentData>().ToList();
        }

        static void AssertClose(double expected, double actual, double tolerance) =>
            Assert.InRange(Math.Abs(expected - actual), 0, tolerance);
    }
}
