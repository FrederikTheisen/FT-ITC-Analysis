using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Application;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [Collection("AutoSaveManager")]
    public class SolverInitialParameterTests
    {
        [Fact]
        public void LevenbergMarquardtVariesOffsetInitializedAtZero()
        {
            const double expectedOffset = 2000;
            var experiment = CreateOffsetExperiment(expectedOffset);
            var model = new OffsetOnlyModel(experiment);
            AddLockedBindingParameters(model);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);

            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                MaxOptimizerIterations = 1000,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success);
            Assert.InRange(
                model.Parameters.Table[ParameterType.Offset].Value,
                expectedOffset - 0.01,
                expectedOffset + 0.01);
        }

        [Fact]
        public void GlobalLevenbergMarquardtVariesOffsetInitializedAtZero()
        {
            const double expectedOffset = 2000;
            var experiment = CreateOffsetExperiment(expectedOffset);
            var model = new OffsetOnlyModel(experiment);
            AddLockedBindingParameters(model);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);

            var globalModel = new GlobalModel();
            globalModel.AddModel(model);
            globalModel.Parameters.AddIndivdualParameter(model.Parameters);

            var solver = new GlobalSolver
            {
                Model = globalModel,
                SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                MaxOptimizerIterations = 1000,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success);
            Assert.InRange(
                model.Parameters.Table[ParameterType.Offset].Value,
                expectedOffset - 0.01,
                expectedOffset + 0.01);
        }

        public static IEnumerable<object[]> ExactBoundCases =>
            from algorithm in new[] { SolverAlgorithm.NelderMead, SolverAlgorithm.LevenbergMarquardt }
            from boundSign in new[] { -1, 1 }
            from targetSign in new[] { -1, 1 }
            from weighted in new[] { false, true }
            from tolerance in new[] { 0d, 0.5d, 1d }
            from global in new[] { false, true }
            select new object[] { algorithm, boundSign, targetSign, weighted, tolerance, global };

        [Theory]
        [MemberData(nameof(ExactBoundCases))]
        public void ExactBoundStartRefitsToIndependentInteriorOffset(
            SolverAlgorithm algorithm, int boundSign, int targetSign, bool weighted, double tolerance, bool global)
        {
            var previousTolerance = AppSettings.OptimizerTolerance;
            try
            {
                AppSettings.OptimizerTolerance = tolerance;
                const double expectedMagnitude = 2000;
                var expectedOffset = targetSign * expectedMagnitude;
                var experiment = CreateOffsetExperiment(expectedOffset, weighted);
                var model = new OffsetOnlyModel(experiment);
                AddLockedBindingParameters(model);
                model.Parameters.AddOrUpdateParameter(ParameterType.Offset, boundSign * 50000);
                GlobalModel sharedModel = global
                    ? CreateSharedOffsetModel(model, expectedOffset, boundSign * 50000, weighted)
                    : null;
                SolverInterface solver;
                if (global) solver = new GlobalSolver { Model = sharedModel };
                else solver = new Solver { Model = model };
                solver.SolverAlgorithm = algorithm;
                solver.UseErrorWeightedFitting = weighted;
                solver.ErrorEstimationMethod = ErrorEstimationMethod.None;
                solver.MaxOptimizerIterations = 1500;
                solver.Silent = true;
                var baselineObjective = ObjectiveAtOffset(global ? sharedModel.Models : new[] { model }, boundSign * 50000d, weighted);
                Assert.True(double.IsFinite(baselineObjective));

                var convergence = solver.Solve();
                var acceptedObjective = ObjectiveAtOffset(global ? sharedModel.Models : new[] { model },
                    model.Parameters.Table[ParameterType.Offset].Value, weighted);
                Assert.True(double.IsFinite(acceptedObjective));
                var error = Math.Abs(model.Parameters.Table[ParameterType.Offset].Value - expectedOffset);
                var maximumError = tolerance == 0 ? 1 : tolerance == 0.5 ? 0.05 : 0.01;
                Assert.True(convergence.Success, convergence.Message);
                Assert.InRange(error, 0, maximumError);
                Assert.True(acceptedObjective <= baselineObjective);
                if (global) Assert.Equal(expectedOffset, sharedModel.Models[1].Parameters.Table[ParameterType.Offset].Value, maximumError);
            }
            finally
            {
                AppSettings.OptimizerTolerance = previousTolerance;
            }
        }

        public static IEnumerable<object[]> BoundaryCases =>
            from algorithm in new[] { SolverAlgorithm.NelderMead, SolverAlgorithm.LevenbergMarquardt }
            from sign in new[] { -1, 1 }
            from weighted in new[] { false, true }
            from tolerance in new[] { 0d, 0.5d, 1d }
            from global in new[] { false, true }
            select new object[] { algorithm, sign, weighted, tolerance, global };

        [Theory]
        [MemberData(nameof(BoundaryCases))]
        public void TighteningFromExpandedClampsToIndependentStandardBoundary(
            SolverAlgorithm algorithm, int sign, bool weighted, double tolerance, bool global)
        {
            var previousLimit = AppSettings.ParameterLimitSetting;
            var previousTolerance = AppSettings.OptimizerTolerance;
            try
            {
                AppSettings.OptimizerTolerance = tolerance;
                AppSettings.ParameterLimitSetting = ParameterLimitSetting.Extended;
                var experiment = CreateOffsetExperiment(sign * 60000, weighted);
                var model = new OffsetOnlyModel(experiment);
                AddLockedBindingParameters(model);
                model.Parameters.AddOrUpdateParameter(ParameterType.Offset, sign * 60000);
                AppSettings.ParameterLimitSetting = ParameterLimitSetting.Standard;
                model.Parameters.Table[ParameterType.Offset].RefreshLimits();
                var baseline = sign * 50000d;
                GlobalModel sharedModel = global
                    ? CreateSharedOffsetModel(model, sign * 60000, sign * 60000, weighted)
                    : null;
                var baselineObjective = ObjectiveAtOffset(global ? sharedModel.Models : new[] { model }, baseline, weighted);
                Assert.True(double.IsFinite(baselineObjective));

                SolverInterface solver;
                if (global) solver = new GlobalSolver { Model = sharedModel };
                else solver = new Solver { Model = model };
                solver.SolverAlgorithm = algorithm;
                solver.UseErrorWeightedFitting = weighted;
                solver.ErrorEstimationMethod = ErrorEstimationMethod.None;
                solver.MaxOptimizerIterations = 1500;
                solver.Silent = true;
                var convergence = solver.Solve();

                Assert.True(convergence.Success, convergence.Message);
                Assert.Equal(baseline, model.Parameters.Table[ParameterType.Offset].Value, 6);
                var acceptedObjective = ObjectiveAtOffset(global ? sharedModel.Models : new[] { model },
                    model.Parameters.Table[ParameterType.Offset].Value, weighted);
                Assert.True(double.IsFinite(acceptedObjective));
                Assert.True(acceptedObjective <= baselineObjective);
                if (global) Assert.Equal(baseline, sharedModel.Models[1].Parameters.Table[ParameterType.Offset].Value, 6);
            }
            finally
            {
                AppSettings.ParameterLimitSetting = previousLimit;
                AppSettings.OptimizerTolerance = previousTolerance;
            }
        }

        [Theory]
        [InlineData(false, 0, 1, -1)]
        [InlineData(false, 2000, 2000, 2001)]
        [InlineData(true, 0, 1, -1)]
        [InlineData(true, 2000, 2000, 2001)]
        public void InwardOptimizerTieOrTinyWorseningKeepsExactBaseline(bool global, double targetOffset, double baselineOffset, double fittedOffset)
        {
            var model = new OffsetOnlyModel(CreateOffsetExperiment(targetOffset));
            AddLockedBindingParameters(model);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, baselineOffset);
            var fitted = new[] { fittedOffset };
            if (targetOffset == 0)
            {
                Assert.True(model.TryLossFunction(new[] { baselineOffset }, false, out var exactScore));
                Assert.True(model.TryLossFunction(new[] { fittedOffset }, false, out var tiedScore));
                Assert.Equal(exactScore, tiedScore);
            }
            if (!global)
                new AcceptanceProbeSolver().Apply(model, new[] { baselineOffset }, fitted);
            else
            {
                var globalModel = new GlobalModel();
                globalModel.AddModel(model);
                globalModel.Parameters.AddIndivdualParameter(model.Parameters);
                new GlobalAcceptanceProbeSolver().Apply(globalModel, new[] { baselineOffset }, fitted);
            }
            Assert.Equal(baselineOffset, model.Parameters.Table[ParameterType.Offset].Value);
        }

        [Theory]
        [InlineData(0d, 0.0001d)]
        [InlineData(-0.0001d, 0.0001d)]
        public void LevenbergMarquardtZeroKickStaysInsideNarrowBounds(double lower, double upper)
        {
            var parameter = new Parameter(ParameterType.Offset, 0);
            parameter.SetLimits(new[] { lower, upper });
            var initial = new LmInitialGuessProbe().Get(new[] { parameter }, new[] { 0d });
            Assert.True(initial[0] > lower);
            Assert.True(initial[0] < upper);
        }

        [Fact]
        public void LevenbergMarquardtNarrowZeroKickCannotWorsenExactOptimum()
        {
            var model = new OffsetOnlyModel(CreateOffsetExperiment(0));
            AddLockedBindingParameters(model);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            model.Parameters.Table[ParameterType.Offset].SetLimits(new[] { -0.0001, 0.0001 });
            Assert.True(model.TryLossFunction(new[] { 0d }, false, out var baselineObjective));

            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                MaxOptimizerIterations = 1000,
                Silent = true,
            };
            Assert.True(solver.Solve().Success);
            Assert.True(model.TryLossFunction(model.Parameters.GetFittedParameterArray(), false, out var acceptedObjective));
            Assert.True(acceptedObjective <= baselineObjective);
            Assert.Equal(0, model.Parameters.Table[ParameterType.Offset].Value);
        }

        [Theory]
        [InlineData(SolverAlgorithm.NelderMead, false, double.NaN)]
        [InlineData(SolverAlgorithm.LevenbergMarquardt, false, double.PositiveInfinity)]
        [InlineData(SolverAlgorithm.NelderMead, true, double.NaN)]
        [InlineData(SolverAlgorithm.LevenbergMarquardt, true, double.PositiveInfinity)]
        public void DirectSolveReturnsInvalidValuesForNonfiniteStartingCoordinates(
            SolverAlgorithm algorithm, bool global, double invalidValue)
        {
            var model = new OffsetOnlyModel(CreateOffsetExperiment(2000));
            AddLockedBindingParameters(model);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, invalidValue);
            SolverInterface solver;
            if (global)
            {
                var globalModel = new GlobalModel();
                globalModel.AddModel(model);
                globalModel.Parameters.AddIndivdualParameter(model.Parameters);
                solver = new GlobalSolver { Model = globalModel };
            }
            else solver = new Solver { Model = model };
            solver.SolverAlgorithm = algorithm;
            solver.ErrorEstimationMethod = ErrorEstimationMethod.None;
            solver.Silent = true;

            var convergence = solver.Solve();

            Assert.True(convergence.Failed);
            Assert.Equal(SolverTermination.InvalidValues, convergence.Termination);
            Assert.NotNull(global ? ((GlobalSolver)solver).Model.Solution : model.Solution);
        }

        static void AddLockedBindingParameters(Model model)
        {
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1, true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000, true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6, true);
        }

        static double ObjectiveAtOffset(IEnumerable<Model> models, double offset, bool weighted)
        {
            return models.SelectMany(model => model.Data.Injections)
                .Where(injection => injection.Include && injection.IsIntegrated)
                .Sum(injection =>
                {
                    var residual = injection.PeakArea.Value - offset * injection.InjectionMass;
                    if (weighted) residual /= injection.PeakAreaError;
                    return residual * residual;
                });
        }

        static GlobalModel CreateSharedOffsetModel(OffsetOnlyModel first, double targetOffset, double sharedStart, bool includeErrors)
        {
            var second = new OffsetOnlyModel(CreateOffsetExperiment(targetOffset, includeErrors));
            AddLockedBindingParameters(second);
            second.Parameters.AddOrUpdateParameter(ParameterType.Offset, sharedStart);
            var global = new GlobalModel();
            global.AddModel(first);
            global.AddModel(second);
            global.Parameters.AddIndivdualParameter(first.Parameters);
            global.Parameters.AddIndivdualParameter(second.Parameters);
            global.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
            global.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, sharedStart, limits: first.Parameters.Table[ParameterType.Offset].Limits);
            global.Parameters.SetIndividualFromGlobal();
            return global;
        }

        static ExperimentData CreateOffsetExperiment(double offset, bool includeErrors = false)
        {
            var experiment = new ExperimentData("zero-offset-fit.itc")
            {
                SyringeConcentration = new FloatWithError(1e-3),
            };

            for (var index = 0; index < 10; index++)
            {
                var injection = new InjectionData(experiment, 2e-6);
                var heat = offset * injection.InjectionMass;
                injection.SetPeakArea(includeErrors
                    ? new FloatWithError(heat, Math.Abs(injection.InjectionMass) * 100)
                    : new FloatWithError(heat));
                experiment.Injections.Add(injection);
            }

            return experiment;
        }

        sealed class OffsetOnlyModel : OneSetOfSites
        {
            public OffsetOnlyModel(ExperimentData data) : base(data)
            {
            }

            public override double Evaluate(int injectionindex, bool withoffset = true)
            {
                return withoffset
                    ? Parameters.Table[ParameterType.Offset].Value
                        * Data.Injections[injectionindex].InjectionMass
                    : 0;
            }
        }

        sealed class LmInitialGuessProbe : Solver
        {
            public double[] Get(System.Collections.Generic.IReadOnlyList<Parameter> parameters, double[] exact)
                => GetLevenbergMarquardtInitialGuess(parameters, exact, out _);
        }

        sealed class AcceptanceProbeSolver : Solver
        {
            public void Apply(Model model, double[] initial, double[] fitted)
            {
                Model = model;
                ApplyBestFittedParameters(model, initial, fitted, false, "test/local", model.Parameters.GetFittedParameters(), true);
            }
        }

        sealed class GlobalAcceptanceProbeSolver : GlobalSolver
        {
            public void Apply(GlobalModel model, double[] initial, double[] fitted)
            {
                Model = model;
                ApplyBestFittedParameters(model, initial, fitted, false, "test/global", model.Parameters.GetFittedParameters(), true);
            }
        }
    }
}
