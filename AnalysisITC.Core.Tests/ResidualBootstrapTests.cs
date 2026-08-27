using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
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
        public void ResidualBootstrapUsesWeightingFallbackWithoutReplacingDeclaredSd()
        {
            var data = CreateProbeExperiment(out var model, out var predictions);
            data.Injections[1].SetPeakArea(new FloatWithError(12, 2));
            data.Injections[2].SetPeakArea(new FloatWithError(24, 0));
            data.Injections[3].SetPeakArea(new FloatWithError(26, double.NaN));
            model.Solution = SolutionInterface.FromModel(model, SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));

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

        [Fact]
        public void SingleSolverBootstrapRetainsPrimaryValueWeightingAndReplicateOrder()
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
                UseErrorWeightedFitting = true,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success);
            Assert.NotEmpty(model.Solution.BootstrapSolutions);
            Assert.Equal(
                model.Parameters.Table[ParameterType.Offset].Value,
                model.Solution.Parameters[ParameterType.Offset].Value);
            Assert.All(model.Solution.BootstrapSolutions, solution => Assert.True(solution.UseWeightedFitting));
            Assert.Equal(
                model.Solution.BootstrapSolutions.Select(solution => solution.BootstrapReplicateIndex).OrderBy(index => index),
                model.Solution.BootstrapSolutions.Select(solution => solution.BootstrapReplicateIndex));
        }

        [Fact]
        public void GlobalSolverBootstrapKeepsMemberOrderAndReplicatePairing()
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
                UseErrorWeightedFitting = true,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success);
            Assert.NotEmpty(global.Solution.BootstrapSolutions);
            foreach (var replicate in global.Solution.BootstrapSolutions)
            {
                Assert.Equal(new[] { first.UniqueID, second.UniqueID },
                    replicate.Solutions.Select(solution => solution.Data.UniqueID));
                Assert.Single(replicate.Solutions.Select(solution => solution.BootstrapReplicateIndex).Distinct());
            }
        }

        static void ConfigureFittedProbe(ProbeModel model)
        {
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1, true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000, true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6, true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        }

        static ExperimentData CreateProbeExperiment(out ProbeModel model, out Dictionary<int, double> predictions)
        {
            var data = new ExperimentData("residual-bootstrap.itc")
            {
                CellConcentration = new FloatWithError(35e-6),
                SyringeConcentration = new FloatWithError(420e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };

            AddInjection(data, 0, include: false, area: 77, sd: 9);
            AddInjection(data, 1, include: true, area: 11, sd: 1);
            AddInjection(data, 2, include: true, area: 24, sd: 2);
            AddInjection(data, 3, include: true, area: 26, sd: 4);

            predictions = new Dictionary<int, double>
            {
                [1] = 10,
                [2] = 20,
                [3] = 30,
            };
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

            internal override Model GenerateSyntheticModel(Random random)
            {
                var synthetic = new ProbeModel(Data.GetSynthClone(ModelCloneOptions, random), predictions);
                SetSynthModelParameters(synthetic, random);
                return synthetic;
            }
        }
    }
}
