using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class ResidualBootstrapTests
    {
        [Fact]
        public void BootstrapDistributionKeepsPrimaryValueAndUsesFixedReferenceRmsDeviation()
        {
            var result = new FloatWithError(new[] { 0.0, 2.0, 10.0 }, 100.0);

            Assert.Equal(100.0, result.Value);
            Assert.Equal(Math.Sqrt(27704.0 / 3.0), result.SD, 12);
            Assert.Equal(0.0, result.Lower);
            Assert.Equal(10.0, result.Upper);
        }

        [Fact]
        public void ResidualBootstrapCentersStandardizedResidualsAndPreservesTargetSd()
        {
            var data = CreateProbeExperiment(out var model, out var predictions);
            var options = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals };
            var random = new Random(173);
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            model.Solution.UseWeightedFitting = true;

            var clone = data.GetSynthClone(options, random);
            var included = data.Injections.Where(injection => injection.Include).ToList();
            var sigmas = included.Select(injection => Model.GetSigmaForWeighting(injection, included)).ToArray();
            var standardized = included
                .Select((injection, index) => (injection.PeakArea.Value - predictions[injection.ID]) / sigmas[index])
                .ToArray();
            var centred = standardized.Select(value => value - standardized.Average()).ToArray();
            var expectedRandom = new Random(173);

            foreach (var injection in clone.Injections)
            {
                var source = data.Injections.Single(original => original.ID == injection.ID);
                Assert.NotSame(source, injection);
                Assert.Same(clone, injection.Experiment);
                Assert.Equal(source.Include, injection.Include);
                Assert.Equal(source.Volume, injection.Volume);
                Assert.Equal(source.ActualCellConcentration, injection.ActualCellConcentration);
                Assert.Equal(source.ActualTitrantConcentration, injection.ActualTitrantConcentration);
                Assert.Equal(source.Ratio, injection.Ratio);

                if (!source.Include)
                {
                    Assert.Equal(source.PeakArea.Value, injection.PeakArea.Value);
                    Assert.Equal(source.PeakArea.SD, injection.PeakArea.SD);
                    continue;
                }

                var draw = centred[expectedRandom.Next(centred.Length)];
                var expectedArea = predictions[source.ID] + sigmas[source.ID - 1] * draw;
                Assert.Equal(expectedArea, injection.PeakArea.Value, 12);
                Assert.Equal(source.PeakArea.SD, injection.PeakArea.SD);
            }

            Assert.Equal(77.0, clone.Injections[0].PeakArea.Value);
            Assert.Equal(9.0, clone.Injections[0].PeakArea.SD);
        }

        [Fact]
        public void UnweightedResidualBootstrapUsesRawResidualsAndIgnoresInjectionSd()
        {
            var first = CreateProbeExperiment(out var firstModel, out var predictions);
            var second = CreateProbeExperiment(out var secondModel, out _);
            first.Injections[1].SetPeakArea(new FloatWithError(11, 100));
            first.Injections[2].SetPeakArea(new FloatWithError(12, 0));
            first.Injections[3].SetPeakArea(new FloatWithError(13, double.NaN));
            second.Injections[1].SetPeakArea(new FloatWithError(11, double.NaN));
            second.Injections[2].SetPeakArea(new FloatWithError(12, 1e-200));
            second.Injections[3].SetPeakArea(new FloatWithError(13, double.PositiveInfinity));
            firstModel.Solution = SolutionInterface.FromModel(firstModel,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            secondModel.Solution = SolutionInterface.FromModel(secondModel,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));

            var firstClone = first.GetSynthClone(
                new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
                new Random(173));
            var secondClone = second.GetSynthClone(
                new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
                new Random(173));
            var rawResiduals = new[] { 11.0 - predictions[1], 12.0 - predictions[2], 13.0 - predictions[3] };
            var centre = rawResiduals.Average();
            var centred = rawResiduals.Select(value => value - centre).ToArray();
            var expectedRandom = new Random(173);

            foreach (var id in Enumerable.Range(1, 3))
            {
                var expected = predictions[id] + centred[expectedRandom.Next(centred.Length)];
                Assert.Equal(expected, firstClone.Injections[id].PeakArea.Value, 12);
                Assert.Equal(expected, secondClone.Injections[id].PeakArea.Value, 12);
            }
            Assert.Equal(new[] { 100.0, 0.0, double.NaN },
                firstClone.Injections.Skip(1).Select(injection => injection.PeakArea.SD).ToArray());
        }

        [Fact]
        public void ResidualBootstrapRequiresPrimarySolutionAndIncludedData()
        {
            var data = CreateProbeExperiment(out var model, out _);
            var options = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals };

            Assert.Throws<InvalidOperationException>(() => data.GetSynthClone(options, new Random(1)));

            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            foreach (var injection in data.Injections) injection.Include = false;

            Assert.Throws<InvalidOperationException>(() => data.GetSynthClone(options, new Random(1)));
        }

        [Fact]
        public void ResidualBootstrapRequiresMoreIncludedInjectionsThanFittedParameters()
        {
            var data = CreateProbeExperiment(out var model, out _);
            ConfigureFittedProbe(model);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000);
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));

            Assert.Throws<InvalidOperationException>(() => data.GetSynthClone(
                new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
                new Random(1)));
        }

        [Fact]
        public void SingleExperimentBootstrapChecksIncludedInjectionsBeforeRefits()
        {
            // The included heat is 11 with a prediction of 10 plus the fitted offset.
            // One injection identifies the offset, leaving no degrees of freedom for bootstrap.
            CreateProbeExperiment(out var model, out _, includedInjectionCount: 1);
            ConfigureFittedProbe(model);
            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
                ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                BootstrapIterations = 10,
                MaxOptimizerIterations = 300,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success, convergence.Message);
            Assert.Equal(1, model.Solution.Parameters[ParameterType.Offset].Value, 6);
            Assert.Equal(ErrorEstimationOutcome.CompleteFailure, convergence.ErrorEstimationOutcome);
            Assert.Equal(10, convergence.RequestedBootstrapIterations);
            Assert.Null(convergence.ErrorEstimationAttemptedRefits);
            Assert.Empty(model.Solution.BootstrapSolutions);
            var rows = UncertaintyEstimationPresentation.Section(GlobalSolution.FromSingleExperimentSolver(solver)).Rows;
            Assert.Equal("Unavailable", Assert.Single(rows, row => row.Label == "Status").Value);
            var reason = Assert.Single(rows, row => row.Label == "Reason").Value;
            Assert.Contains("Included injections (1)", reason);
            Assert.Contains("effective fitted parameter count (1)", reason);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ResidualBootstrapScalesCentredResidualsByFittedDegreesOfFreedom(bool weighted)
        {
            var data = CreateProbeExperiment(out var model, out _, includedInjectionCount: 5);
            ConfigureFittedProbe(model);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000);
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            model.Solution.UseWeightedFitting = weighted;

            // n = 5 included injections; p = 2 fitted (offset and enthalpy). The
            // locked N and affinity do not absorb residual degrees of freedom.
            AssertSyntheticSampleMatchesPrimaryFit(data, model, weighted, seed: 29, residualScale: Math.Sqrt(5.0 / 3.0));
        }

        [Theory]
        [InlineData(true, 24.0 / 13.0, 40.0 / 27.0)]
        [InlineData(false, 3.0 / 2.0, 5.0 / 4.0)]
        public void GlobalResidualBootstrapUsesEachMembersCountsAndShareOfSharedParameters(
            bool shared, double firstVarianceRatio, double secondVarianceRatio)
        {
            var first = CreateProbeExperiment(out var firstModel, out _);
            var second = CreateProbeExperiment(out var secondModel, out _, includedInjectionCount: 5);
            ConfigureFittedProbe(firstModel);
            ConfigureFittedProbe(secondModel);
            CreateSharedEnthalpyJoint(firstModel, secondModel, shared);

            // Shared: 3 + 5 = 8 included injections, one local offset per member and one
            // shared enthalpy. Each member is charged its offset plus the enthalpy in
            // proportion to its injections: p = 1 + 3/8 gives 3 / (3 - 11/8) = 24/13 and
            // p = 1 + 5/8 gives 5 / (5 - 13/8) = 40/27. Individual: each member keeps its
            // own n and p = 1.
            AssertSyntheticSampleMatchesPrimaryFit(first, firstModel, weighted: false, seed: 31, residualScale: Math.Sqrt(firstVarianceRatio));
            AssertSyntheticSampleMatchesPrimaryFit(second, secondModel, weighted: false, seed: 37, residualScale: Math.Sqrt(secondVarianceRatio));
        }

        [Fact]
        public void PooledBootstrapScalesEachExperimentByItsOwnCountsAndShareOfSharedParameters()
        {
            // A: centred residuals {-1, 0, 1}, RMS sqrt(2/3), unit values {-sqrt(1.5), 0, sqrt(1.5)}.
            // B: centred residuals {-2, -1, 0, 1, 2}, RMS sqrt(2), unit values
            // {-sqrt(2), -1/sqrt(2), 0, 1/sqrt(2), sqrt(2)}. With a local offset each and one
            // shared enthalpy, A is corrected by 24/13 and B by 40/27 in variance, so the
            // target scales are sqrt(2/3 * 24/13) = 4/sqrt(13) and sqrt(2 * 40/27).
            var a = CreatePooledMember("A", new[] { -1.0, 0.0, 1.0 }, new[] { 1.0, 1.0, 1.0 });
            var b = CreatePooledMember("B", new[] { -2.0, -1.0, 0.0, 1.0, 2.0 }, new[] { 1.0, 1.0, 1.0, 1.0, 1.0 });
            var model = CreateSharedEnthalpyJoint(a, b);
            var pool = PooledBootstrapResiduals.Prepare(model, weighted: false);
            var clone = model.GenerateSyntheticModel(new PoolDraws(8, 2, 3, 0, 7, 1, 4, 5, 6), model.ModelCloneOptions, pool);

            var expected = new[]
            {
                10 + Math.Sqrt(24.0 / 13), 20 - Math.Sqrt(32.0 / 13), 30 - Math.Sqrt(24.0 / 13),
                10 + Math.Sqrt(160.0 / 27), 20, 30 - Math.Sqrt(40.0 / 27), 40, 50 + Math.Sqrt(40.0 / 27),
            };
            var heats = clone.Models.SelectMany(member => member.Data.Injections.Where(injection => injection.Include)
                .Select(injection => injection.PeakArea.Value)).ToArray();
            for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], heats[i], 12);
        }

        [Fact]
        public void ResidualBootstrapRequiresMoreInjectionsThanFittedParametersInEachJointMember()
        {
            // 1 + 5 = 6 included injections exceed the 3 fitted values (two local offsets and a
            // shared enthalpy), but the one-injection member is charged 1 + 1/6 values.
            var single = CreatePooledMember("Single", new[] { 1.0 }, new[] { 1.0 });
            var other = CreatePooledMember("Other", new[] { -2.0, -1.0, 0.0, 1.0, 2.0 }, new[] { 1.0, 1.0, 1.0, 1.0, 1.0 });
            var model = CreateSharedEnthalpyJoint(single, other);

            var pooled = Assert.Throws<InvalidOperationException>(() => PooledBootstrapResiduals.Prepare(model, weighted: false));
            Assert.Contains("more included injections than fitted parameters", pooled.Message);
            var local = Assert.Throws<InvalidOperationException>(() => single.Data.GetSynthClone(
                new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
                new Random(1)));
            Assert.Contains("more included injections than fitted parameters", local.Message);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ResidualBootstrapReportsAnIneligibleJointMemberBeforeRefits(bool pooled)
        {
            var single = CreatePooledMember("Single", new[] { 1.0 }, new[] { 1.0 });
            single.Data.Name = "One included injection";
            var other = CreatePooledMember("Other", new[] { -2.0, -1.0, 0.0, 1.0, 2.0 }, new[] { 1.0, 1.0, 1.0, 1.0, 1.0 });
            var model = CreateSharedEnthalpyJoint(single, other);
            model.ModelCloneOptions.PoolBootstrapResiduals = pooled;
            var solution = model.Solution;
            var solver = CreatePooledSolver(model, weighted: false);
            solver.ApplyErrorEstimationPolicy();
            var convergence = solution.Convergence;
            convergence.CaptureBootstrapRequest(ErrorEstimationMethod.BootstrapResiduals, solver.BootstrapIterations);
            solver.EstimateUncertainty(convergence);

            Assert.Same(solution, model.Solution);
            Assert.True(convergence.Success);
            Assert.Equal(ErrorEstimationOutcome.CompleteFailure, convergence.ErrorEstimationOutcome);
            Assert.Equal(solver.BootstrapIterations, convergence.RequestedBootstrapIterations);
            Assert.Null(convergence.ErrorEstimationAttemptedRefits);
            Assert.Empty(solution.BootstrapSolutions);
            Assert.Contains("'One included injection'", convergence.ErrorEstimationSummary);
            Assert.Contains("Included injections (1)", convergence.ErrorEstimationSummary);
            var rows = UncertaintyEstimationPresentation.Section(solution).Rows;
            Assert.Equal("Unavailable", Assert.Single(rows, row => row.Label == "Status").Value);
            Assert.Equal(convergence.ErrorEstimationSummary, Assert.Single(rows, row => row.Label == "Reason").Value);
        }

        [Fact]
        public void ResidualBootstrapSpreadMatchesAnalyticStandardErrorForOffsetModel()
        {
            // The offset model predicts theta * m_i, so ordinary least squares has
            // closed forms: theta = sum(y m) / sum(m^2) and SE = s / sqrt(sum(m^2))
            // with s^2 = RSS / (n - 1). Each synthetic dataset is refitted with the
            // same closed form, so the expectation is independent of the solver.
            var data = CreateProbeExperiment(out _, out _, includedInjectionCount: 5);
            var included = data.Injections.Where(injection => injection.Include).ToList();
            var sumMassSquared = included.Sum(injection => injection.InjectionMass * injection.InjectionMass);
            double Estimate(IEnumerable<InjectionData> injections) => injections
                .Where(injection => injection.Include)
                .Sum(injection => injection.PeakArea.Value * injection.InjectionMass) / sumMassSquared;
            var estimate = Estimate(data.Injections);
            var rss = included.Sum(injection => Math.Pow(injection.PeakArea.Value - estimate * injection.InjectionMass, 2));
            var standardError = Math.Sqrt(rss / (included.Count - 1) / sumMassSquared);

            var model = new Offset(data);
            model.InitializeParameters(data);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, estimate);
            data.Model = model;
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromFixedFit(0, 0));

            var options = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals };
            var random = new Random(2027);
            const int iterations = 10000;
            var squaredDeviation = 0.0;
            for (var iteration = 0; iteration < iterations; iteration++)
                squaredDeviation += Math.Pow(Estimate(data.GetSynthClone(options, random).Injections) - estimate, 2);
            var bootstrapSd = Math.Sqrt(squaredDeviation / iterations);

            // Monte Carlo relative precision is about 1 / sqrt(2B) = 0.7%. Without the
            // degrees-of-freedom scaling the expected ratio would be sqrt(4/5) = 0.894.
            Assert.InRange(bootstrapSd / standardError, 0.97, 1.03);
        }

        [Fact]
        public void ResidualBootstrapUsesWeightingFallbackWithoutReplacingDeclaredSd()
        {
            var data = CreateProbeExperiment(out var model, out var predictions);
            data.Injections[1].SetPeakArea(new FloatWithError(12, 2));
            data.Injections[2].SetPeakArea(new FloatWithError(24, 0));
            data.Injections[3].SetPeakArea(new FloatWithError(26, double.NaN));
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));
            model.Solution.UseWeightedFitting = true;

            var options = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals };
            var clone = data.GetSynthClone(options, new Random(29));
            var included = data.Injections.Where(injection => injection.Include).ToList();

            Assert.All(included, injection =>
                Assert.Equal(2, Model.GetSigmaForWeighting(injection, included), 12));
            Assert.All(clone.Injections.Where(injection => injection.Include), injection =>
                Assert.True(double.IsFinite(injection.PeakArea.Value)));
            Assert.Equal(2, clone.Injections[1].PeakArea.SD);
            Assert.Equal(0, clone.Injections[2].PeakArea.SD);
            Assert.True(double.IsNaN(clone.Injections[3].PeakArea.SD));
            Assert.Equal(predictions.Keys.OrderBy(id => id), included.Select(injection => injection.ID));
        }

        [Fact]
        public void SingleIncludedResidualCentersToThePrimaryPrediction()
        {
            var data = CreateProbeExperiment(out var model, out var predictions);
            foreach (var injection in data.Injections)
                injection.Include = injection.ID == 2;
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));

            var clone = data.GetSynthClone(
                new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
                new Random(41));

            Assert.Equal(predictions[2], clone.Injections[2].PeakArea.Value, 12);
            Assert.Equal(data.Injections[2].PeakArea.SD, clone.Injections[2].PeakArea.SD);
        }

        [Fact]
        public void NoneCloneDoesNotRequireSolutionAndOwnsItsInjectionCopies()
        {
            var data = CreateProbeExperiment(out _, out _);

            var clone = data.GetSynthClone(
                new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
                new Random(7));

            Assert.Equal(data.Injections.Count, clone.Injections.Count);
            for (var index = 0; index < data.Injections.Count; index++)
            {
                Assert.NotSame(data.Injections[index], clone.Injections[index]);
                Assert.Same(clone, clone.Injections[index].Experiment);
                Assert.Equal(data.Injections[index].PeakArea.Value, clone.Injections[index].PeakArea.Value);
                Assert.Equal(data.Injections[index].PeakArea.SD, clone.Injections[index].PeakArea.SD);
            }
        }

        [Fact]
        public void BootstrapRandomStreamsAreDistinct()
        {
            var streams = BootstrapRandomStreams.Create(8);

            Assert.Equal(8, streams.Length);
            Assert.Equal(8, streams.Distinct().Count());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SingleSolverBootstrapRetainsPrimaryValueWeightingAndReplicateOrder(bool weighted)
        {
            var data = CreateProbeExperiment(out var model, out _);
            ConfigureFittedProbe(model);
            model.ModelCloneOptions = new ModelCloneOptions
            {
                ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
            };
            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
                ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                BootstrapIterations = 8,
                MaxOptimizerIterations = 300,
                UseErrorWeightedFitting = weighted,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success);
            Assert.NotEmpty(model.Solution.BootstrapSolutions);
            Assert.Equal(
                model.Parameters.Table[ParameterType.Offset].Value,
                model.Solution.Parameters[ParameterType.Offset].Value);
            Assert.Equal(weighted, model.Solution.UseWeightedFitting);
            Assert.All(model.Solution.BootstrapSolutions, solution => Assert.Equal(weighted, solution.UseWeightedFitting));
            // n = 3 included injections; p = 1 fitted offset.
            AssertSyntheticSampleMatchesPrimaryFit(data, model, weighted, seed: 19, residualScale: Math.Sqrt(3.0 / 2.0));
            Assert.Equal(
                model.Solution.BootstrapSolutions.Select(solution => solution.BootstrapReplicateIndex).OrderBy(index => index),
                model.Solution.BootstrapSolutions.Select(solution => solution.BootstrapReplicateIndex));
        }

        [Fact]
        public void LeaveOneOutRunsOncePerIncludedInjectionInOmissionOrder()
        {
            var data = CreateProbeExperiment(out var model, out _, includedInjectionCount: 20);
            ConfigureFittedProbe(model);
            model.ModelCloneOptions = new ModelCloneOptions
            {
                ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                IncludeConcentrationErrorsInBootstrap = true,
                EnableAutoConcentrationVariance = true,
                AutoConcentrationVariance = 0.05,
                UnlockBootstrapParameters = true,
            };

            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
                ErrorEstimationMethod = ErrorEstimationMethod.LeaveOneOut,
                BootstrapIterations = 100,
                MaxOptimizerIterations = 300,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success, convergence.Message);
            Assert.NotEqual(ErrorEstimationOutcome.NotRun, convergence.ErrorEstimationOutcome);
            Assert.Equal(20, model.Solution.BootstrapSolutions.Count);
            Assert.Equal(
                Enumerable.Range(1, 20),
                model.Solution.BootstrapSolutions
                    .Select(solution => solution.Data.Injections
                        .Single(injection => injection.ID > 0 && !injection.Include).ID));
            Assert.False(model.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap);
            Assert.False(model.ModelCloneOptions.EnableAutoConcentrationVariance);
            Assert.Equal(0, model.ModelCloneOptions.AutoConcentrationVariance);
            Assert.False(model.ModelCloneOptions.UnlockBootstrapParameters);
            Assert.False(model.ModelCloneOptions.EffectiveIncludeConcentrationErrors);
            Assert.False(model.ModelCloneOptions.EffectiveUnlockBootstrapParameters);
        }

        [Fact]
        public void LeaveOneOutSyntheticModelsPreserveLocksAndUseCentralModelOptions()
        {
            CreateProbeExperiment(out var model, out _);
            ConfigureFittedProbe(model);
            model.ModelCloneOptions = new ModelCloneOptions
            {
                ErrorEstimationMethod = ErrorEstimationMethod.LeaveOneOut,
                DiscardedDataPoint = 2,
                UnlockBootstrapParameters = true,
            };
            var option = ExperimentAttribute.Parameter(
                AttributeKey.PreboundLigandAffinity,
                "uncertain option",
                new FloatWithError(4.5, 1.2));
            model.ModelOptions.Add(option.Key, option);

            var first = model.GenerateSyntheticModel(new Random(1));
            var second = model.GenerateSyntheticModel(new Random(999));

            Assert.All(first.Parameters.Table.Values.Where(parameter =>
                    parameter.Key != ParameterType.Offset),
                parameter => Assert.True(parameter.IsLocked));
            Assert.Equal(4.5, first.ModelOptions[option.Key].ParameterValue.Value, 12);
            Assert.Equal(0, first.ModelOptions[option.Key].ParameterValue.SD);
            Assert.Equal(
                first.ModelOptions[option.Key].ParameterValue.Value,
                second.ModelOptions[option.Key].ParameterValue.Value);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BootstrapRefitUsesPrimaryOptimizerSettings(bool joint)
        {
            const double originalOffset = 10000;
            const int primaryBudget = 60;
            const double toleranceModifier = 0.01;
            var a = CreateMember("A");
            var b = joint ? CreateMember("B") : null;
            var primary = CreateSolver(a, b);
            primary.ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals;
            primary.BootstrapIterations = 1;

            var convergence = primary.Solve();
            Assert.True(convergence.Success);
            Assert.Equal(originalOffset, a.Solution.Parameters[ParameterType.Offset].Value, 5);
            var retained = Assert.Single(a.Solution.BootstrapSolutions);

            // Every synthetic injection gets the same positive residual. The
            // resulting least-squares offset is the original offset plus that draw.
            double shift = 1000 * (joint ? Math.Sqrt(6.0 / 5) : Math.Sqrt(3.0 / 2));
            var reference = CreateSolver(CreateMember("Reference A", shift),
                joint ? CreateMember("Reference B", shift) : null);
            var referenceConvergence = reference.Solve();
            Assert.True(referenceConvergence.Success);
            Assert.InRange(retained.Parameters[ParameterType.Offset].Value, originalOffset + shift - 0.05,
                originalOffset + shift + 0.05);
            Assert.Equal(referenceConvergence.Iterations, retained.Convergence.Iterations);
            Assert.InRange(retained.Convergence.Iterations, primaryBudget / 3 + 1, primaryBudget);

            ProbeModel CreateMember(string name, double? constantShift = null)
            {
                var residuals = constantShift.HasValue
                    ? Enumerable.Repeat(originalOffset + constantShift.Value, 3).ToArray()
                    : new[] { originalOffset - 1000, originalOffset, originalOffset + 1000 };
                var member = CreatePooledMember(name, residuals, new[] { 1.0, 1.0, 1.0 });
                member.Parameters.AddOrUpdateParameter(ParameterType.Offset, originalOffset);
                member.FixedBootstrapDraw = 2;
                return member;
            }

            SolverInterface CreateSolver(ProbeModel first, ProbeModel second)
            {
                SolverInterface solver;
                if (second == null)
                    solver = new Solver { Model = first };
                else
                {
                    var global = CreateJointProbe(first, second);
                    global.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, originalOffset);
                    global.Parameters.SetIndividualFromGlobal();
                    global.ModelCloneOptions.PoolBootstrapResiduals = false;
                    solver = new GlobalSolver { Model = global };
                }
                solver.SolverAlgorithm = SolverAlgorithm.NelderMead;
                solver.MaxOptimizerIterations = primaryBudget;
                solver.SolverToleranceModifier = toleranceModifier;
                solver.Silent = true;
                return solver;
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GlobalSolverBootstrapKeepsMemberOrderAndReplicatePairing(bool weighted)
        {
            var first = CreateProbeExperiment(out var firstModel, out _);
            var second = CreateProbeExperiment(out var secondModel, out _);
            first.SetID("bootstrap-global-first");
            second.SetID("bootstrap-global-second");
            ConfigureFittedProbe(firstModel);
            ConfigureFittedProbe(secondModel);
            firstModel.ModelCloneOptions = new ModelCloneOptions
            {
                ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                IsGlobalClone = true,
            };
            secondModel.ModelCloneOptions = new ModelCloneOptions
            {
                ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                IsGlobalClone = true,
            };

            var global = new GlobalModel
            {
                ModelCloneOptions = new ModelCloneOptions
                {
                    ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                    IsGlobalClone = true,
                },
            };
            global.AddModel(firstModel);
            global.AddModel(secondModel);
            global.Parameters.AddIndivdualParameter(firstModel.Parameters);
            global.Parameters.AddIndivdualParameter(secondModel.Parameters);

            var solver = new GlobalSolver
            {
                Model = global,
                SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
                ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                BootstrapIterations = 6,
                MaxOptimizerIterations = 300,
                UseErrorWeightedFitting = weighted,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success);
            Assert.NotEmpty(global.Solution.BootstrapSolutions);
            Assert.Equal(weighted, global.Solution.UseWeightedFitting);
            Assert.Equal(weighted, firstModel.Solution.ParentSolution.UseWeightedFitting);
            // Members without shared parameters are fitted individually: n = 3, p = 1.
            AssertSyntheticSampleMatchesPrimaryFit(first, firstModel, weighted, seed: 19, residualScale: Math.Sqrt(3.0 / 2.0));
            foreach (var replicate in global.Solution.BootstrapSolutions)
            {
                Assert.Equal(new[] { first.UniqueID, second.UniqueID },
                    replicate.Solutions.Select(solution => solution.Data.UniqueID));
                Assert.Single(replicate.Solutions.Select(solution => solution.BootstrapReplicateIndex).Distinct());
            }
        }

        [Fact]
        public void PooledBootstrapKeepsEachExperimentsNoiseSizeAndIgnoresSdsWhenUnweighted()
        {
            // Centred residuals A = {-4, 0, 4} and B = {-1, 0, 1} have RMS sqrt(32/3) and sqrt(2/3).
            // Both scale to the unit values {-sqrt(1.5), 0, sqrt(1.5)}. Six observations and one
            // shared parameter give the correction f = sqrt(6/5).
            var f = Math.Sqrt(6.0 / 5);
            double[] Heats(double[] sdsA, double[] sdsB)
            {
                var model = CreateJointProbe(
                    CreatePooledMember("A", new[] { -4.0, 0.0, 4.0 }, sdsA),
                    CreatePooledMember("B", new[] { -1.0, 0.0, 1.0 }, sdsB));
                var pool = PooledBootstrapResiduals.Prepare(model, weighted: false);
                var clone = model.GenerateSyntheticModel(new PoolDraws(6, 0, 1, 2, 2, 0, 1), model.ModelCloneOptions, pool);
                return clone.Models.SelectMany(member => member.Data.Injections.Where(injection => injection.Include)
                    .Select(injection => injection.PeakArea.Value)).ToArray();
            }

            var heats = Heats(new[] { 1.0, 5.0, 2.0 }, new[] { 3.0, 1.0, 7.0 });
            var expected = new[] { 10 - 4 * f, 20, 30 + 4 * f, 10 + f, 20 - f, 30 };
            for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], heats[i], 12);
            Assert.Equal(heats, Heats(new[] { 1.0, 1.0, 1.0 }, new[] { 0.0, 1.0, 1.0 }));
        }

        [Fact]
        public void PooledNoiseVarianceMatchesTheWithinExperimentBootstrap()
        {
            var a = CreatePooledMember("A", new[] { -4.0, 0.0, 4.0 }, new[] { 1.0, 1.0, 1.0 });
            var b = CreatePooledMember("B", new[] { -1.0, 0.0, 1.0 }, new[] { 1.0, 1.0, 1.0 });
            var model = CreateJointProbe(a, b);
            var pool = PooledBootstrapResiduals.Prepare(model, weighted: false);

            // Over every pool value, the mean squared noise equals the within-experiment
            // bootstrap variance: mean centred residual squared times n / (n - p).
            double MeanSquare(ProbeModel member)
            {
                var injection = member.Data.Injections[1];
                var draws = Enumerable.Range(0, 6).Select(index => pool.SampleHeat(injection, new PoolDraws(6, index)) - 10);
                return draws.Average(noise => noise * noise);
            }

            Assert.Equal(32.0 / 3 * 6 / 5, MeanSquare(a), 12);
            Assert.Equal(2.0 / 3 * 6 / 5, MeanSquare(b), 12);
        }

        [Fact]
        public void WeightedPooledBootstrapKeepsSdShapeAndCentresEachExperiment()
        {
            // A: residuals {-2, 0, 4} over SDs {1, 1, 2} give r/SD = {-2, 0, 2}, RMS sqrt(8/3).
            // B: residuals {4, 5, 6} carry an offset of 5 that per-experiment centring removes,
            // leaving {-1, 0, 1} with RMS sqrt(2/3). Pool values are {-sqrt(1.5), 0, sqrt(1.5)} * f.
            var a = CreatePooledMember("A", new[] { -2.0, 0.0, 4.0 }, new[] { 1.0, 1.0, 2.0 });
            var b = CreatePooledMember("B", new[] { 4.0, 5.0, 6.0 }, new[] { 1.0, 1.0, 1.0 });
            var model = CreateJointProbe(a, b);
            var pool = PooledBootstrapResiduals.Prepare(model, weighted: true);
            var clone = model.GenerateSyntheticModel(new PoolDraws(6, 0, 1, 2, 3, 5, 4), model.ModelCloneOptions, pool);

            var f = Math.Sqrt(6.0 / 5);
            Assert.Equal(10 - 2 * f, clone.Models[0].Data.Injections[1].PeakArea.Value, 12);
            Assert.Equal(20, clone.Models[0].Data.Injections[2].PeakArea.Value, 12);
            Assert.Equal(30 + 4 * f, clone.Models[0].Data.Injections[3].PeakArea.Value, 12);
            Assert.Equal(10 - f, clone.Models[1].Data.Injections[1].PeakArea.Value, 12);
            Assert.Equal(20 + f, clone.Models[1].Data.Injections[2].PeakArea.Value, 12);
            Assert.Equal(30, clone.Models[1].Data.Injections[3].PeakArea.Value, 12);
            Assert.Equal(2, clone.Models[0].Data.Injections[3].PeakAreaError);
            Assert.Equal(77, clone.Models[0].Data.Injections[0].PeakArea.Value);
        }

        [Fact]
        public void PooledBootstrapGivesAnExperimentWithoutSpreadNoNoiseAndKeepsTheRun()
        {
            var flat = CreatePooledMember("Constant residual", new[] { 2.0, 2.0, 2.0 }, new[] { 1.0, 1.0, 1.0 });
            var b = CreatePooledMember("B", new[] { -1.0, 0.0, 1.0 }, new[] { 1.0, 1.0, 1.0 });
            var model = CreateJointProbe(flat, b);
            var pool = PooledBootstrapResiduals.Prepare(model, weighted: false);
            var clone = model.GenerateSyntheticModel(new PoolDraws(3, 0, 2, 1), model.ModelCloneOptions, pool);

            var f = Math.Sqrt(6.0 / 5);
            Assert.Equal(new[] { 10.0, 20.0, 30.0 },
                clone.Models[0].Data.Injections.Where(injection => injection.Include).Select(injection => injection.PeakArea.Value));
            Assert.Equal(10 - f, clone.Models[1].Data.Injections[1].PeakArea.Value, 12);
            Assert.Equal(20 + f, clone.Models[1].Data.Injections[2].PeakArea.Value, 12);
            Assert.Equal(30, clone.Models[1].Data.Injections[3].PeakArea.Value, 12);

            var run = CreateJointProbe(
                CreatePooledMember("Constant residual", new[] { 2.0, 2.0, 2.0 }, new[] { 1.0, 1.0, 1.0 }),
                CreatePooledMember("B", new[] { -1.0, 0.0, 1.0 }, new[] { 1.0, 1.0, 1.0 }));
            var convergence = CreatePooledSolver(run, weighted: false).Solve();
            Assert.True(convergence.Success);
            Assert.NotEqual(ErrorEstimationOutcome.CompleteFailure, convergence.ErrorEstimationOutcome);
            Assert.NotEmpty(run.Solution.BootstrapSolutions);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PooledBootstrapDistinguishesConstantResidualsFromSmallSpread(bool hasSpread)
        {
            var data = CreateProbeExperiment(out _, out _);
            var member = new ProbeModel(data, new Dictionary<int, double>());
            data.Model = member;
            ConfigureFittedProbe(member);
            const double small = 1e-12;
            var residuals = hasSpread ? new[] { -small, 0, small } : new[] { 0.1, 0.1, 0.1 };
            for (int i = 0; i < residuals.Length; i++)
                data.Injections[i + 1].SetPeakArea(new FloatWithError(residuals[i], 1));
            var other = CreatePooledMember("Other", new[] { -1.0, 0.0, 1.0 }, new[] { 1.0, 1.0, 1.0 });
            var pool = PooledBootstrapResiduals.Prepare(CreateJointProbe(member, other), weighted: false);

            var factor = Math.Sqrt(6.0 / 5);
            if (hasSpread)
                Assert.InRange(pool.SampleHeat(data.Injections[1], new PoolDraws(6, 5)),
                    small * factor - 1e-24, small * factor + 1e-24);
            else
                Assert.Equal(0, pool.SampleHeat(data.Injections[1], new PoolDraws(0)));
            var poolSize = hasSpread ? 6 : 3;
            var target = other.Data.Injections[1];
            var mean = Enumerable.Range(0, poolSize)
                .Average(index => pool.SampleHeat(target, new PoolDraws(poolSize, index)));
            Assert.Equal(10, mean, 12);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PooledBootstrapWithoutAnyResidualSpreadReturnsPredictions(bool weighted)
        {
            var a = CreatePooledMember("A", new[] { 0.0, 0.0 }, new[] { 1.0, 1.0 });
            var b = CreatePooledMember("B", new[] { 0.0, 0.0 }, new[] { 1.0, 1.0 });
            var model = CreateJointProbe(a, b);
            var pool = PooledBootstrapResiduals.Prepare(model, weighted);
            var clone = model.GenerateSyntheticModel(new PoolDraws(0), model.ModelCloneOptions, pool);
            Assert.All(clone.Models, member => Assert.Equal(new[] { 10.0, 20.0 },
                member.Data.Injections.Where(injection => injection.Include).Select(injection => injection.PeakArea.Value)));
        }

        [Fact]
        public void PooledBootstrapNeedsMoreObservationsThanParameters()
        {
            var model = CreateJointProbe(CreatePooledMember("Single", new[] { 1.0 }, new[] { 1.0 }));
            var exception = Assert.Throws<InvalidOperationException>(() => PooledBootstrapResiduals.Prepare(model, weighted: false));
            Assert.Contains("more included injections than fitted parameters", exception.Message);
        }

        [Theory]
        [InlineData(false, -0.8)]
        [InlineData(true, 10.0 / 7)]
        public void PooledBootstrapRefitsUsePrimaryWeightingAndKeepTheBestFit(bool weighted, double expected)
        {
            var a = CreatePooledMember("A", new[] { 2.0, 4.0 }, new[] { 2.0, 2.0 });
            var b = CreatePooledMember("B", new[] { -8.0, -4.0, 2.0 }, new[] { 4.0, 4.0, 2.0 });
            var model = CreateJointProbe(a, b);
            var solver = CreatePooledSolver(model, weighted);
            Assert.True(solver.Solve().Success);
            Assert.NotEmpty(model.Solution.BootstrapSolutions);
            Assert.Equal(expected, model.Solution.Solutions[0].Parameters[ParameterType.Offset].Value, 6);
            foreach (var replicate in model.Solution.BootstrapSolutions)
            {
                Assert.Equal(weighted, replicate.UseWeightedFitting);
                // Solve the shared intercept independently from the synthetic heats.
                var observations = replicate.Solutions.SelectMany(member => member.Data.Injections.Where(i => i.Include));
                var numerator = observations.Sum(i => (i.PeakArea.Value - 10 * i.ID) / (weighted ? i.PeakAreaError * i.PeakAreaError : 1));
                var denominator = observations.Sum(i => weighted ? 1 / (i.PeakAreaError * i.PeakAreaError) : 1);
                Assert.Equal(numerator / denominator, replicate.Solutions[0].Parameters[ParameterType.Offset].Value, 6);
            }
        }

        static ProbeModel CreatePooledMember(string name, double[] residuals, double[] sds)
        {
            var data = CreateProbeExperiment(out var member, out var predictions, residuals.Length, name);
            ConfigureFittedProbe(member);
            member.ModelCloneOptions = new ModelCloneOptions
            {
                ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                IsGlobalClone = true,
                IncludeConcentrationErrorsInBootstrap = false,
                UnlockBootstrapParameters = false,
            };
            data.Injections[0].SetPeakArea(new FloatWithError(77, 0));
            for (int i = 0; i < residuals.Length; i++)
                data.Injections[i + 1].SetPeakArea(new FloatWithError(predictions[i + 1] + residuals[i], sds[i]));
            return member;
        }

        static GlobalModel CreateJointProbe(params ProbeModel[] members)
        {
            var model = new GlobalModel
            {
                ModelCloneOptions = new ModelCloneOptions
                {
                    ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                    IsGlobalClone = true,
                    PoolBootstrapResiduals = true,
                    UnlockBootstrapParameters = false,
                },
            };
            foreach (var member in members)
            {
                model.AddModel(member);
                model.Parameters.AddIndivdualParameter(member.Parameters);
            }
            model.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
            model.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, 0);
            model.Parameters.SetIndividualFromGlobal();
            return model;
        }

        // Two members with their own fitted offsets; the enthalpy, locked in each member, is
        // shared and fitted when requested. Primary solutions are attached so both bootstrap
        // modes can read the joint fit.
        static GlobalModel CreateSharedEnthalpyJoint(ProbeModel first, ProbeModel second, bool shared = true)
        {
            var global = new GlobalModel
            {
                ModelCloneOptions = new ModelCloneOptions
                {
                    ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                    IsGlobalClone = true,
                    PoolBootstrapResiduals = true,
                    UnlockBootstrapParameters = false,
                },
            };
            foreach (var member in new[] { first, second })
            {
                global.AddModel(member);
                global.Parameters.AddIndivdualParameter(member.Parameters);
            }
            if (shared)
            {
                global.Parameters.SetConstraintForParameter(ParameterType.Enthalpy1, VariableConstraint.SameForAll);
                global.Parameters.AddorUpdateGlobalParameter(ParameterType.Enthalpy1, -1000);
            }
            var convergence = SolverConvergence.FromFixedFit(0, 0);
            var solutions = new List<SolutionInterface>
            {
                SolutionInterface.FromModel(first, convergence),
                SolutionInterface.FromModel(second, convergence),
            };
            first.Solution = solutions[0];
            second.Solution = solutions[1];
            global.Solution = new GlobalSolution(new GlobalSolver { Model = global }, solutions, convergence, reconstructBootstrap: false);
            return global;
        }

        static GlobalSolver CreatePooledSolver(GlobalModel model, bool weighted) => new GlobalSolver
        {
            Model = model,
            SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
            ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
            BootstrapIterations = 8,
            UseErrorWeightedFitting = weighted,
            MaxOptimizerIterations = 300,
            Silent = true,
        };

        sealed class PoolDraws : Random
        {
            readonly int poolSize;
            readonly Queue<int> draws;

            internal PoolDraws(int poolSize, params int[] draws)
            {
                this.poolSize = poolSize;
                this.draws = new Queue<int>(draws);
            }

            public override int Next(int maxValue)
            {
                Assert.Equal(poolSize, maxValue);
                return draws.Dequeue();
            }
        }

        [Fact]
        public void GlobalLeaveOneOutRunsOncePerExperimentAndKeepsOmissionOrder()
        {
            var experiments = Enumerable.Range(0, 3)
                .Select(index =>
                {
                    var data = CreateProbeExperiment(out var model, out _);
                    data.SetID($"global-loo-{index}");
                    ConfigureFittedProbe(model);
                    model.ModelCloneOptions = new ModelCloneOptions
                    {
                        ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                        IsGlobalClone = true,
                        IncludeConcentrationErrorsInBootstrap = true,
                        EnableAutoConcentrationVariance = true,
                        AutoConcentrationVariance = 0.2,
                        UnlockBootstrapParameters = true,
                    };
                    return (data, model);
                })
                .ToList();
            var global = new GlobalModel
            {
                ModelCloneOptions = new ModelCloneOptions
                {
                    ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                    IsGlobalClone = true,
                    IncludeConcentrationErrorsInBootstrap = true,
                    EnableAutoConcentrationVariance = true,
                    AutoConcentrationVariance = 0.2,
                    UnlockBootstrapParameters = true,
                },
            };
            foreach (var (_, model) in experiments)
            {
                global.AddModel(model);
                global.Parameters.AddIndivdualParameter(model.Parameters);
            }

            var solver = new GlobalSolver
            {
                Model = global,
                SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
                ErrorEstimationMethod = ErrorEstimationMethod.LeaveOneOut,
                BootstrapIterations = 999,
                MaxOptimizerIterations = 300,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success, convergence.Message);
            Assert.Equal(experiments.Count, global.Solution.BootstrapSolutions.Count);
            Assert.Equal(ErrorEstimationMethod.LeaveOneOut,
                global.ModelCloneOptions.ErrorEstimationMethod);
            Assert.All(global.Models, member =>
                Assert.Equal(ErrorEstimationMethod.LeaveOneOut,
                    member.ModelCloneOptions.ErrorEstimationMethod));
            for (var omission = 0; omission < experiments.Count; omission++)
            {
                var retained = global.Solution.BootstrapSolutions[omission].Solutions;
                Assert.DoesNotContain(retained,
                    solution => solution.Data.UniqueID == experiments[omission].data.UniqueID);
                Assert.Equal(
                    experiments.Where((_, index) => index != omission)
                        .Select(item => item.data.UniqueID),
                    retained.Select(solution => solution.Data.UniqueID));
                Assert.All(retained, solution =>
                {
                    var source = experiments.Single(item =>
                        item.data.UniqueID == solution.Data.UniqueID).data;
                    Assert.Equal(source.CellConcentration.Value,
                        solution.Data.CellConcentration.Value, 12);
                    Assert.Equal(source.SyringeConcentration.Value,
                        solution.Data.SyringeConcentration.Value, 12);
                });
            }
        }

        static void ConfigureFittedProbe(ProbeModel model)
        {
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1, true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000, true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6, true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        }

        static void AssertSyntheticSampleMatchesPrimaryFit(
            ExperimentData data,
            ProbeModel model,
            bool weighted,
            int seed,
            double residualScale)
        {
            Assert.Equal(weighted, model.Solution.ParentSolution?.UseWeightedFitting
                ?? model.Solution.UseWeightedFitting);
            var included = data.Injections.Where(injection => injection.Include).ToList();
            var sigmas = weighted
                ? included.Select(injection => Model.GetSigmaForWeighting(injection, included)).ToArray()
                : Enumerable.Repeat(1.0, included.Count).ToArray();
            var residuals = included.Select((injection, index) =>
            {
                var residual = injection.PeakArea.Value - model.Evaluate(injection.ID, withoffset: true);
                return weighted ? residual / sigmas[index] : residual;
            }).ToArray();
            var centred = residuals.Select(value => (value - residuals.Average()) * residualScale).ToArray();
            var random = new Random(seed);
            var clone = data.GetSynthClone(
                new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
                new Random(seed));

            for (var index = 0; index < included.Count; index++)
            {
                var injection = included[index];
                var expected = model.Evaluate(injection.ID, withoffset: true)
                    + sigmas[index] * centred[random.Next(centred.Length)];
                Assert.Equal(expected, clone.Injections[injection.ID].PeakArea.Value, 12);
            }
        }

        static ExperimentData CreateProbeExperiment(
            out ProbeModel model,
            out Dictionary<int, double> predictions,
            int includedInjectionCount = 3,
            string fileName = "residual-bootstrap.itc")
        {
            var data = new ExperimentData(fileName)
            {
                CellConcentration = new FloatWithError(35e-6),
                SyringeConcentration = new FloatWithError(420e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };

            AddInjection(data, 0, include: false, area: 77, sd: 9);
            for (var id = 1; id <= includedInjectionCount; id++)
                AddInjection(data, id, include: true, area: 10 + id, sd: 1 + id % 4);

            predictions = Enumerable.Range(1, includedInjectionCount)
                .ToDictionary(id => id, id => id * 10.0);
            model = new ProbeModel(data, predictions);
            data.Model = model;
            return data;
        }

        static void AddInjection(ExperimentData data, int id, bool include, double area, double sd)
        {
            var injection = new InjectionData(data, id, 2e-6, data.SyringeConcentration * 2e-6, include)
            {
                ActualCellConcentration = data.CellConcentration * (1 - id * 0.01),
                ActualTitrantConcentration = id * 2.5e-6,
                Ratio = id * 0.1,
                Temperature = 25 + id,
            };
            injection.SetPeakArea(new FloatWithError(area, sd));
            data.Injections.Add(injection);
        }

        sealed class ProbeModel : Model
        {
            readonly IReadOnlyDictionary<int, double> predictions;
            internal int? FixedBootstrapDraw { get; set; }

            public ProbeModel(ExperimentData data, IReadOnlyDictionary<int, double> predictions) : base(data)
            {
                this.predictions = predictions;
            }

            public override double Evaluate(int injectionindex, bool withoffset = true)
            {
                var value = predictions.TryGetValue(injectionindex, out var prediction) ? prediction : 0;
                if (withoffset && Parameters.Table.TryGetValue(ParameterType.Offset, out var offset))
                    value += offset.Value;
                return value;
            }

            internal override Model GenerateSyntheticModel(Random random, ModelCloneOptions options)
            {
                if (FixedBootstrapDraw is int draw)
                    random = new PoolDraws(predictions.Count, Enumerable.Repeat(draw, predictions.Count).ToArray());
                var synthetic = new ProbeModel(Data.GetSynthClone(options, random), predictions);
                SetSynthModelParameters(synthetic, random, options);
                return synthetic;
            }
        }
    }
}
