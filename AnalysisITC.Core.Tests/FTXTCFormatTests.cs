using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Viewer;
using AnalysisITC.Core.Analysis;
using Xunit;
using Buffer = AnalysisITC.Core.Data.Buffer;

namespace AnalysisITC.Core.Tests
{
    [Collection("AutoSaveManager")]
    public sealed class FTXTCFormatTests
    {
        [Fact]
        public async Task LegacyRoundTripReconstructsProcessedStateBeforeRestoringLock()
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var experiment = (await FTITCReader.ReadStream(source)).OfType<ExperimentData>().First();
            var expectedBaseline = experiment.Processor.Interpolator.Baseline.Select(value => value.Value).ToArray();
            var expectedCorrected = experiment.BaseLineCorrectedDataPoints.Select(value => value.Power).ToArray();
            experiment.Processor.Lock();

            using var legacy = new MemoryStream();
            await FTITCWriter.WriteStream(legacy, new[] { experiment });
            legacy.Position = 0;
            var restored = (await FTITCReader.ReadStream(legacy)).OfType<ExperimentData>().Single();

            Assert.True(restored.Processor.IsLocked);
            Assert.True(restored.Processor.BaselineCompleted);
            Assert.Equal(expectedBaseline, restored.Processor.Interpolator.Baseline.Select(value => value.Value).ToArray());
            Assert.Equal(expectedCorrected, restored.BaseLineCorrectedDataPoints.Select(value => value.Power).ToArray());
            Assert.All(restored.Injections, injection => Assert.True(injection.IsIntegrated));
        }

        [Fact]
        public async Task CurrentRoundTripReconstructsCorrectedTraceForLockedProcessor()
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var experiment = (await FTITCReader.ReadStream(source)).OfType<ExperimentData>().First();
            var expectedCorrected = experiment.BaseLineCorrectedDataPoints.Select(value => value.Power).ToArray();
            experiment.Processor.Lock();

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { experiment });
            package.Position = 0;
            var restored = (await FTXTCReader.ReadStream(package)).OfType<ExperimentData>().Single();

            Assert.True(restored.Processor.IsLocked);
            Assert.True(restored.Processor.BaselineCompleted);
            Assert.Equal(expectedCorrected, restored.BaseLineCorrectedDataPoints.Select(value => value.Power).ToArray());
            Assert.All(restored.Injections, injection => Assert.True(injection.IsIntegrated));
        }

        [Fact]
        public void DataPointCopiesAndBaselineSubtractionRetainCoreValues()
        {
            var point = new DataPoint(12.5f, 3.25f, 24.75f);

            var copy = point.Copy();
            var corrected = point.SubtractBaseline(1.5f);

            Assert.Equal(point.Time, copy.Time);
            Assert.Equal(point.Power, copy.Power);
            Assert.Equal(point.Temperature, copy.Temperature);
            Assert.Equal(point.Time, corrected.Time);
            Assert.Equal(1.75f, corrected.Power);
            Assert.Equal(point.Temperature, corrected.Temperature);
        }

        [Fact]
        public async Task RoundTripPreservesExactNumericStateAndFits()
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            var experiments = containers.OfType<ExperimentData>().ToList();
            var experiment = experiments[0];
            var result = Assert.Single(containers.OfType<AnalysisResult>());

            var original = experiment.DataPoints[0];
            experiment.DataPoints[0] = new DataPoint(original.Time, original.Power, original.Temperature);
            experiment.Processor.DiscardIntegratedPoints = false;
            experiment.Processor.IntegrationLengthMode = InjectionData.IntegrationLengthMode.Factor;
            experiment.Processor.IntegrationLengthFactor = 4.25f;
            var expectedBaseline = experiment.Processor.Interpolator.Baseline
                .Select(value => new[] { value.Value, value.SD, value.FloatWithError.Lower, value.FloatWithError.Upper })
                .ToArray();
            var expectedCorrectedFirst = experiment.DataPoints[0]
                .SubtractBaseline((float)experiment.Processor.Interpolator.Baseline[0]);

            var expectedCurves = experiment.Solution.BootstrapSolutions
                .Select(solution => experiment.Injections.Select(injection => solution.Model.EvaluateEnthalpy(injection.ID, true)).ToArray())
                .ToArray();
            var expectedBands = experiment.Injections
                .Select(injection => experiment.Model.EvaluateBootstrap(injection.ID, true).DistributionConfidence95.ToArray())
                .ToArray();

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, experiments, new[] { result });
            package.Position = 0;
            var restored = await FTXTCReader.ReadStream(package);
            var restoredExperiment = restored.OfType<ExperimentData>().Single(item => item.UniqueID == experiment.UniqueID);

            Assert.Equal(original.Time, restoredExperiment.DataPoints[0].Time);
            Assert.Equal(original.Power, restoredExperiment.DataPoints[0].Power);
            Assert.Equal(original.Temperature, restoredExperiment.DataPoints[0].Temperature);
            Assert.Equal(expectedCorrectedFirst.Power, restoredExperiment.BaseLineCorrectedDataPoints[0].Power);
            Assert.Equal(original.Time, restoredExperiment.BaseLineCorrectedDataPoints[0].Time);
            Assert.Equal(original.Temperature, restoredExperiment.BaseLineCorrectedDataPoints[0].Temperature);
            Assert.False(restoredExperiment.Processor.DiscardIntegratedPoints);
            Assert.Equal(InjectionData.IntegrationLengthMode.Factor, restoredExperiment.Processor.IntegrationLengthMode);
            Assert.Equal(4.25f, restoredExperiment.Processor.IntegrationLengthFactor);
            Assert.Equal(expectedBaseline.Length, restoredExperiment.Processor.Interpolator.Baseline.Count);
            for (var index = 0; index < expectedBaseline.Length; index++)
            {
                var actual = restoredExperiment.Processor.Interpolator.Baseline[index].FloatWithError;
                Assert.Equal(expectedBaseline[index][0], actual.Value);
                Assert.Equal(expectedBaseline[index][1], actual.SD);
                Assert.Equal(expectedBaseline[index][2], actual.Lower);
                Assert.Equal(expectedBaseline[index][3], actual.Upper);
            }
            Assert.Equal(experiment.Injections.Select(item => item.RawPeakArea.Value),
                restoredExperiment.Injections.Select(item => item.RawPeakArea.Value));
            Assert.Equal(experiment.Injections.Select(item => item.PeakArea.Value),
                restoredExperiment.Injections.Select(item => item.PeakArea.Value));
            Assert.Equal(experiment.Solution.BootstrapSolutions.Count, restoredExperiment.Solution.BootstrapSolutions.Count);

            for (var replicate = 0; replicate < expectedCurves.Length; replicate++)
            for (var injection = 0; injection < expectedCurves[replicate].Length; injection++)
                Assert.Equal(expectedCurves[replicate][injection],
                    restoredExperiment.Solution.BootstrapSolutions[replicate].Model.EvaluateEnthalpy(
                        restoredExperiment.Injections[injection].ID, true), 10);
            for (var injection = 0; injection < expectedBands.Length; injection++)
            {
                var actual = restoredExperiment.Model.EvaluateBootstrap(
                    restoredExperiment.Injections[injection].ID, true).DistributionConfidence95;
                Assert.Equal(expectedBands[injection][0], actual[0], 10);
                Assert.Equal(expectedBands[injection][1], actual[1], 10);
            }
        }

        [Fact]
        public async Task AdvancedAnalysesRoundTripAndPopulateViewerWithoutRerunning()
        {
            var previousIterations = ResultAnalysisController.CalculationIterations;
            var previousErrorMethod = AppSettings.DefaultErrorEstimationMethod;
            try
            {
                ResultAnalysisController.CalculationIterations = 4;
                AppSettings.DefaultErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
                var result = await PrepareAdvancedResult();
                result.MarkClean();

                Assert.True(await result.SpolarRecordAnalysis.PerformAnalysisAsync());
                Assert.True(await result.ElectrostaticsAnalysis.PerformAnalysisAsync());
                Assert.True(await result.ProtonationAnalysis.PerformAnalysisAsync());
                Assert.True(result.IsModified);

                var expectedSpolar = result.SpolarRecordAnalysis.Result;
                var expectedElectrostatics = result.ElectrostaticsAnalysis;
                var expectedProtonation = result.ProtonationAnalysis;
                var expectedTemperatures = result.Solution.Solutions.Select(solution => solution.Temp).ToArray();
                var expectedTemperatureDependences = result.Solution.TemperatureDependence.ToDictionary(
                    item => item.Key,
                    item => new
                    {
                        Slope = item.Value.Slope,
                        Intercept = item.Value.Intercept,
                        item.Value.ReferenceT,
                    });
                using var package = new MemoryStream();
                await FTXTCWriter.WriteStream(package,
                    result.Solution.Solutions.Select(solution => solution.Data).Distinct(), new[] { result });

                package.Position = 0;
                using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
                using (var reader = new StreamReader(archive.GetEntry("results/000000/result.json").Open()))
                {
                    var json = await reader.ReadToEndAsync();
                    Assert.Contains("\"advancedAnalyses\"", json, StringComparison.Ordinal);
                    Assert.Contains("\"spolarRecord\"", json, StringComparison.Ordinal);
                    Assert.Contains("\"electrostatics\"", json, StringComparison.Ordinal);
                    Assert.Contains("\"protonation\"", json, StringComparison.Ordinal);
                }

                package.Position = 0;
                var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());
                Assert.False(restored.IsModified);
                Assert.NotNull(restored.SpolarRecordAnalysis.Result);
                Assert.True(restored.ElectrostaticsAnalysis.Calculated);
                Assert.NotNull(restored.ProtonationAnalysis.Fit);
                Assert.Equal(expectedTemperatures, restored.Solution.Solutions.Select(solution => solution.Temp));
                Assert.Equal(expectedTemperatureDependences.Keys.OrderBy(value => value),
                    restored.Solution.TemperatureDependence.Keys.OrderBy(value => value));
                foreach (var dependence in expectedTemperatureDependences)
                {
                    var actual = restored.Solution.TemperatureDependence[dependence.Key];
                    Assert.Equal(dependence.Value.Slope.Value, actual.Slope.Value, 10);
                    Assert.Equal(dependence.Value.Slope.Lower, actual.Slope.Lower, 10);
                    Assert.Equal(dependence.Value.Slope.Upper, actual.Slope.Upper, 10);
                    Assert.Equal(dependence.Value.Intercept.Value, actual.Intercept.Value, 10);
                    Assert.Equal(dependence.Value.Intercept.Lower, actual.Intercept.Lower, 10);
                    Assert.Equal(dependence.Value.Intercept.Upper, actual.Intercept.Upper, 10);
                    Assert.Equal(dependence.Value.ReferenceT, actual.ReferenceT, 10);
                }
                Assert.Equal(expectedSpolar.HydrationEntropy.Value, restored.SpolarRecordAnalysis.Result.HydrationEntropy.Value);
                Assert.Equal(expectedSpolar.ConformationalEntropy.SD, restored.SpolarRecordAnalysis.Result.ConformationalEntropy.SD);
                Assert.Equal(expectedElectrostatics.Kd0.Value, restored.ElectrostaticsAnalysis.Kd0.Value);
                Assert.Equal(expectedElectrostatics.CounterIonReleaseFit.Intercept.Lower,
                    restored.ElectrostaticsAnalysis.CounterIonReleaseFit.Intercept.Lower);
                Assert.Equal(expectedProtonation.BindingEnthalpy.Value, restored.ProtonationAnalysis.BindingEnthalpy.Value);
                Assert.Equal(expectedProtonation.ProtonationChange.Upper, restored.ProtonationAnalysis.ProtonationChange.Upper);
                Assert.Equal(4, restored.ProtonationAnalysis.CompletedIterations);
                Assert.NotNull(restored.ProtonationAnalysis.CompletedAtUtc);

                package.Position = 0;
                var viewer = await new ViewerDocumentReader().ReadAsync(package, "advanced.ftxtc", ViewerFileFormat.Ftxtc);
                var viewerResult = Assert.Single(viewer.AnalysisResults);
                Assert.NotNull(viewerResult.AdvancedAnalyses?.SpolarRecord);
                Assert.NotNull(viewerResult.AdvancedAnalyses?.Electrostatics);
                Assert.NotNull(viewerResult.AdvancedAnalyses?.Protonation);
                var temperature = viewerResult.AdvancedAnalyses.SpolarRecord;
                Assert.NotEmpty(temperature.TemperatureDependencePlot.Series);
                var enthalpyPoints = Assert.Single(temperature.TemperatureDependencePlot.Series,
                    series => series.Group == ParameterType.Enthalpy1.ToString() && series.Kind == "points");
                var enthalpyLine = Assert.Single(temperature.TemperatureDependencePlot.Series,
                    series => series.Group == ParameterType.Enthalpy1.ToString() && series.Kind == "line");
                Assert.Equal(expectedTemperatures.OrderBy(value => value), enthalpyPoints.X);
                var expectedEnthalpies = restored.Solution.Solutions
                    .OrderBy(solution => solution.Temp)
                    .Select(solution => solution.ReportParameters[ParameterType.Enthalpy1].Value / 1000.0)
                    .ToArray();
                Assert.Equal(expectedEnthalpies.Length, enthalpyPoints.Y.Length);
                for (var index = 0; index < expectedEnthalpies.Length; index++)
                    Assert.Equal(expectedEnthalpies[index], enthalpyPoints.Y[index], 10);
                Assert.Equal(81, enthalpyLine.X.Length);
                Assert.Equal(enthalpyLine.X.Length, enthalpyLine.Lower.Length);
                Assert.Equal(enthalpyLine.X.Length, enthalpyLine.Upper.Length);
                Assert.True(restored.Solution.BootstrapSolutions.Count > 1);
                var midpoint = enthalpyLine.X.Length / 2;
                var bootstrapValues = restored.Solution.BootstrapSolutions
                    .Select(solution => solution.TemperatureDependence[ParameterType.Enthalpy1])
                    .Select(fit => ((enthalpyLine.X[midpoint] - fit.ReferenceT) * fit.Slope.Value
                        + fit.Intercept.Value) / 1000.0)
                    .OrderBy(value => value)
                    .ToArray();
                Assert.Equal(Percentile(bootstrapValues, 0.025), enthalpyLine.Lower[midpoint], 10);
                Assert.Equal(Percentile(bootstrapValues, 0.975), enthalpyLine.Upper[midpoint], 10);
                var expectedHydration = restored.SpolarRecordAnalysis.Result.HydrationContribution(
                    restored.SpolarRecordAnalysis.Result.ReferenceTemperature.Value);
                var expectedConformation = restored.SpolarRecordAnalysis.Result.ConformationalContribution(
                    restored.SpolarRecordAnalysis.Result.ReferenceTemperature.Value);
                Assert.Equal(expectedHydration.Value / 1000.0,
                    temperature.HydrationContributionKilojoulesPerMole.Value, 12);
                Assert.Equal(expectedHydration.SD / 1000.0,
                    temperature.HydrationContributionKilojoulesPerMole.Sd, 12);
                Assert.Equal(expectedHydration.Lower / 1000.0,
                    temperature.HydrationContributionKilojoulesPerMole.ConfidenceLower.Value, 12);
                Assert.Equal(expectedConformation.Value / 1000.0,
                    temperature.ConformationalContributionKilojoulesPerMole.Value, 12);
                Assert.Equal(expectedConformation.SD / 1000.0,
                    temperature.ConformationalContributionKilojoulesPerMole.Sd, 12);
                Assert.Equal(expectedConformation.Upper / 1000.0,
                    temperature.ConformationalContributionKilojoulesPerMole.ConfidenceUpper.Value, 12);
                Assert.Equal(restored.SpolarRecordAnalysis.Result.Rvalue.Value,
                    temperature.ResidueEstimate.Value, 12);
                Assert.Equal(restored.SpolarRecordAnalysis.Result.ReferenceTemperature.Value,
                    temperature.ReferenceTemperatureCelsius.Value, 12);
                Assert.Equal(3, viewerResult.AdvancedAnalyses.Electrostatics.Plots.Count);
                var debyePlot = viewerResult.AdvancedAnalyses.Electrostatics.Plots.Single(plot => plot.Key == "debye-huckel");
                var debyeFit = debyePlot.Series.Single(series => series.Label == "Saved fit");
                Assert.Equal(restored.ElectrostaticsAnalysis.IonicStrengthDependenceFit.Evaluate(debyeFit.X[40]),
                    debyeFit.Y[40], 12);
                Assert.NotEmpty(viewerResult.AdvancedAnalyses.Protonation.Plot.Series);
            }
            finally
            {
                ResultAnalysisController.CalculationIterations = previousIterations;
                AppSettings.DefaultErrorEstimationMethod = previousErrorMethod;
            }
        }

        [Fact]
        public async Task TemperatureViewerPlotFallsBackToSavedFitIntervalsWithoutBootstrapReplicates()
        {
            var result = await PrepareAdvancedResult(includeBootstrapReplicates: false);
            result.SpolarRecordAnalysis.RestoreResult(
                FTSRMethod.SRFoldedMode.Glob,
                FTSRMethod.SRTempMode.ReferenceTemperature,
                new FTSRMethod.SROutput(
                    new FloatWithError(-0.11, 0.01),
                    new FloatWithError(-0.22, 0.02),
                    new FloatWithError(42, 2),
                    new FloatWithError(25, 0.5)),
                completedIterations: 0,
                completedAtUtc: DateTime.UtcNow);

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package,
                result.Solution.Solutions.Select(solution => solution.Data).Distinct(), new[] { result });
            package.Position = 0;

            var document = await new ViewerDocumentReader().ReadAsync(
                package, "advanced-no-bootstrap.ftxtc", ViewerFileFormat.Ftxtc);
            var temperature = Assert.Single(document.AnalysisResults).AdvancedAnalyses.SpolarRecord;
            var lines = temperature.TemperatureDependencePlot.Series
                .Where(series => series.Kind == "line").ToArray();

            Assert.Equal(3, lines.Length);
            Assert.All(lines, line =>
            {
                Assert.NotEmpty(line.X);
                Assert.Equal(line.X.Length, line.Y.Length);
                Assert.Equal(line.X.Length, line.Lower.Length);
                Assert.Equal(line.X.Length, line.Upper.Length);
                Assert.All(line.X.Concat(line.Y).Concat(line.Lower).Concat(line.Upper),
                    value => Assert.True(double.IsFinite(value)));
                for (var index = 0; index < line.Y.Length; index++)
                {
                    Assert.True(line.Lower[index] <= line.Y[index]);
                    Assert.True(line.Upper[index] >= line.Y[index]);
                }
            });
        }

        [Fact]
        public async Task TwoSiteTemperatureViewerPlotIncludesBothThermodynamicSites()
        {
            using var source = File.OpenRead(Fixture("two-sites.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            var sourceResult = Assert.Single(containers.OfType<AnalysisResult>());
            var members = sourceResult.Solution.Solutions;
            Assert.Equal(2, members.Count);
            members[0].Data.MeasuredTemperature = 20;
            members[1].Data.MeasuredTemperature = 30;

            var globalModel = new GlobalModel(members.Select(solution => solution.Model).ToList())
            {
                Parameters = sourceResult.Model.Parameters,
                ModelCloneOptions = sourceResult.Model.ModelCloneOptions,
            };
            var globalSolver = new GlobalSolver
            {
                Model = globalModel,
                ErrorEstimationMethod = sourceResult.Solution.ErrorEstimationMethod,
                UseErrorWeightedFitting = sourceResult.Solution.UseWeightedFitting,
            };
            var globalSolution = new GlobalSolution(globalSolver, members, sourceResult.Solution.Convergence);
            globalModel.Solution = globalSolution;
            var result = new AnalysisResult(globalSolution);
            Assert.NotNull(result.SpolarRecordAnalysis);
            result.SpolarRecordAnalysis.RestoreResult(
                FTSRMethod.SRFoldedMode.Glob,
                FTSRMethod.SRTempMode.ReferenceTemperature,
                new FTSRMethod.SROutput(
                    new FloatWithError(-0.11, 0.01),
                    new FloatWithError(-0.22, 0.02),
                    new FloatWithError(42, 2),
                    new FloatWithError(25, 0.5)),
                completedIterations: 0,
                completedAtUtc: DateTime.UtcNow);

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package,
                members.Select(solution => solution.Data).Distinct(), new[] { result });
            package.Position = 0;
            var document = await new ViewerDocumentReader().ReadAsync(
                package, "advanced-two-site.ftxtc", ViewerFileFormat.Ftxtc);
            var series = Assert.Single(document.AnalysisResults).AdvancedAnalyses.SpolarRecord
                .TemperatureDependencePlot.Series;

            foreach (var parameter in new[]
                     {
                         ParameterType.Enthalpy1,
                         ParameterType.EntropyContribution1,
                         ParameterType.Gibbs1,
                         ParameterType.Enthalpy2,
                         ParameterType.EntropyContribution2,
                         ParameterType.Gibbs2,
                     })
            {
                Assert.Single(series, item => item.Group == parameter.ToString() && item.Kind == "points");
                Assert.Single(series, item => item.Group == parameter.ToString() && item.Kind == "line");
            }
        }

        [Fact]
        public async Task CancelledAdvancedRerunKeepsLastSuccessfulState()
        {
            var previousIterations = ResultAnalysisController.CalculationIterations;
            try
            {
                ResultAnalysisController.CalculationIterations = 4;
                var result = await PrepareAdvancedResult();
                Assert.True(await result.SpolarRecordAnalysis.PerformAnalysisAsync());
                var expected = result.SpolarRecordAnalysis.Result;
                var expectedCompletedAt = result.SpolarRecordAnalysis.CompletedAtUtc;
                result.MarkClean();

                EventHandler<Tuple<int, int, float, string>> cancel = (_, _) =>
                    ResultAnalysisController.TerminateAnalysisFlag.Raise();
                ResultAnalysisController.IterationFinished += cancel;
                try
                {
                    Assert.False(await result.SpolarRecordAnalysis.PerformAnalysisAsync());
                }
                finally
                {
                    ResultAnalysisController.IterationFinished -= cancel;
                }

                Assert.Same(expected, result.SpolarRecordAnalysis.Result);
                Assert.Equal(expectedCompletedAt, result.SpolarRecordAnalysis.CompletedAtUtc);
                Assert.False(result.IsModified);
            }
            finally
            {
                ResultAnalysisController.CalculationIterations = previousIterations;
            }
        }

        [Fact]
        public async Task FailedAdvancedRerunKeepsLastSuccessfulState()
        {
            var result = await PrepareAdvancedResult();
            var analysis = new FailingAdvancedAnalysis(result);

            Assert.True(await analysis.PerformAnalysisAsync());
            var expectedCompletedAt = analysis.CompletedAtUtc;
            Assert.Equal(1, analysis.Value);
            result.MarkClean();

            analysis.FailNextRun = true;
            Assert.False(await analysis.PerformAnalysisAsync());

            Assert.Equal(1, analysis.Value);
            Assert.Equal(1, analysis.CompletedIterations);
            Assert.Equal(expectedCompletedAt, analysis.CompletedAtUtc);
            Assert.False(result.IsModified);
        }

        [Fact]
        public async Task InvalidAdvancedSubtypeIsIsolatedOnlyInRecoveryMode()
        {
            var previousIterations = ResultAnalysisController.CalculationIterations;
            try
            {
                ResultAnalysisController.CalculationIterations = 2;
                var result = await PrepareAdvancedResult();
                Assert.True(await result.SpolarRecordAnalysis.PerformAnalysisAsync());
                using var package = new MemoryStream();
                await FTXTCWriter.WriteStream(package,
                    result.Solution.Solutions.Select(solution => solution.Data).Distinct(), new[] { result });
                using var corrupt = RewriteAuthenticatedPackage(package, (path, bytes) =>
                {
                    if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
                    var root = JsonNode.Parse(bytes).AsObject();
                    root["advancedAnalyses"]["spolarRecord"]["schemaVersion"] = 99;
                    return Encoding.UTF8.GetBytes(root.ToJsonString(FTXTCFormat.JsonOptions));
                });

                await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadStream(corrupt));
                corrupt.Position = 0;
                var recovered = await FTXTCReader.ReadWithRecovery(corrupt, FtxtcReadPolicy.RecoverUsableContent);
                var restored = Assert.Single(recovered.Containers.OfType<AnalysisResult>());
                Assert.Null(restored.SpolarRecordAnalysis.Result);
                Assert.Contains(recovered.Issues, issue => issue.Code == "advanced-analysis-unavailable");
            }
            finally
            {
                ResultAnalysisController.CalculationIterations = previousIterations;
            }
        }

        [Fact]
        public async Task Schema11AndLegacyFtitcContainNoAdvancedAnalysisStorage()
        {
            var previousIterations = ResultAnalysisController.CalculationIterations;
            try
            {
                ResultAnalysisController.CalculationIterations = 2;
                var result = await PrepareAdvancedResult();
                Assert.True(await result.SpolarRecordAnalysis.PerformAnalysisAsync());

                using var legacyText = new MemoryStream();
                await FTITCWriter.WriteStream(legacyText,
                    result.Solution.Solutions.Select(solution => solution.Data).Distinct(), new[] { result });
                var legacyContents = Encoding.UTF8.GetString(legacyText.ToArray());
                Assert.DoesNotContain("AdvancedAnalyses", legacyContents, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("SpolarRecord", legacyContents, StringComparison.OrdinalIgnoreCase);

                using var current = new MemoryStream();
                await FTXTCWriter.WriteStream(current,
                    result.Solution.Solutions.Select(solution => solution.Data).Distinct(), new[] { result });
                using var schema11 = RewriteAuthenticatedPackage(current, (path, bytes) =>
                {
                    if (!path.EndsWith("/result.json", StringComparison.Ordinal)) return bytes;
                    var root = JsonNode.Parse(bytes).AsObject();
                    root.Remove("advancedAnalyses");
                    return Encoding.UTF8.GetBytes(root.ToJsonString(FTXTCFormat.JsonOptions));
                }, schemaMinor: 1);
                var restored = Assert.Single((await FTXTCReader.ReadStream(schema11)).OfType<AnalysisResult>());
                Assert.Null(restored.SpolarRecordAnalysis.Result);
                Assert.False(restored.ElectrostaticsAnalysis.Calculated);
                Assert.Null(restored.ProtonationAnalysis.Fit);
            }
            finally
            {
                ResultAnalysisController.CalculationIterations = previousIterations;
            }
        }

        [Fact]
        public async Task PackageHasVersionedManifestAndTypedPayloads()
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            var results = containers.OfType<AnalysisResult>().ToList();
            foreach (var result in results)
                result.SetValiditySnapshot(AnalysisResultValiditySnapshot.Capture(result.Solution));
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, containers.OfType<ExperimentData>(), results);
            package.Position = 0;
            using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

            Assert.NotNull(archive.GetEntry("manifest.json"));
            Assert.NotNull(archive.GetEntry("project.json"));
            Assert.NotNull(archive.GetEntry("experiments/000000/experiment.json"));
            Assert.NotNull(archive.GetEntry("experiments/000000/thermogram.ftxb"));
            Assert.NotNull(archive.GetEntry("experiments/000000/baseline.ftxb"));
            Assert.Null(archive.GetEntry("experiments/000000/corrected-trace.ftxb"));
            Assert.NotNull(archive.GetEntry("solutions/000000/solution.json"));
            Assert.NotNull(archive.GetEntry("solutions/000000/bootstrap.json"));
            Assert.NotNull(archive.GetEntry("solutions/000000/bootstrap-parameters.ftxb"));
            Assert.NotNull(archive.GetEntry("solutions/000000/bootstrap-parameter-locks.ftxb"));
            Assert.NotNull(archive.GetEntry("solutions/000000/bootstrap-injections.ftxb"));
            Assert.NotNull(archive.GetEntry("solutions/000000/bootstrap-injection-includes.ftxb"));
            Assert.NotNull(archive.GetEntry("results/000000/result.json"));

            using (var thermogramStream = archive.GetEntry("experiments/000000/thermogram.ftxb").Open())
            using (var thermogramBytes = new MemoryStream())
            {
                thermogramStream.CopyTo(thermogramBytes);
                var thermogram = FtxbCodec.DecodeFloat32(
                    thermogramBytes.ToArray(), "experiments/000000/thermogram.ftxb");
                Assert.Equal(3, thermogram.GetLength(1));
            }

            using var projectReader = new StreamReader(archive.GetEntry("project.json").Open());
            var projectText = await projectReader.ReadToEndAsync();
            Assert.DoesNotContain("semanticGraph", projectText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("payloadBase64", projectText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FTITCVersion", projectText, StringComparison.Ordinal);
            Assert.DoesNotContain("correctedTrace", projectText, StringComparison.OrdinalIgnoreCase);

            using var experimentReader = new StreamReader(archive.GetEntry("experiments/000000/experiment.json").Open());
            var experimentText = await experimentReader.ReadToEndAsync();
            Assert.DoesNotContain("correctedPeakArea", experimentText, StringComparison.OrdinalIgnoreCase);

            using var solutionReader = new StreamReader(archive.GetEntry("solutions/000000/solution.json").Open());
            var solutionText = await solutionReader.ReadToEndAsync();
            Assert.Contains("\"isValid\": true", solutionText, StringComparison.Ordinal);

            using var resultReader = new StreamReader(archive.GetEntry("results/000000/result.json").Open());
            var resultText = await resultReader.ReadToEndAsync();
            Assert.Contains("\"isValid\": true", resultText, StringComparison.Ordinal);
            Assert.Contains("\"peakArea\"", resultText, StringComparison.Ordinal);
            Assert.DoesNotContain("\"baselineType\"", resultText, StringComparison.Ordinal);

            using var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json").Open());
            Assert.Equal("ftxtc", manifest.RootElement.GetProperty("format").GetString());
            Assert.Equal(1, manifest.RootElement.GetProperty("schemaMajor").GetInt32());
            Assert.Equal(2, manifest.RootElement.GetProperty("schemaMinor").GetInt32());
            Assert.All(manifest.RootElement.GetProperty("entries").EnumerateArray(), entry =>
            {
                Assert.Equal(64, entry.GetProperty("sha256").GetString().Length);
                Assert.True(entry.GetProperty("length").GetInt64() >= 0);
            });
        }

        [Fact]
        public async Task ReadOnlyViewerOpensFtxtc()
        {
            using var package = await CreatePackage();
            var document = await new ViewerDocumentReader().ReadAsync(package, "project.ftxtc", ViewerFileFormat.Ftxtc);

            Assert.Equal("ftxtc", document.Format);
            Assert.Equal("1.2", document.FormatVersion);
            Assert.NotEmpty(document.Experiments);
            Assert.NotEmpty(document.AnalysisResults);
        }

        [Fact]
        public async Task GlobalReplicatesRemainPairedByExplicitIndex()
        {
            using var source = File.OpenRead(Fixture("jors.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            var sourceResult = containers.OfType<AnalysisResult>()
                .First(result => result.Solution.Solutions.Count > 1
                    && result.Solution.Solutions.All(member => member.BootstrapSolutions.Count >= 3));

            for (var memberIndex = 0; memberIndex < sourceResult.Solution.Solutions.Count; memberIndex++)
            {
                var replicates = sourceResult.Solution.Solutions[memberIndex].BootstrapSolutions.Take(3).ToList();
                for (var index = 0; index < replicates.Count; index++) replicates[index].BootstrapReplicateIndex = index;
                if (memberIndex == 1) replicates = new List<SolutionInterface> { replicates[2], replicates[0] };
                sourceResult.Solution.Solutions[memberIndex].SetBootstrapSolutions(replicates);
            }

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, containers.OfType<ExperimentData>(), new[] { sourceResult });
            package.Position = 0;
            var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>()).Solution;

            Assert.Equal(2, restored.BootstrapSolutions.Count);
            Assert.All(restored.BootstrapSolutions[0].Solutions, solution => Assert.Equal(0, solution.BootstrapReplicateIndex));
            Assert.All(restored.BootstrapSolutions[1].Solutions, solution => Assert.Equal(2, solution.BootstrapReplicateIndex));
        }

        [Fact]
        public async Task AppendingSamePackageRemapsCollidingIdsAndInternalReferences()
        {
            var path = Path.Combine(Path.GetTempPath(), "ftxtc-append-" + Guid.NewGuid().ToString("N") + ".ftxtc");
            try
            {
                DataManager.Init();
                using (var source = File.OpenRead(Fixture("one-set.ftitc")))
                {
                    var containers = await FTITCReader.ReadStream(source);
                    await FTXTCWriter.WriteFileAsync(path, containers.OfType<ExperimentData>(), containers.OfType<AnalysisResult>());
                }

                Assert.True((await DataReader.ReadPathsAsync(new[] { path })).OpenedCleanProject);
                var experimentCount = DataManager.Data.Count;
                var resultCount = DataManager.Results.Count;
                await DataReader.ReadPathsAsync(new[] { path });

                Assert.Equal(experimentCount * 2, DataManager.Data.Count);
                Assert.Equal(resultCount * 2, DataManager.Results.Count);
                Assert.Equal(DataManager.SourceItems.Count,
                    DataManager.SourceItems.Select(item => item.UniqueID).Distinct(StringComparer.Ordinal).Count());
                var appendedExperimentIds = new HashSet<string>(
                    DataManager.Data.Skip(experimentCount).Select(item => item.UniqueID), StringComparer.Ordinal);
                Assert.All(DataManager.Results.Skip(resultCount).SelectMany(result => result.Solution.Solutions),
                    solution => Assert.Contains(solution.Data.UniqueID, appendedExperimentIds));
            }
            finally
            {
                DataManager.Init();
                FTITCFormat.CurrentAccessedAppDocumentPath = "";
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public async Task ChecksumFailureDoesNotFallBackToEmbeddedState()
        {
            using var package = await CreatePackage();
            using var corrupt = RewritePackage(package, (path, bytes) =>
                path == "experiments/000000/experiment.json" ? bytes.Concat(new byte[] { 0 }).ToArray() : bytes);

            var error = await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadStream(corrupt));
            Assert.Contains("length", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task UnsupportedFutureSchemaIsRejected()
        {
            using var package = await CreatePackage();
            using var future = RewritePackage(package, (path, bytes) =>
            {
                if (path != "manifest.json") return bytes;
                using var json = JsonDocument.Parse(bytes);
                var text = Encoding.UTF8.GetString(bytes).Replace("\"schemaMajor\": 1", "\"schemaMajor\": 2");
                return Encoding.UTF8.GetBytes(text);
            });

            await Assert.ThrowsAsync<NotSupportedException>(() => FTXTCReader.ReadStream(future));
        }

        [Fact]
        public async Task RecoveryRetainsIntegratedExperimentWhenThermogramIsCorrupt()
        {
            using var package = await CreatePackage();
            using var corrupt = RewritePackage(package, (path, bytes) =>
                path == "experiments/000000/thermogram.ftxb" ? bytes.Concat(new byte[] { 0 }).ToArray() : bytes);

            var recovered = await FTXTCReader.ReadWithRecovery(corrupt, FtxtcReadPolicy.RecoverUsableContent);
            var experiment = Assert.Single(recovered.Containers.OfType<ExperimentData>(), item => item.DataPoints.Count == 0);
            Assert.True(recovered.IsPartial);
            Assert.Empty(experiment.DataPoints);
            Assert.NotEmpty(experiment.Injections);
            Assert.Contains(recovered.Issues, issue => issue.Code == "checksum-failure");

            corrupt.Position = 0;
            await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadStream(corrupt));
        }

        [Fact]
        public async Task SaveLoadSaveKeepsNormalizedPayloadHashes()
        {
            using var first = await CreatePackage();
            var restored = await FTXTCReader.ReadStream(first);
            using var second = new MemoryStream();
            await FTXTCWriter.WriteStream(second, restored.OfType<ExperimentData>(), restored.OfType<AnalysisResult>());

            Assert.Equal(PayloadHashes(first), PayloadHashes(second));
        }

        [Fact]
        public async Task InvalidMemberAndGlobalValidityRoundTripIndependently()
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            var experiments = containers.OfType<ExperimentData>().ToList();
            var result = Assert.Single(containers.OfType<AnalysisResult>());
            var member = result.Solution.Solutions.First();
            var memberExperimentId = member.Data.UniqueID;

            member.RestoreValidity(false);
            result.Solution.RestoreValidity(true);
            using (var package = new MemoryStream())
            {
                await FTXTCWriter.WriteStream(package, experiments, new[] { result });
                package.Position = 0;
                var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());
                Assert.False(restored.Solution.Solutions.Single(value => value.Data.UniqueID == memberExperimentId).IsValid);
                Assert.All(restored.Solution.Solutions.Where(value => value.Data.UniqueID != memberExperimentId),
                    value => Assert.True(value.IsValid));
                Assert.True(restored.Solution.IsValid);
            }

            foreach (var solution in result.Solution.Solutions) solution.RestoreValidity(true);
            result.Solution.RestoreValidity(false);
            using (var package = new MemoryStream())
            {
                await FTXTCWriter.WriteStream(package, experiments, new[] { result });
                package.Position = 0;
                var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<AnalysisResult>());
                Assert.All(restored.Solution.Solutions, value => Assert.True(value.IsValid));
                Assert.False(restored.Solution.IsValid);
            }
        }

        [Fact]
        public async Task BootstrapRestorationIgnoresCurrentParameterLimits()
        {
            var previous = AppSettings.ParameterLimitSetting;
            try
            {
                using var source = File.OpenRead(Fixture("one-set.ftitc"));
                var containers = await FTITCReader.ReadStream(source);
                var experiment = containers.OfType<ExperimentData>().First();
                var bootstrap = experiment.Solution.BootstrapSolutions.First();
                var parameter = bootstrap.Model.Parameters.Table[ParameterType.Offset];
                AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
                var standardLimits = new Parameter(ParameterType.Offset, parameter.Value).Limits;
                var outsideStandardLimit = standardLimits[1] * 1.5;
                parameter.Update(outsideStandardLimit);
                bootstrap.Parameters[ParameterType.Offset] = new FloatWithError(outsideStandardLimit);
                var expectedCount = experiment.Solution.BootstrapSolutions.Count;
                var expectedIndices = experiment.Solution.BootstrapSolutions
                    .Select((value, index) => value.BootstrapReplicateIndex ?? index).ToArray();

                AppSettings.ParameterLimitSetting = ParameterLimitSetting.NoLimit;
                using var package = new MemoryStream();
                await FTXTCWriter.WriteStream(package, containers.OfType<ExperimentData>(), containers.OfType<AnalysisResult>());

                AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
                package.Position = 0;
                var restored = (await FTXTCReader.ReadStream(package)).OfType<ExperimentData>()
                    .Single(value => value.UniqueID == experiment.UniqueID).Solution;

                Assert.Equal(expectedCount, restored.BootstrapSolutions.Count);
                Assert.Equal(expectedIndices, restored.BootstrapSolutions.Select(value => value.BootstrapReplicateIndex.Value));
                Assert.Equal(outsideStandardLimit,
                    restored.BootstrapSolutions.First().Model.Parameters.Table[ParameterType.Offset].Value);
            }
            finally
            {
                AppSettings.ParameterLimitSetting = previous;
            }
        }

        [Fact]
        public void StableAttributeValueRegistryCoversEveryEnumValue()
        {
            Assert.All(Enum.GetValues<Buffer>(), value =>
            {
                var id = FtxtcWireIds.AttributeValueId(AttributeKey.Buffer, (int)value);
                Assert.False(string.IsNullOrWhiteSpace(id));
                Assert.Equal((int)value, FtxtcWireIds.AttributeIntValue(AttributeKey.Buffer, id, null));
            });
            Assert.All(Enum.GetValues<Salt>(), value =>
            {
                var id = FtxtcWireIds.AttributeValueId(AttributeKey.Salt, (int)value);
                Assert.Equal((int)value, FtxtcWireIds.AttributeIntValue(AttributeKey.Salt, id, null));
            });
            Assert.All(Enum.GetValues<BufferSubtractionMethod>(), value =>
            {
                var id = FtxtcWireIds.AttributeValueId(AttributeKey.BufferSubtraction, (int)value);
                Assert.Equal((int)value, FtxtcWireIds.AttributeIntValue(AttributeKey.BufferSubtraction, id, null));
            });
            Assert.All(Enum.GetValues<ExperimentSpeciesLocation>(), value =>
            {
                var id = FtxtcWireIds.AttributeValueId(AttributeKey.Species, (int)value);
                Assert.Equal((int)value, FtxtcWireIds.AttributeIntValue(AttributeKey.Species, id, null));
            });
            Assert.Equal(4, FtxtcWireIds.AttributeIntValue(AttributeKey.NumberOfSites1, null, 4));
            Assert.Throws<NotSupportedException>(() =>
                FtxtcWireIds.AttributeIntValue(AttributeKey.BufferSubtraction, "future-method", null));
        }

        [Fact]
        public async Task CorrectedPeakAreasAreRebuiltBeforeCoreAndViewerReturn()
        {
            var target = await LoadExperiment("one-set.ftitc");
            var reference = await LoadExperiment("one-set.ftitc");
            target.SetID("target-experiment");
            target.Name = "Target";
            target.Model = null;
            reference.SetID("reference-experiment");
            reference.Name = "Reference";
            reference.Model = null;
            target.Attributes.Add(new BufferSubtractionSettings(reference.UniqueID, BufferSubtractionMethod.MatchedInjection).ToAttribute());

            var subtraction = BufferSubtractionCalculator.BuildModel(reference, target.BufferSubtractionSettings);
            var expected = target.Injections[0].RawPeakArea;
            if (BufferSubtractionCalculator.TryGetReferenceHeat(target.Injections[0], subtraction, out var referenceHeat))
                expected -= referenceHeat;
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { target, reference });
            package.Position = 0;
            var restored = (await FTXTCReader.ReadStream(package)).OfType<ExperimentData>()
                .Single(value => value.UniqueID == target.UniqueID);
            Assert.Equal(expected.Value, restored.Injections[0].PeakArea.Value, 12);
            Assert.Equal(expected.SD, restored.Injections[0].PeakArea.SD, 12);

            package.Position = 0;
            var document = await new ViewerDocumentReader().ReadAsync(package, "project.ftxtc", ViewerFileFormat.Ftxtc);
            var viewerTarget = document.Experiments.Single(value => value.Name == target.Name);
            Assert.Equal(expected.Value * 1e6, viewerTarget.Integrated.CorrectedHeatMicrojoules[0].Value, 8);
        }

        [Fact]
        public async Task MissingBufferReferenceFailsStrictAndWarnsDuringRecovery()
        {
            var target = await LoadExperiment("one-set.ftitc");
            target.Model = null;
            target.Attributes.Add(new BufferSubtractionSettings("missing-reference", BufferSubtractionMethod.Linear).ToAttribute());
            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { target });

            package.Position = 0;
            await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadStream(package));

            package.Position = 0;
            var recovered = await FTXTCReader.ReadWithRecovery(package, FtxtcReadPolicy.RecoverUsableContent);
            var experiment = recovered.Containers.OfType<ExperimentData>().First();
            Assert.Contains(recovered.Issues, value => value.Code == "buffer-reference-unavailable");
            Assert.Equal(experiment.Injections.Select(value => value.RawPeakArea.Value),
                experiment.Injections.Select(value => value.PeakArea.Value));
        }

        [Fact]
        public async Task BaselineShapeMismatchFailsStrictAndClearsProcessedOutputDuringRecovery()
        {
            using var package = await CreatePackage();
            using var mismatch = RewriteAuthenticatedPackage(package, (path, bytes) =>
            {
                if (path != "experiments/000000/baseline.ftxb") return bytes;
                var values = FtxbCodec.DecodeFloat64(bytes, path);
                return FtxbCodec.EncodeFloat64(values.GetLength(0) - 1, values.GetLength(1),
                    (row, column) => values[row, column]);
            });

            await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadStream(mismatch));

            mismatch.Position = 0;
            var recovered = await FTXTCReader.ReadWithRecovery(mismatch, FtxtcReadPolicy.RecoverUsableContent);
            var experiment = recovered.Containers.OfType<ExperimentData>().First();
            Assert.False(experiment.Processor.BaselineCompleted);
            Assert.Null(experiment.BaseLineCorrectedDataPoints);
            Assert.Contains(recovered.Issues, value => value.Code == "processed-output-unavailable");
        }

        [Fact]
        public async Task TraceColumnCountsAreValidatedByPackageVersion()
        {
            using var current = await CreatePackage();
            using var invalidCurrent = RewriteAuthenticatedPackage(current, (path, bytes) =>
                path == "experiments/000000/thermogram.ftxb" ? ExpandLegacyTrace(bytes) : bytes);

            var currentError = await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadStream(invalidCurrent));
            Assert.Contains("1.2", currentError.InnerException?.Message, StringComparison.Ordinal);
            Assert.Contains("3 columns", currentError.InnerException?.Message, StringComparison.Ordinal);

            invalidCurrent.Position = 0;
            var recovered = await FTXTCReader.ReadWithRecovery(invalidCurrent, FtxtcReadPolicy.RecoverUsableContent);
            Assert.Contains(recovered.Issues, value => value.Code == "thermogram-unavailable");
            Assert.Contains(recovered.Containers.OfType<ExperimentData>(), value => value.DataPoints.Count == 0);

            current.Position = 0;
            using var legacy = ConvertToLegacy10(current);
            using var invalidLegacy = RewriteAuthenticatedPackage(legacy, (path, bytes) =>
            {
                if (path != "experiments/000000/thermogram.ftxb") return bytes;
                var values = FtxbCodec.DecodeFloat32(bytes, path);
                return FtxbCodec.EncodeFloat32(values.GetLength(0), 3,
                    (row, column) => values[row, column]);
            }, schemaMinor: 0);

            var legacyError = await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadStream(invalidLegacy));
            Assert.Contains("1.0", legacyError.InnerException?.Message, StringComparison.Ordinal);
            Assert.Contains("7 columns", legacyError.InnerException?.Message, StringComparison.Ordinal);

            current.Position = 0;
            using var invalidSchema11 = RewriteAuthenticatedPackage(current, (path, bytes) =>
            {
                if (path != "experiments/000000/thermogram.ftxb") return bytes;
                var values = FtxbCodec.DecodeFloat32(bytes, path);
                return FtxbCodec.EncodeFloat32(values.GetLength(0), 4,
                    (row, column) => column < 3 ? values[row, column] : 0);
            }, schemaMinor: 1);

            var schema11Error = await Assert.ThrowsAsync<InvalidDataException>(() => FTXTCReader.ReadStream(invalidSchema11));
            Assert.Contains("1.1", schema11Error.InnerException?.Message, StringComparison.Ordinal);
            Assert.Contains("3 or legacy 7 columns", schema11Error.InnerException?.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Schema11SevenColumnThermogramsLoadAndNormalizeToSchema12()
        {
            using var current = await CreatePackage();
            var expectedContainers = await FTXTCReader.ReadStream(current);
            var expected = expectedContainers.OfType<ExperimentData>()
                .ToDictionary(experiment => experiment.UniqueID, StringComparer.Ordinal);

            current.Position = 0;
            using var schema11 = RewriteAuthenticatedPackage(current, (path, bytes) =>
                path.EndsWith("/thermogram.ftxb", StringComparison.Ordinal)
                    ? ExpandLegacyTrace(bytes)
                    : bytes, schemaMinor: 1);

            var restoredContainers = await FTXTCReader.ReadStream(schema11);
            var restored = restoredContainers.OfType<ExperimentData>()
                .ToDictionary(experiment => experiment.UniqueID, StringComparer.Ordinal);
            Assert.Equal(expected.Keys.OrderBy(value => value), restored.Keys.OrderBy(value => value));
            foreach (var id in expected.Keys)
            {
                Assert.Equal(expected[id].DataPoints.Count, restored[id].DataPoints.Count);
                for (var index = 0; index < expected[id].DataPoints.Count; index++)
                {
                    Assert.Equal(expected[id].DataPoints[index].Time, restored[id].DataPoints[index].Time);
                    Assert.Equal(expected[id].DataPoints[index].Power, restored[id].DataPoints[index].Power);
                    Assert.Equal(expected[id].DataPoints[index].Temperature, restored[id].DataPoints[index].Temperature);
                }
            }

            schema11.Position = 0;
            var recovered = await FTXTCReader.ReadWithRecovery(schema11, FtxtcReadPolicy.RecoverUsableContent);
            Assert.DoesNotContain(recovered.Issues, issue => issue.Code == "thermogram-unavailable");
            foreach (var experiment in recovered.Containers.OfType<ExperimentData>())
                Assert.Equal(expected[experiment.UniqueID].DataPoints.Count, experiment.DataPoints.Count);

            using var normalized = new MemoryStream();
            await FTXTCWriter.WriteStream(normalized,
                restoredContainers.OfType<ExperimentData>(), restoredContainers.OfType<AnalysisResult>());
            normalized.Position = 0;
            using var archive = new ZipArchive(normalized, ZipArchiveMode.Read, leaveOpen: true);
            using (var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json").Open()))
                Assert.Equal(2, manifest.RootElement.GetProperty("schemaMinor").GetInt32());
            foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith("/thermogram.ftxb", StringComparison.Ordinal)))
            {
                using var trace = entry.Open();
                using var traceBytes = new MemoryStream();
                trace.CopyTo(traceBytes);
                Assert.Equal(3, FtxbCodec.DecodeFloat32(traceBytes.ToArray(), entry.FullName).GetLength(1));
            }
        }

        [Fact]
        public async Task Legacy10OrdinalsAndRedundantDerivedValuesRemainReadable()
        {
            var target = await LoadExperiment("one-set.ftitc");
            var reference = await LoadExperiment("one-set.ftitc");
            target.SetID("legacy-target");
            target.Name = "Legacy target";
            target.Model = null;
            target.Attributes.Clear();
            reference.SetID("legacy-reference");
            reference.Name = "Legacy reference";
            reference.Model = null;
            reference.Attributes.Clear();

            var buffer = ExperimentAttribute.FromKey(AttributeKey.Buffer);
            buffer.IntValue = (int)Buffer.Tris;
            var salt = ExperimentAttribute.FromKey(AttributeKey.Salt);
            salt.IntValue = (int)Salt.KCl;
            target.Attributes.Add(buffer);
            target.Attributes.Add(salt);
            target.Attributes.Add(ExperimentAttribute.Species(ExperimentSpeciesLocation.Syringe, "ligand"));
            target.Attributes.Add(new BufferSubtractionSettings(reference.UniqueID, BufferSubtractionMethod.MatchedInjection).ToAttribute());

            var subtraction = BufferSubtractionCalculator.BuildModel(reference, target.BufferSubtractionSettings);
            var expectedPeak = target.Injections[0].RawPeakArea;
            var expectedPoint = target.DataPoints[0];
            if (BufferSubtractionCalculator.TryGetReferenceHeat(target.Injections[0], subtraction, out var referenceHeat))
                expectedPeak -= referenceHeat;

            using var current = new MemoryStream();
            await FTXTCWriter.WriteStream(current, new[] { target, reference });
            current.Position = 0;
            using (var archive = new ZipArchive(current, ZipArchiveMode.Read, leaveOpen: true))
            using (var reader = new StreamReader(archive.GetEntry("experiments/000000/experiment.json").Open()))
            {
                var metadata = await reader.ReadToEndAsync();
                Assert.Contains("\"valueId\": \"tris\"", metadata, StringComparison.Ordinal);
                Assert.DoesNotContain("\"intValue\"", metadata, StringComparison.Ordinal);
            }
            using var legacy = ConvertToLegacy10(current);
            var restored = (await FTXTCReader.ReadStream(legacy)).OfType<ExperimentData>()
                .Single(value => value.UniqueID == target.UniqueID);

            Assert.Equal(expectedPoint.Time, restored.DataPoints[0].Time);
            Assert.Equal(expectedPoint.Power, restored.DataPoints[0].Power);
            Assert.Equal(expectedPoint.Temperature, restored.DataPoints[0].Temperature);
            Assert.Equal((int)Buffer.Tris, restored.Attributes.Single(value => value.Key == AttributeKey.Buffer).IntValue);
            Assert.Equal((int)Salt.KCl, restored.Attributes.Single(value => value.Key == AttributeKey.Salt).IntValue);
            Assert.Equal((int)ExperimentSpeciesLocation.Syringe,
                restored.Attributes.Single(value => value.Key == AttributeKey.Species).IntValue);
            Assert.Equal(expectedPeak.Value, restored.Injections[0].PeakArea.Value, 12);
            Assert.Equal(restored.DataPoints[0].Power - (float)restored.Processor.Interpolator.Baseline[0],
                restored.BaseLineCorrectedDataPoints[0].Power);
        }

        [Fact]
        public async Task Legacy10ValiditySnapshotAndMissingValidityFlagsRemainReadable()
        {
            using var current = await CreatePackage();
            using var legacy = ConvertToLegacy10(current);
            var restored = await FTXTCReader.ReadStream(legacy);
            var result = Assert.Single(restored.OfType<AnalysisResult>());

            Assert.NotNull(result.ValiditySnapshot);
            Assert.True(result.Solution.IsValid);
            Assert.All(result.Solution.Solutions, value => Assert.True(value.IsValid));
        }

        [Fact]
        public void PersistenceRegistryCoversEverySolutionModel()
        {
            var expected = Enum.GetValues<AnalysisModel>().OrderBy(value => value).ToArray();
            Assert.Equal(expected, FtxtcModelRegistry.SupportedModels.OrderBy(value => value));
            Assert.All(expected, model => Assert.False(string.IsNullOrWhiteSpace(FtxtcWireIds.Model(model))));
        }

        [Fact]
        public async Task FtitcOpenIsDetachedForNativeSaveAs()
        {
            FTITCFormat.CurrentAccessedAppDocumentPath = "previous.ftxtc";
            var restored = await FTITCReader.ReadPath(Fixture("one-set.ftitc"));

            Assert.NotEmpty(restored);
            Assert.Equal(string.Empty, FTITCFormat.CurrentAccessedAppDocumentPath);
        }

        [Fact]
        public async Task LegacyFtitcReaderAcceptsFourColumnsAndTestSerializerWritesThree()
        {
            var legacyText = await File.ReadAllTextAsync(Fixture("one-set.ftitc"));
            Assert.Equal(4, DataPointRows(legacyText).First().Split(',').Length);

            using var source = new MemoryStream(Encoding.UTF8.GetBytes(legacyText));
            var experiments = (await FTITCReader.ReadStream(source)).OfType<ExperimentData>().ToList();
            var expected = experiments[0].DataPoints[0];

            using var serialized = new MemoryStream();
            await FTITCWriter.WriteStream(serialized, experiments);
            var serializedText = Encoding.UTF8.GetString(serialized.ToArray());
            Assert.All(DataPointRows(serializedText), row => Assert.Equal(3, row.Split(',').Length));

            serialized.Position = 0;
            var restored = (await FTITCReader.ReadStream(serialized)).OfType<ExperimentData>().First();
            Assert.Equal(experiments[0].DataPoints.Count, restored.DataPoints.Count);
            Assert.Equal(expected.Time, restored.DataPoints[0].Time);
            Assert.Equal(expected.Power, restored.DataPoints[0].Power);
            Assert.Equal(expected.Temperature, restored.DataPoints[0].Temperature);
        }

        static async Task<MemoryStream> CreatePackage()
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            foreach (var result in containers.OfType<AnalysisResult>())
                result.SetValiditySnapshot(AnalysisResultValiditySnapshot.Capture(result.Solution));
            var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, containers.OfType<ExperimentData>(), containers.OfType<AnalysisResult>());
            package.Position = 0;
            return package;
        }

        static async Task<AnalysisResult> PrepareAdvancedResult(bool includeBootstrapReplicates = true)
        {
            using var source = File.OpenRead(Fixture("one-set.ftitc"));
            var containers = await FTITCReader.ReadStream(source);
            var sourceResult = Assert.Single(containers.OfType<AnalysisResult>());
            var buffers = new[] { Buffer.Hepes, Buffer.Tris, Buffer.SodiumPhosphate };

            for (var index = 0; index < sourceResult.Solution.Solutions.Count; index++)
            {
                var data = sourceResult.Solution.Solutions[index].Data;
                data.MeasuredTemperature = 20 + index * 5;
                data.Attributes.RemoveAll(attribute => attribute.Key == AttributeKey.Salt || attribute.Key == AttributeKey.Buffer);

                var salt = ExperimentAttribute.FromKey(AttributeKey.Salt);
                salt.IntValue = (int)Salt.NaCl;
                salt.ParameterValue = new FloatWithError(0.05 + index * 0.05);
                data.Attributes.Add(salt);

                var buffer = ExperimentAttribute.FromKey(AttributeKey.Buffer);
                buffer.IntValue = (int)buffers[index % buffers.Length];
                buffer.DoubleValue = 7.4;
                buffer.ParameterValue = new FloatWithError(0.05);
                data.Attributes.Add(buffer);
            }

            var members = sourceResult.Solution.Solutions;
            if (!includeBootstrapReplicates)
                foreach (var member in members)
                    member.SetBootstrapSolutions(new List<SolutionInterface>());
            var globalModel = new GlobalModel(members.Select(solution => solution.Model).ToList())
            {
                Parameters = sourceResult.Model.Parameters,
                ModelCloneOptions = sourceResult.Model.ModelCloneOptions,
            };
            var globalSolver = new GlobalSolver
            {
                Model = globalModel,
                ErrorEstimationMethod = sourceResult.Solution.ErrorEstimationMethod,
                UseErrorWeightedFitting = sourceResult.Solution.UseWeightedFitting,
            };
            var globalSolution = new GlobalSolution(globalSolver, members, sourceResult.Solution.Convergence);
            globalModel.Solution = globalSolution;
            var result = new AnalysisResult(globalSolution);
            Assert.NotNull(result.SpolarRecordAnalysis);
            Assert.NotNull(result.ElectrostaticsAnalysis);
            Assert.NotNull(result.ProtonationAnalysis);
            result.MarkClean();
            return result;
        }

        static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
        {
            var position = percentile * (sortedValues.Count - 1);
            var lowerIndex = (int)Math.Floor(position);
            var upperIndex = (int)Math.Ceiling(position);
            var weight = position - lowerIndex;
            return sortedValues[lowerIndex] * (1 - weight) + sortedValues[upperIndex] * weight;
        }

        static async Task<ExperimentData> LoadExperiment(string fixture)
        {
            using var source = File.OpenRead(Fixture(fixture));
            return (await FTITCReader.ReadStream(source)).OfType<ExperimentData>().First();
        }

        static MemoryStream RewritePackage(Stream source, Func<string, byte[], byte[]> transform)
        {
            var items = new List<(string path, byte[] bytes)>();
            source.Position = 0;
            using (var input = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            {
                foreach (var entry in input.Entries)
                {
                    using var entryStream = entry.Open();
                    using var copy = new MemoryStream();
                    entryStream.CopyTo(copy);
                    items.Add((entry.FullName, transform(entry.FullName, copy.ToArray())));
                }
            }
            var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var item in items)
                {
                    var entry = archive.CreateEntry(item.path);
                    using var destination = entry.Open();
                    destination.Write(item.bytes, 0, item.bytes.Length);
                }
            }
            output.Position = 0;
            return output;
        }

        static MemoryStream RewriteAuthenticatedPackage(Stream source, Func<string, byte[], byte[]> transform, int schemaMinor = 2)
        {
            var items = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            source.Position = 0;
            using (var input = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            {
                foreach (var entry in input.Entries)
                {
                    using var entryStream = entry.Open();
                    using var copy = new MemoryStream();
                    entryStream.CopyTo(copy);
                    if (entry.FullName != FTXTCFormat.ManifestPath)
                        items.Add(entry.FullName, transform(entry.FullName, copy.ToArray()));
                }
            }

            return WriteAuthenticatedPackage(items, schemaMinor);
        }

        static MemoryStream ConvertToLegacy10(Stream source)
        {
            var items = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            source.Position = 0;
            using (var input = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true))
            {
                foreach (var entry in input.Entries.Where(value => value.FullName != FTXTCFormat.ManifestPath))
                {
                    using var entryStream = entry.Open();
                    using var copy = new MemoryStream();
                    entryStream.CopyTo(copy);
                    items.Add(entry.FullName, copy.ToArray());
                }
            }

            var project = JsonNode.Parse(items[FTXTCFormat.ProjectPath]).AsObject();
            project["projectSchemaVersion"] = 1;
            foreach (var experiment in project["experiments"].AsArray().Select(value => value.AsObject()))
            {
                var thermogramPath = experiment["thermogram"].GetValue<string>();
                var prefix = thermogramPath.Substring(0, thermogramPath.LastIndexOf("/", StringComparison.Ordinal));
                var correctedPath = prefix + "/corrected-trace.ftxb";
                experiment["correctedTrace"] = correctedPath;
                items[thermogramPath] = ExpandLegacyTrace(items[thermogramPath]);
                // Deliberately use the raw trace as the legacy corrected trace. A
                // compatible reader must ignore it and derive from the baseline.
                items[correctedPath] = items[thermogramPath].ToArray();
            }
            items[FTXTCFormat.ProjectPath] = Encoding.UTF8.GetBytes(project.ToJsonString(FTXTCFormat.JsonOptions));

            foreach (var path in items.Keys.Where(value => value.EndsWith(".json", StringComparison.Ordinal)
                && value != FTXTCFormat.ProjectPath).ToList())
            {
                var root = JsonNode.Parse(items[path]);
                if (path.EndsWith("/result.json", StringComparison.Ordinal) && root["validity"] != null)
                {
                    var validity = root["validity"].Deserialize<FtxtcValidityState>(FTXTCFormat.JsonOptions);
                    root["validity"] = JsonSerializer.SerializeToNode(validity.Restore(), FTXTCFormat.JsonOptions);
                    root.AsObject().Remove("isValid");
                }
                else if (path.EndsWith("/solution.json", StringComparison.Ordinal))
                {
                    root.AsObject().Remove("isValid");
                }
                ConvertAttributesToLegacyOrdinals(root);
                if (path.EndsWith("/experiment.json", StringComparison.Ordinal))
                {
                    foreach (var injection in root["injections"].AsArray().Select(value => value.AsObject()))
                        injection["correctedPeakArea"] = injection["rawPeakArea"]?.DeepClone();
                }
                items[path] = Encoding.UTF8.GetBytes(root.ToJsonString(FTXTCFormat.JsonOptions));
            }

            return WriteAuthenticatedPackage(items, schemaMinor: 0);
        }

        static byte[] ExpandLegacyTrace(byte[] bytes)
        {
            var values = FtxbCodec.DecodeFloat32(bytes, "trace.ftxb");
            if (values.GetLength(1) != 3)
                throw new InvalidDataException("Only current three-column traces can be expanded for a legacy fixture.");
            return FtxbCodec.EncodeFloat32(values.GetLength(0), 7, (row, column) =>
                column < 3 ? values[row, column] : 1000f + row + column);
        }

        static void ConvertAttributesToLegacyOrdinals(JsonNode node)
        {
            if (node is JsonObject value)
            {
                if (value["key"] is JsonValue keyNode && value["valueId"] is JsonValue valueIdNode)
                {
                    var key = FtxtcWireIds.Attribute(keyNode.GetValue<string>());
                    value["intValue"] = FtxtcWireIds.AttributeIntValue(key, valueIdNode.GetValue<string>(), null);
                    value.Remove("valueId");
                }
                foreach (var child in value.ToList()) ConvertAttributesToLegacyOrdinals(child.Value);
            }
            else if (node is JsonArray array)
            {
                foreach (var child in array) ConvertAttributesToLegacyOrdinals(child);
            }
        }

        static MemoryStream WriteAuthenticatedPackage(Dictionary<string, byte[]> items, int schemaMinor)
        {
            var manifest = new FtxtcManifest { WriterVersion = "test", SchemaMinor = schemaMinor };
            manifest.Entries = items.OrderBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => new FtxtcManifestEntry
                {
                    Path = value.Key,
                    MediaType = value.Key.EndsWith(".json", StringComparison.Ordinal) ? "application/json" : "application/x-ftxb",
                    Length = value.Value.LongLength,
                    Sha256 = FTXTCFormat.Sha256(value.Value),
                }).ToList();

            var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifestEntry = archive.CreateEntry(FTXTCFormat.ManifestPath);
                using (var destination = manifestEntry.Open())
                {
                    var bytes = FTXTCFormat.JsonBytes(manifest);
                    destination.Write(bytes, 0, bytes.Length);
                }
                foreach (var item in items.OrderBy(value => value.Key, StringComparer.Ordinal))
                {
                    var entry = archive.CreateEntry(item.Key);
                    using var destination = entry.Open();
                    destination.Write(item.Value, 0, item.Value.Length);
                }
            }
            output.Position = 0;
            return output;
        }

        static string[] PayloadHashes(Stream source)
        {
            source.Position = 0;
            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            return archive.Entries.Where(entry => entry.FullName != "manifest.json")
                .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
                .Select(entry =>
                {
                    using var stream = entry.Open();
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    return entry.FullName + ":" + Convert.ToHexString(sha.ComputeHash(stream));
                }).ToArray();
        }

        static IEnumerable<string> DataPointRows(string text)
        {
            var inDataPoints = false;
            foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                if (line == "LIST:DataPointList")
                {
                    inDataPoints = true;
                    continue;
                }
                if (!inDataPoints) continue;
                if (line == "ENDLIST")
                {
                    inDataPoints = false;
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(line)) yield return line;
            }
        }

        sealed class FailingAdvancedAnalysis : AdvancedAnalysis
        {
            internal int Value { get; private set; }
            internal bool FailNextRun { get; set; }

            internal FailingAdvancedAnalysis(AnalysisResult result) : base(result)
            {
            }

            protected override void Calculate()
            {
                Value++;
                CompletedIterations = Value;
                if (FailNextRun) throw new InvalidOperationException("Expected advanced-analysis test failure.");
            }

            protected override object CaptureCommittedState() => Value;

            protected override void RestoreCommittedState(object state) => Value = (int)state;
        }

        static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
    }
}
