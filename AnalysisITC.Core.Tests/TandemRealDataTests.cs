using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class TandemRealDataTests
    {
        [Fact]
        public async Task HistoricalProjectRestoresItsSourcesAndSavedMerges()
        {
            var containers = await ReadContainers("280-430-D2mut-1p6mM-JNK-200uM-1.ftxtc");
            var experiments = containers.OfType<ExperimentData>().ToList();
            var sources = experiments.Take(3).ToList();
            var tandems = experiments.Skip(3).ToList();

            Assert.Single(containers.OfType<AnalysisResult>());
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
        public async Task HistoricalSavedTandemConcentrationsSurviveRoundTrip()
        {
            var experiments = await ReadExperiments("280-430-D2mut-1p6mM-JNK-200uM-1.ftxtc");
            var saved = FindSavedMerge(experiments, mixingFraction: 0.20);

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { saved });
            package.Position = 0;
            var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<ExperimentData>());
            AssertTandemStateEqual(saved, restored);
        }

        [Fact]
        public async Task FullProcessingPipelineAddsPartialTailAndRefreshesHistoricalHeatSd()
        {
            var experiments = await ReadExperiments("280-430-D2mut-1p6mM-JNK-200uM-1.ftxtc");
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
            var historicalSds = saved.Injections.Select(injection => injection.RawPeakArea.SD).ToArray();
            await saved.Processor.InterpolateBaseline(replace: false, notify: false, throwOnError: true);
            for (var index = 0; index < merged.Injections.Count; index++)
            {
                var injection = merged.Injections[index];
                var historicalTail = PartialFinalInterval(
                    merged.BaseLineCorrectedDataPoints,
                    injection.IntegrationStartTime,
                    injection.IntegrationEndTime);
                AssertClose(
                    saved.Injections[index].RawPeakArea.Value + historicalTail,
                    injection.RawPeakArea.Value,
                    1e-10);
                // Historical SDs include the old cross-gap normalization. Compare
                // the full processing route with reintegration of the saved
                // baseline instead; independent SD targets live in
                // BaselineAutoCorrelationTests. Keep the on-disk fixture intact.
                saved.Injections[index].Integrate();
                AssertClose(saved.Injections[index].RawPeakArea.SD, injection.RawPeakArea.SD, 1e-10);
            }
            Assert.Contains(Enumerable.Range(0, merged.Injections.Count), index =>
                Math.Abs(historicalSds[index] - merged.Injections[index].RawPeakArea.SD) > 1e-10);
        }

        [Fact]
        public void PerReloadBackMixingUsesThreeIndependentTransitionFractions()
        {
            var sources = Enumerable.Range(1, 4).Select(CreateTandemSource).ToList();
            var settings = new TandemConcatenation.BackMixingSettings
            {
                UseBackMixingMethod = true,
                DidRemoveOverflow = true,
                DeadVolume = 80e-6,
            };

            var noMixing = Merge(sources, settings, 0.0, 0.0, 0.0);
            var firstReload = Merge(sources, settings, 0.10, 0.0, 0.0);
            var firstTwoReloads = Merge(sources, settings, 0.10, 0.20, 0.0);
            var allReloads = Merge(sources, settings, 0.10, 0.20, 0.30);

            AssertSegmentConcentrationsEqual(firstReload.Segments[1], allReloads.Segments[1]);
            AssertSegmentConcentrationsEqual(firstTwoReloads.Segments[2], allReloads.Segments[2]);
            AssertSegmentConcentrationsDiffer(noMixing.Segments[1], allReloads.Segments[1]);
            AssertSegmentConcentrationsDiffer(firstReload.Segments[2], allReloads.Segments[2]);
            AssertSegmentConcentrationsDiffer(firstTwoReloads.Segments[3], allReloads.Segments[3]);
            Assert.Contains("MixFrac=10.0% / 20.0% / 30.0%", allReloads.TandemMergeDescription);

            Assert.Throws<ArgumentException>(() => TandemConcatenation.ConcatTandemWithBackMixing(
                sources,
                settings,
                new[] { 0.10, 0.20 }));
        }

        [Fact]
        public void TandemMergePreservesFirstExperimentDateAndProvenance()
        {
            var first = CreateTandemSource(1);
            first.Date = new DateTime(2024, 3, 14, 9, 26, 53, DateTimeKind.Unspecified);
            first.DateSource = ExperimentDateSource.DataFile;
            var second = CreateTandemSource(2);
            first.ExternalExperimentId = "external-first";
            second.ExternalExperimentId = " external-second ";
            first.CellSampleId = "cell-batch-12";
            second.CellSampleId = "cell-batch-12";
            first.SyringeSampleId = "syringe-batch-8";
            second.SyringeSampleId = "syringe-batch-8";

            var merged = TandemConcatenation.ConcatTandem(new List<ExperimentData> { first, second });

            Assert.Equal(first.Date, merged.Date);
            Assert.Equal(first.DateSource, merged.DateSource);
            Assert.Equal("external-first-concat", merged.ExternalExperimentId);
            Assert.Equal("cell-batch-12", merged.CellSampleId);
            Assert.Equal("syringe-batch-8", merged.SyringeSampleId);

            second.CellSampleId = "different-cell-batch";
            var mixed = TandemConcatenation.ConcatTandem(new List<ExperimentData> { first, second });
            Assert.Equal("", mixed.CellSampleId);
            Assert.Equal("external-first-concat", mixed.ExternalExperimentId);
            second.SyringeSampleId = "different-syringe-batch";
            var mismatchedSamples = TandemConcatenation.ConcatTandem(new List<ExperimentData> { first, second });
            Assert.Equal("", mismatchedSamples.SyringeSampleId);

            first.ExternalExperimentId = "";
            second.ExternalExperimentId = "  external-second  ";
            var missingFirst = TandemConcatenation.ConcatTandem(new List<ExperimentData> { first, second });
            Assert.Equal("", missingFirst.ExternalExperimentId);
            first.CellSampleId = "";
            second.CellSampleId = "";
            first.SyringeSampleId = "";
            second.SyringeSampleId = "";
            first.ExternalExperimentId = "";
            second.ExternalExperimentId = "";
            var noIds = TandemConcatenation.ConcatTandem(new List<ExperimentData> { first, second });
            Assert.Equal("", noIds.ExternalExperimentId);
            Assert.Equal("", noIds.CellSampleId);
            Assert.Equal("", noIds.SyringeSampleId);

            first.ExternalExperimentId = "backmix-first";
            var backmixed = TandemConcatenation.ConcatTandemWithBackMixing(
                new List<ExperimentData> { first, second },
                new TandemConcatenation.BackMixingSettings { UseBackMixingMethod = true, DidRemoveOverflow = false });
            Assert.Equal("backmix-first-concat", backmixed.ExternalExperimentId);
        }

        [Theory]
        [InlineData(TandemMixingCriterion.OneSiteFit, "One-site")]
        [InlineData(TandemMixingCriterion.ModelFree, "Model-free")]
        public void AutomaticMergeCommentsRecordCriterionSettingsAndSelectedFractions(
            TandemMixingCriterion criterion, string expectedCriterion)
        {
            var sources = Enumerable.Range(1, 3).Select(CreateTandemSource).ToList();
            sources[0].Comments = "Original sample note";
            var merged = TandemConcatenation.ConcatTandemWithBackMixing(
                sources,
                new TandemConcatenation.BackMixingSettings
                {
                    UseBackMixingMethod = true,
                    DeadVolume = 80e-6,
                    DidRemoveOverflow = true,
                    RemoveOverflowVolume = 40e-6,
                },
                new[] { 0.043271, 0.208 },
                automaticCriterion: criterion);

            Assert.StartsWith("Original sample note" + Environment.NewLine + Environment.NewLine, merged.Comments);
            Assert.Contains($"auto back-mixing; criterion={expectedCriterion}", merged.Comments);
            Assert.Contains("DeadVolume=80 µL", merged.Comments);
            Assert.Contains("RemoveOverflow=True", merged.Comments);
            Assert.Contains("RemoveOverflowVolume=preceding segment's total injected volume", merged.Comments);
            Assert.DoesNotContain("RemoveOverflowVolume=40", merged.Comments);
            Assert.Contains("MixFrac=4.3271% / 20.8%", merged.Comments);
            Assert.Contains("bookkeeping", merged.Comments);
            Assert.Contains($"criterion={expectedCriterion}", merged.TandemMergeDescription);
            Assert.Equal("Original sample note", sources[0].Comments);
        }

        [Fact]
        public void ManualMergeCommentsRecordModeAndSettingsWithoutAnAutoCriterion()
        {
            var sources = Enumerable.Range(1, 2).Select(CreateTandemSource).ToList();
            sources[0].Comments = "";
            var simple = TandemConcatenation.ConcatTandem(sources);
            Assert.StartsWith("Tandem concatenation", simple.Comments);
            Assert.Contains("no back-mixing", simple.Comments);

            var settings = new TandemConcatenation.BackMixingSettings
            {
                UseBackMixingMethod = true,
                DidRemoveOverflow = false,
                MixingFraction = 0.25,
            };
            var fixedMerge = TandemConcatenation.ConcatTandemWithBackMixing(sources, settings);
            Assert.Contains("fixed back-mixing", fixedMerge.Comments);
            Assert.Contains("RemoveOverflow=False", fixedMerge.Comments);
            Assert.Contains("MixFrac=25.0%", fixedMerge.Comments);
            Assert.DoesNotContain("criterion=", fixedMerge.Comments);
            Assert.DoesNotContain("RemoveOverflowVolume=", fixedMerge.Comments);
            settings.DidRemoveOverflow = true;
            var withRemoval = TandemConcatenation.ConcatTandemWithBackMixing(sources, settings);
            Assert.Contains("RemoveOverflowVolume=preceding segment's total injected volume", withRemoval.Comments);
            Assert.DoesNotContain("RemoveOverflowVolume=0", withRemoval.Comments);

            var individual = TandemConcatenation.ConcatTandemWithBackMixing(sources, settings, new[] { 0.35 });
            Assert.Contains("fixed per-transition back-mixing", individual.Comments);
            Assert.Contains("MixFrac=35.0%", individual.Comments);
            Assert.DoesNotContain("criterion=", individual.Comments);
        }

        static ExperimentData Merge(
            List<ExperimentData> sources,
            TandemConcatenation.BackMixingSettings settings,
            params double[] transitionMixingFractions)
        {
            return TandemConcatenation.ConcatTandemWithBackMixing(
                sources,
                settings,
                transitionMixingFractions);
        }

        static ExperimentData CreateTandemSource(int index)
        {
            var experiment = new ExperimentData($"synthetic-tandem-{index}.itc")
            {
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(100e-6),
                CellVolume = 200e-6,
                DataPoints = new List<DataPoint>
                {
                    new(0, 0, 25),
                    new(1, 0, 25),
                },
            };

            for (var injectionIndex = 0; injectionIndex < 3; injectionIndex++)
                experiment.Injections.Add(new InjectionData(experiment, 5e-6));

            return experiment;
        }

        static void AssertSegmentConcentrationsEqual(
            TandemExperimentSegment expected,
            TandemExperimentSegment actual)
        {
            AssertClose(expected.SegmentInitialActiveCellConc, actual.SegmentInitialActiveCellConc, 1e-12);
            AssertClose(expected.SegmentInitialActiveTitrantConc, actual.SegmentInitialActiveTitrantConc, 1e-12);
        }

        static void AssertSegmentConcentrationsDiffer(
            TandemExperimentSegment expected,
            TandemExperimentSegment actual)
        {
            var cellDifference = Math.Abs(expected.SegmentInitialActiveCellConc - actual.SegmentInitialActiveCellConc);
            var titrantDifference = Math.Abs(expected.SegmentInitialActiveTitrantConc - actual.SegmentInitialActiveTitrantConc);
            Assert.True(cellDifference > 1e-15 || titrantDifference > 1e-15);
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
            var containers = await ReadContainers(fileName);
            return containers.OfType<ExperimentData>().ToList();
        }

        static async Task<ITCDataContainer[]> ReadContainers(string fileName)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Tandem", fileName);
            await using var stream = File.OpenRead(path);
            return await FTXTCReader.ReadStream(stream);
        }

        // The saved fixture was produced before fractional final intervals were
        // included. Calculate only that independently expected tail here, while
        // retaining the historical heat as the baseline for every injection.
        static double PartialFinalInterval(
            IReadOnlyList<DataPoint> data,
            float start,
            float end)
        {
            DataPoint? previous = null;

            foreach (var point in data)
            {
                if (point.Time <= start)
                {
                    previous = point;
                    continue;
                }

                if (point.Time > end)
                    return previous.HasValue && previous.Value.Time < end
                        ? point.Power * (end - previous.Value.Time)
                        : 0;

                previous = point;
            }

            return 0;
        }

        static void AssertClose(double expected, double actual, double tolerance) =>
            Assert.InRange(Math.Abs(expected - actual), 0, tolerance);
    }
}
