using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests
{
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

        static void AddLockedBindingParameters(Model model)
        {
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1, true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000, true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6, true);
        }

        static ExperimentData CreateOffsetExperiment(double offset)
        {
            var experiment = new ExperimentData("zero-offset-fit.itc")
            {
                SyringeConcentration = new FloatWithError(1e-3),
            };

            for (var index = 0; index < 10; index++)
            {
                var injection = new InjectionData(experiment, 2e-6);
                injection.SetPeakArea(new FloatWithError(offset * injection.InjectionMass));
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
    }
}
