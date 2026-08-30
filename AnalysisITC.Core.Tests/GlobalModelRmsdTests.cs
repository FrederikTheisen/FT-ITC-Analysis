using System;
using System.Collections.Generic;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class GlobalModelRmsdTests
    {
        [Fact]
        public void GlobalLossPoolsIncludedRawResidualsRegardlessOfMemberSizeOrWeighting()
        {
            var first = CreateProbe(new[] { new ResidualSpec(true, 1e-6, 1e-6) });
            var second = CreateProbe(new[]
            {
                new ResidualSpec(true, 3e-6, 3e-6),
                new ResidualSpec(true, -3e-6, 3e-6),
                new ResidualSpec(true, 3e-6, 3e-6),
                new ResidualSpec(false, 100e-6, 1e-9),
            });
            var global = CreateGlobal(first, second);

            Assert.Equal(Math.Sqrt(7), global.Loss(), 12);
            Assert.Equal(28e-12, global.LossFunction(Array.Empty<double>(), errorweighted: false), 15);
            Assert.Equal(4, global.LossFunction(Array.Empty<double>(), errorweighted: true), 12);
        }

        [Fact]
        public void GlobalLossIsInvariantWhenAnIdenticalMemberIsDuplicated()
        {
            var singleMember = CreateProbe(new[]
            {
                new ResidualSpec(true, 1e-6, 1e-6),
                new ResidualSpec(true, -3e-6, 2e-6),
            });
            var duplicateMember = CreateProbe(new[]
            {
                new ResidualSpec(true, 1e-6, 1e-6),
                new ResidualSpec(true, -3e-6, 2e-6),
            });

            Assert.Equal(CreateGlobal(singleMember).Loss(), CreateGlobal(singleMember, duplicateMember).Loss(), 12);
        }

        [Fact]
        public void GlobalLossIgnoresFullyExcludedMembersAndMatchesOneMemberLoss()
        {
            var included = CreateProbe(new[] { new ResidualSpec(true, 2e-6, 1e-6) });
            var excluded = CreateProbe(new[] { new ResidualSpec(false, 100e-6, 1e-9) });

            Assert.Equal(included.Loss(), CreateGlobal(included).Loss(), 12);
            Assert.Equal(included.Loss(), CreateGlobal(included, excluded).Loss(), 12);
            Assert.True(double.IsNaN(CreateGlobal(excluded).Loss()));
        }

        [Fact]
        public void IndividuallyFittedGlobalSolutionRefreshesParentLossWithoutChangingMemberLosses()
        {
            var first = CreateProbe(new[] { new ResidualSpec(true, 1e-6, 1e-6) });
            var second = CreateProbe(new[]
            {
                new ResidualSpec(true, 3e-6, 3e-6),
                new ResidualSpec(true, -3e-6, 3e-6),
                new ResidualSpec(true, 3e-6, 3e-6),
            });
            var firstSolution = SolutionInterface.FromModel(first, Convergence(1));
            var secondSolution = SolutionInterface.FromModel(second, Convergence(3));
            first.Solution = firstSolution;
            second.Solution = secondSolution;
            var global = CreateGlobal(first, second);
            var globalSolution = new GlobalSolution(
                new GlobalSolver { Model = global },
                new List<SolutionInterface> { firstSolution, secondSolution },
                Convergence(999));

            Assert.Equal(Math.Sqrt(7), globalSolution.Loss, 12);
            Assert.Equal(1, firstSolution.Loss, 12);
            Assert.Equal(3, secondSolution.Loss, 12);
        }

        [Fact]
        public void GloballyFittedSolutionRefreshesParentAndMemberLosses()
        {
            var first = CreateProbe(new[] { new ResidualSpec(true, 1e-6, 1e-6) });
            var second = CreateProbe(new[]
            {
                new ResidualSpec(true, 3e-6, 3e-6),
                new ResidualSpec(true, -3e-6, 3e-6),
                new ResidualSpec(true, 3e-6, 3e-6),
            });
            var global = CreateGlobal(first, second);
            var globalSolution = new GlobalSolution(
                new GlobalSolver { Model = global },
                Convergence(999));

            Assert.Equal(Math.Sqrt(7), globalSolution.Loss, 12);
            Assert.Equal(1, globalSolution.Solutions[0].Loss, 12);
            Assert.Equal(3, globalSolution.Solutions[1].Loss, 12);
        }

        static GlobalModel CreateGlobal(params ProbeModel[] models)
        {
            var global = new GlobalModel();
            foreach (var model in models)
            {
                global.AddModel(model);
                global.Parameters.AddIndivdualParameter(model.Parameters);
            }

            return global;
        }

        static ProbeModel CreateProbe(IReadOnlyList<ResidualSpec> residuals)
        {
            var data = new ExperimentData("global-rmsd.itc")
            {
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(100e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };
            var predictions = new Dictionary<int, double>();
            for (var index = 0; index < residuals.Count; index++)
            {
                var spec = residuals[index];
                var injection = new InjectionData(data, index, 2e-6, 2e-10, spec.Include)
                {
                    ActualCellConcentration = 10e-6,
                    ActualTitrantConcentration = index * 2e-6,
                };
                injection.SetPeakArea(new FloatWithError(spec.Residual, spec.Sigma));
                data.Injections.Add(injection);
                predictions[index] = 0;
            }

            var model = new ProbeModel(data, predictions);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1, islocked: true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000, islocked: true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6, islocked: true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0, islocked: true);
            data.Model = model;
            return model;
        }

        static SolverConvergence Convergence(double loss) => SolverConvergence.FromSnapshot(
            new SolverConvergenceSnapshot
            {
                Algorithm = SolverAlgorithm.LevenbergMarquardt,
                Termination = SolverTermination.Converged,
                Loss = loss,
            });

        readonly struct ResidualSpec
        {
            public bool Include { get; }
            public double Residual { get; }
            public double Sigma { get; }

            public ResidualSpec(bool include, double residual, double sigma)
            {
                Include = include;
                Residual = residual;
                Sigma = sigma;
            }
        }

        sealed class ProbeModel : Model
        {
            readonly IReadOnlyDictionary<int, double> predictions;

            public ProbeModel(ExperimentData data, IReadOnlyDictionary<int, double> predictions)
                : base(data) => this.predictions = predictions;

            public override double Evaluate(int injectionindex, bool withoffset = true) =>
                predictions[injectionindex];
        }
    }
}
