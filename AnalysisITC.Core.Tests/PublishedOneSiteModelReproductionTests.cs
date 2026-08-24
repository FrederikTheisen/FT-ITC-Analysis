using System;
using System.Globalization;
using System.IO;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [Collection("Published model reproduction")]
    public sealed class PublishedOneSiteModelReproductionTests : IDisposable
    {
        // These are the fitted FT-ITC parameters for the archived integrated
        // heats, with the source stoichiometry fixed at one.  They are not a
        // claim that the source's K = 1e6 M^-1 is reproduced exactly; the
        // source exports direct differences of heat content while FT-ITC
        // applies its injection-volume heat correction.
        const double ExpectedAssociationConstant = 7.663e5;
        const double ExpectedEnthalpyCalPerMole = -9954.2;
        const double RelativeTolerance = 0.005;

        readonly DilutionMethod originalDilutionMethod;

        public PublishedOneSiteModelReproductionTests()
        {
            originalDilutionMethod = AppSettings.DilutionCalculationMethod;
        }

        public void Dispose()
        {
            AppSettings.DilutionCalculationMethod = originalDilutionMethod;
        }

        [Theory]
        [InlineData(DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(DilutionMethod.MicroCal, SolverAlgorithm.NelderMead)]
        [InlineData(DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(DilutionMethod.Exponential, SolverAlgorithm.NelderMead)]
        public void OneSetOfSitesFitsMEquivalentIntegratedHeatBenchmark(
            DilutionMethod dilutionMethod,
            SolverAlgorithm algorithm)
        {
            AppSettings.DilutionCalculationMethod = dilutionMethod;
            var experiment = LoadExperiment();
            var model = new OneSetOfSites(experiment);
            model.InitializeParameters(experiment);

            // The accompanying forward-model script fixes N = 1.
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1.0, true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, Energy.ConvertToJoule(-10000, EnergyUnit.Cal));
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6.0);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, Energy.ConvertToJoule(-100, EnergyUnit.Cal));

            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = algorithm,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                UseErrorWeightedFitting = false,
                MaxOptimizerIterations = 4000,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success, convergence.Message);
            Assert.True(model.Parameters.Table[ParameterType.Nvalue1].IsLocked);
            Assert.Equal(1.0, model.Parameters.Table[ParameterType.Nvalue1].Value);
            AssertRelativeAgreement(
                "Ka",
                ExpectedAssociationConstant,
                Math.Pow(10.0, model.Parameters.Table[ParameterType.Affinity1].Value));
            AssertRelativeAgreement(
                "dH",
                Energy.ConvertToJoule(ExpectedEnthalpyCalPerMole, EnergyUnit.Cal),
                model.Parameters.Table[ParameterType.Enthalpy1].Value);
        }

        static ExperimentData LoadExperiment()
        {
            var experiment = new ExperimentData("bbr2020-m-equivalent.ndh")
            {
                CellConcentration = new(20e-6),
                SyringeConcentration = new(200e-6),
                CellVolume = 230e-6,
                TargetTemperature = 25,
            };

            var lines = File.ReadAllLines(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PublishedBenchmarks",
                "bbr2020-m-equivalent.ndh"));

            foreach (var line in lines.Skip(1))
            {
                var columns = line.Split('\t')
                    .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
                    .ToArray();
                var injection = new InjectionData(experiment, columns[3] * 1e-6)
                {
                    ActualCellConcentration = columns[0] * 1e-6,
                    ActualTitrantConcentration = columns[1] * 1e-6,
                    Ratio = columns[0] == 0 ? 0 : columns[1] / columns[0],
                };

                // The source header says Kcal/mol, but its parameter values
                // and numerical scale establish these values are cal/mol.
                injection.SetPeakArea(new FloatWithError(
                    Energy.ConvertToJoule(columns[7], EnergyUnit.Cal) * injection.InjectionMass,
                    0));
                experiment.Injections.Add(injection);
            }

            Assert.Equal(20, experiment.Injections.Count);
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
