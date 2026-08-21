using System;
using System.IO;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [CollectionDefinition("Published model reproduction", DisableParallelization = true)]
    public sealed class PublishedModelReproductionCollectionDefinition
    {
    }

    [Collection("Published model reproduction")]
    public sealed class PublishedModelReproductionTests : IDisposable
    {
        const double ExpectedN = 0.973948;
        const double ExpectedAssociationConstant = 4.05476e7;
        const double ExpectedEnthalpyCalPerMole = -11566.9;
        const double RelativeTolerance = 0.02;

        readonly DilutionMethod originalDilutionMethod;

        public PublishedModelReproductionTests()
        {
            originalDilutionMethod = AppSettings.DilutionCalculationMethod;
            IntegratedHeatReader.BeginImportQueue();
            PlatformServices.RegisterImportPromptService(new FixedEnergyUnitPromptService(EnergyUnit.MicroCal));
        }

        public void Dispose()
        {
            IntegratedHeatReader.EndImportQueue();
            PlatformServices.RegisterImportPromptService(null);
            AppSettings.DilutionCalculationMethod = originalDilutionMethod;
        }

        [Theory]
        [InlineData(SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(SolverAlgorithm.NelderMead)]
        public void OneSetOfSitesReproducesPublishedPyTcParametersWithMicroCalDilution(SolverAlgorithm algorithm)
        {
            ReproducePublishedPyTcParameters(algorithm, DilutionMethod.MicroCal);
        }

        [Theory]
        [InlineData(SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(SolverAlgorithm.NelderMead)]
        public void OneSetOfSitesReproducesPublishedPyTcParametersWithExponentialDilution(SolverAlgorithm algorithm)
        {
            ReproducePublishedPyTcParameters(algorithm, DilutionMethod.Exponential);
        }

        void ReproducePublishedPyTcParameters(SolverAlgorithm algorithm, DilutionMethod dilutionMethod)
        {
            AppSettings.DilutionCalculationMethod = dilutionMethod;
            var experiment = IntegratedHeatReader.ReadFile(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PublishedBenchmarks",
                "pytc-ca-edta-tris-01.DH"));

            Assert.Equal(56, experiment.Injections.Count);
            experiment.Injections[0].Include = false;
            experiment.Injections[1].Include = false;

            var model = new OneSetOfSites(experiment);
            model.InitializeParameters(experiment);

            // Deliberately distinct from the published solution so the test
            // verifies parameter recovery rather than merely evaluating it.
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1.10);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, Energy.ConvertToJoule(-9.0, EnergyUnit.KCal));
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 7.0);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0.0);

            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = algorithm,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                MaxOptimizerIterations = 4000,
                Silent = true,
            };

            var convergence = solver.Solve();

            Assert.True(convergence.Success, convergence.Message);
            AssertRelativeAgreement(
                "N",
                ExpectedN,
                model.Parameters.Table[ParameterType.Nvalue1].Value);
            AssertRelativeAgreement(
                "Ka",
                ExpectedAssociationConstant,
                Math.Pow(10.0, model.Parameters.Table[ParameterType.Affinity1].Value));
            AssertRelativeAgreement(
                "dH",
                Energy.ConvertToJoule(ExpectedEnthalpyCalPerMole, EnergyUnit.Cal),
                model.Parameters.Table[ParameterType.Enthalpy1].Value);
        }

        static void AssertRelativeAgreement(string parameter, double expected, double actual)
        {
            var relativeDifference = Math.Abs((actual - expected) / expected);
            Assert.True(
                relativeDifference <= RelativeTolerance,
                $"{parameter}: expected {expected:G10}, fitted {actual:G10}, relative difference {relativeDifference:P3}");
        }

        sealed class FixedEnergyUnitPromptService : IImportPromptService
        {
            readonly EnergyUnit unit;

            public FixedEnergyUnitPromptService(EnergyUnit unit)
            {
                this.unit = unit;
            }

            public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse) =>
                new EnergyUnitPromptResult(unit, useForRemainingFilesInQueue: false, isCancelled: false);
        }
    }
}
