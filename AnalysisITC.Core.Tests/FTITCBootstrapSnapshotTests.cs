using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class FTITCBootstrapSnapshotTests
    {
        [Fact]
        public async Task RoundTripPreservesBootstrapCurvesOptionsConcentrationsAndBands()
        {
            var source = await CreateExperimentWithSyntheticSnapshots();
            var sourceSolution = source.Solution;
            var expectedCurves = EvaluateBootstrapCurves(sourceSolution);
            var expectedBands = EvaluateConfidenceBands(sourceSolution);
            var expectedOptionValues = sourceSolution.BootstrapSolutions
                .Select(solution => solution.ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue.Value)
                .ToArray();

            var stream = new MemoryStream();
            await FTITCWriter.WriteStream(stream, new[] { source });
            var text = Encoding.UTF8.GetString(stream.ToArray());

            Assert.Contains("LIST:BootSnapshots", text);
            Assert.Contains("BootSnapshotVersion:1", text);
            Assert.DoesNotContain("LIST:BootParameters", text);

            stream.Position = 0;
            var restoredContainers = await FTITCReader.ReadStream(stream);
            var restored = Assert.Single(restoredContainers.OfType<ExperimentData>());
            var restoredSolution = restored.Solution;

            Assert.Equal(sourceSolution.BootstrapSolutions.Count, restoredSolution.BootstrapSolutions.Count);
            Assert.Equal(
                Enumerable.Range(0, restoredSolution.BootstrapSolutions.Count).Select(index => (int?)index),
                restoredSolution.BootstrapSolutions.Select(solution => solution.BootstrapReplicateIndex));
            Assert.Equal(
                restoredSolution.BootstrapSolutions.Count,
                restoredSolution.BootstrapSolutions.Select(solution => solution.Model).Distinct().Count());
            Assert.Equal(
                restoredSolution.BootstrapSolutions.Count,
                restoredSolution.BootstrapSolutions.Select(solution => solution.Data).Distinct().Count());

            AssertMatrixEqual(expectedCurves, EvaluateBootstrapCurves(restoredSolution));
            AssertMatrixEqual(expectedBands, EvaluateConfidenceBands(restoredSolution));

            for (int index = 0; index < restoredSolution.BootstrapSolutions.Count; index++)
            {
                var expected = sourceSolution.BootstrapSolutions[index];
                var actual = restoredSolution.BootstrapSolutions[index];

                AssertClose(expectedOptionValues[index], actual.ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue.Value);
                Assert.NotEqual(
                    restored.Attributes.Single(attribute => attribute.Key == AttributeKey.PreboundLigandConc).ParameterValue.Value,
                    actual.ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue.Value);
                AssertClose(expected.Data.CellConcentration.Value, actual.Data.CellConcentration.Value);
                AssertClose(expected.Data.SyringeConcentration.Value, actual.Data.SyringeConcentration.Value);
                AssertClose(expected.Data.CellVolume, actual.Data.CellVolume);
                AssertClose(expected.Data.MeasuredTemperature, actual.Data.MeasuredTemperature);

                Assert.Equal(expected.Data.Injections.Count, actual.Data.Injections.Count);
                for (int injectionIndex = 0; injectionIndex < expected.Data.Injections.Count; injectionIndex++)
                {
                    var expectedInjection = expected.Data.Injections[injectionIndex];
                    var actualInjection = actual.Data.Injections[injectionIndex];
                    Assert.Equal(expectedInjection.ID, actualInjection.ID);
                    Assert.Equal(expectedInjection.Include, actualInjection.Include);
                    AssertClose(expectedInjection.Volume, actualInjection.Volume);
                    AssertClose(expectedInjection.ActualCellConcentration, actualInjection.ActualCellConcentration);
                    AssertClose(expectedInjection.ActualTitrantConcentration, actualInjection.ActualTitrantConcentration);
                }

                Assert.Equal(expected.Data.Segments.Count, actual.Data.Segments.Count);
                for (int segmentIndex = 0; segmentIndex < expected.Data.Segments.Count; segmentIndex++)
                {
                    var expectedSegment = expected.Data.Segments[segmentIndex];
                    var actualSegment = actual.Data.Segments[segmentIndex];
                    Assert.Equal(expectedSegment.FirstInjectionID, actualSegment.FirstInjectionID);
                    AssertClose(expectedSegment.SegmentInitialActiveCellConc, actualSegment.SegmentInitialActiveCellConc);
                    AssertClose(expectedSegment.SegmentInitialActiveTitrantConc, actualSegment.SegmentInitialActiveTitrantConc);
                }
            }
        }

        [Fact]
        public async Task UnsupportedBootstrapSnapshotDoesNotFallBackSilently()
        {
            var source = await CreateExperimentWithSyntheticSnapshots();
            var stream = new MemoryStream();
            await FTITCWriter.WriteStream(stream, new[] { source });
            var text = Encoding.UTF8.GetString(stream.ToArray())
                .Replace("BootSnapshotVersion:1", "BootSnapshotVersion:99", StringComparison.Ordinal);

            using var malformed = TextStream(text);
            await Assert.ThrowsAsync<InvalidDataException>(() => FTITCReader.ReadStream(malformed));
        }

        [Fact]
        public async Task LegacyBootParametersStillRestoreIndependentModels()
        {
            using var stream = File.OpenRead(Fixture("one-set.ftitc"));
            var containers = await FTITCReader.ReadStream(stream);
            var solution = containers.OfType<ExperimentData>().First().Solution;

            Assert.True(solution.BootstrapSolutions.Count > 1);
            Assert.All(solution.BootstrapSolutions, bootstrap => Assert.Null(bootstrap.BootstrapReplicateIndex));
            Assert.Equal(
                solution.BootstrapSolutions.Count,
                solution.BootstrapSolutions.Select(bootstrap => bootstrap.Model).Distinct().Count());
            var band = solution.Model.EvaluateBootstrap(0, true).DistributionConfidence95;
            Assert.True(band[0] < band[1]);
        }

        [Fact]
        public async Task GlobalRoundTripPairsCommonReplicateIndicesDespiteMissingAndReorderedSnapshots()
        {
            using var stream = File.OpenRead(Fixture("jors.ftitc"));
            var containers = await FTITCReader.ReadStream(stream);
            var sourceResult = containers.OfType<AnalysisResult>()
                .First(result => result.Solution.Solutions.Count > 1
                    && result.Solution.Solutions.All(member => member.BootstrapSolutions.Count >= 3));
            var members = sourceResult.Solution.Solutions;

            for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
            {
                var original = members[memberIndex].BootstrapSolutions.Take(3).ToList();
                for (int index = 0; index < original.Count; index++)
                    original[index].BootstrapReplicateIndex = index;

                if (memberIndex == 1)
                    original = new List<SolutionInterface> { original[2], original[0] };
                members[memberIndex].SetBootstrapSolutions(original);
            }

            var roundTrip = new MemoryStream();
            await FTITCWriter.WriteStream(
                roundTrip,
                containers.OfType<ExperimentData>(),
                new[] { sourceResult });
            roundTrip.Position = 0;
            var restoredContainers = await FTITCReader.ReadStream(roundTrip);
            var restored = Assert.Single(restoredContainers.OfType<AnalysisResult>()).Solution;

            Assert.Equal(2, restored.BootstrapSolutions.Count);
            Assert.All(restored.BootstrapSolutions[0].Solutions,
                solution => Assert.Equal(0, solution.BootstrapReplicateIndex));
            Assert.All(restored.BootstrapSolutions[1].Solutions,
                solution => Assert.Equal(2, solution.BootstrapReplicateIndex));
        }

        static async Task<ExperimentData> CreateExperimentWithSyntheticSnapshots()
        {
            using var fixture = File.OpenRead(Fixture("competitive.ftitc"));
            var containers = await FTITCReader.ReadStream(fixture);
            var experiment = containers.OfType<ExperimentData>()
                .First(item => item.Model?.ModelType == AnalysisModel.CompetitiveBinding
                    && item.Model.ModelOptions.ContainsKey(AttributeKey.PreboundLigandConc));
            var model = experiment.Model;

            experiment.CellConcentration = new FloatWithError(experiment.CellConcentration.Value, experiment.CellConcentration.Value * 0.04);
            experiment.SyringeConcentration = new FloatWithError(experiment.SyringeConcentration.Value, experiment.SyringeConcentration.Value * 0.05);

            var experimentOption = experiment.Attributes.Single(attribute => attribute.Key == AttributeKey.PreboundLigandConc);
            experimentOption.ParameterValue = new FloatWithError(experimentOption.ParameterValue.Value, experimentOption.ParameterValue.Value * 0.06);
            model.ModelOptions[AttributeKey.PreboundLigandConc].BoolValue = true;
            model.ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue = experimentOption.ParameterValue;
            model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue = new FloatWithError(
                model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue.Value,
                0.04);
            model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue = new FloatWithError(
                model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue.Value,
                500);

            var segmentIndex = Math.Min(5, experiment.Injections.Count - 1);
            var previous = experiment.Injections[Math.Max(0, segmentIndex - 1)];
            experiment.ReplaceSegments(new[]
            {
                new TandemExperimentSegment(0, experiment.CellConcentration.Value, 0),
                new TandemExperimentSegment(segmentIndex, previous.ActualCellConcentration, previous.ActualTitrantConcentration),
            });

            model.ModelCloneOptions = new ModelCloneOptions
            {
                ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                IncludeConcentrationErrorsInBootstrap = true,
            };

            var bootstrapSolutions = new List<SolutionInterface>();
            for (int index = 0; index < 8; index++)
            {
                var synthetic = model.GenerateSyntheticModel();
                synthetic.ApplyModelOptions();
                bootstrapSolutions.Add(SolutionInterface.FromModel(synthetic, null));
            }
            experiment.Solution.SetBootstrapSolutions(bootstrapSolutions);

            return experiment;
        }

        static double[][] EvaluateBootstrapCurves(SolutionInterface solution) => solution.BootstrapSolutions
            .Select(bootstrap => solution.Data.Injections
                .Select(injection => bootstrap.Model.EvaluateEnthalpy(injection.ID, true))
                .ToArray())
            .ToArray();

        static double[][] EvaluateConfidenceBands(SolutionInterface solution) => solution.Data.Injections
            .Select(injection => solution.Model.EvaluateBootstrap(injection.ID, true).DistributionConfidence95.ToArray())
            .ToArray();

        static void AssertMatrixEqual(double[][] expected, double[][] actual)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (int row = 0; row < expected.Length; row++)
            {
                Assert.Equal(expected[row].Length, actual[row].Length);
                for (int column = 0; column < expected[row].Length; column++)
                    AssertClose(expected[row][column], actual[row][column]);
            }
        }

        static void AssertClose(double expected, double actual)
        {
            var tolerance = Math.Max(1e-12, Math.Abs(expected) * 1e-12);
            Assert.True(Math.Abs(expected - actual) <= tolerance,
                $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");
        }

        static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

        static MemoryStream TextStream(string text) => new MemoryStream(Encoding.UTF8.GetBytes(text));
    }
}
