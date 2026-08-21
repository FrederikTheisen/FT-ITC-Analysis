using System;
using System.Globalization;
using System.IO;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [Collection("Published model reproduction")]
    public sealed class PublishedTwoSiteModelReproductionTests
    {
        const double ExpectedAssociationConstant1 = 1.18e10;
        const double ExpectedEnthalpy1CalPerMole = 767.3;
        const double ExpectedAssociationConstant2 = 3.46e7;
        const double ExpectedEnthalpy2CalPerMole = -1.203e4;
        const double RelativeTolerance = 0.15;

        [Theory]
        [InlineData(SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(SolverAlgorithm.NelderMead)]
        public void TwoSetsOfSitesReproducesPublishedFeOtf54Parameters(SolverAlgorithm algorithm)
        {
            var experiment = LoadExperiment();
            var model = new TwoSetsOfSites(experiment);
            model.InitializeParameters(experiment);

            // The published forward model fixes both site stoichiometries at
            // one. Keep that constraint and recover the four fitted terms.
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1.0, true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, Energy.ConvertToJoule(2000, EnergyUnit.Cal));
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 8.0);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue2, 1.0, true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy2, Energy.ConvertToJoule(-9000, EnergyUnit.Cal));
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity2, 6.0);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0.0);

            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = algorithm,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                MaxOptimizerIterations = 6000,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success, convergence.Message);
            Assert.True(model.Parameters.Table[ParameterType.Nvalue1].IsLocked);
            Assert.Equal(1.0, model.Parameters.Table[ParameterType.Nvalue1].Value);
            AssertRelativeAgreement("Ka1", ExpectedAssociationConstant1, Math.Pow(10.0, model.Parameters.Table[ParameterType.Affinity1].Value));
            AssertRelativeAgreement("dH1", Energy.ConvertToJoule(ExpectedEnthalpy1CalPerMole, EnergyUnit.Cal), model.Parameters.Table[ParameterType.Enthalpy1].Value);
            Assert.True(model.Parameters.Table[ParameterType.Nvalue2].IsLocked);
            Assert.Equal(1.0, model.Parameters.Table[ParameterType.Nvalue2].Value);
            AssertRelativeAgreement("Ka2", ExpectedAssociationConstant2, Math.Pow(10.0, model.Parameters.Table[ParameterType.Affinity2].Value));
            AssertRelativeAgreement("dH2", Energy.ConvertToJoule(ExpectedEnthalpy2CalPerMole, EnergyUnit.Cal), model.Parameters.Table[ParameterType.Enthalpy2].Value);
        }

        static ExperimentData LoadExperiment()
        {
            var experiment = new ExperimentData("feotf54-independent-sites.ndh")
            {
                CellConcentration = new(31.4e-6),
                SyringeConcentration = new(1560e-6),
                CellVolume = 1411e-6,
                TargetTemperature = 25,
            };

            var lines = File.ReadAllLines(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PublishedBenchmarks",
                "feotf54-independent-sites.ndh"));

            foreach (var line in lines.Skip(1))
            {
                var columns = line.Split('\t')
                    .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
                    .ToArray();
                var injection = new InjectionData(experiment, columns[3] * 1e-6)
                {
                    // The table exports P_Correct, but the accompanying
                    // forward model propagates P_T at its initial 31.4 uM.
                    ActualCellConcentration = experiment.CellConcentration,
                    ActualTitrantConcentration = columns[1] * 1e-6,
                    Ratio = columns[1] / (experiment.CellConcentration * 1e6),
                };

                // The downloaded table is normalized integrated heat in cal/mol.
                injection.SetPeakArea(new FloatWithError(
                    Energy.ConvertToJoule(columns[7], EnergyUnit.Cal) * injection.InjectionMass,
                    0));
                experiment.Injections.Add(injection);
            }

            return experiment;
        }

        static void AssertRelativeAgreement(string parameter, double expected, double actual)
        {
            var relativeDifference = Math.Abs((actual - expected) / expected);
            Assert.True(
                relativeDifference <= RelativeTolerance,
                $"{parameter}: expected {expected:G10}, fitted {actual:G10}, relative difference {relativeDifference:P3}");
        }
    }
}
