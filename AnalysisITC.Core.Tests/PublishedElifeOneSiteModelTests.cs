using System;
using System.IO;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    [Collection("Published model reproduction")]
    public sealed class PublishedElifeOneSiteModelTests : IDisposable
    {
        // Origin's fit note embedded in G223W_Mn_onesite_first_run.OPJ.
        const double OriginAssociationConstant = 2.17e3;
        const double OriginEnthalpyCalPerMole = 5847;
        // The second worksheet records K = 2.31e3 M^-1 and dH = +3048
        // cal/mol. Its DH column is retained exactly; fitting those direct
        // integrated heats unweighted gives this separate FT-ITC regression.
        // It is deliberately not conflated with Origin's normalized-NDH fit.
        const double SecondRunMicroCalAssociationConstant = 3026.9;
        const double SecondRunMicroCalEnthalpyCalPerMole = 2906.0;
        const double SecondRunExponentialAssociationConstant = 3038.6;
        const double SecondRunExponentialEnthalpyCalPerMole = 2898.9;
        const double RelativeTolerance = 0.005;
        const double DirectHeatRegressionTolerance = 0.005;

        readonly DilutionMethod originalDilutionMethod;
        readonly bool originalReprocessSetting;

        public PublishedElifeOneSiteModelTests()
        {
            originalDilutionMethod = AppSettings.DilutionCalculationMethod;
            originalReprocessSetting = AppSettings.ReprocessIntegratedHeatDataOnLoad;
            AppSettings.ReprocessIntegratedHeatDataOnLoad = false;
            IntegratedHeatReader.BeginImportQueue();
            PlatformServices.RegisterImportPromptService(new FixedEnergyUnitPromptService(EnergyUnit.MicroCal));
        }

        public void Dispose()
        {
            IntegratedHeatReader.EndImportQueue();
            PlatformServices.RegisterImportPromptService(null);
            AppSettings.DilutionCalculationMethod = originalDilutionMethod;
            AppSettings.ReprocessIntegratedHeatDataOnLoad = originalReprocessSetting;
        }

        [Theory]
        [InlineData(DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(DilutionMethod.MicroCal, SolverAlgorithm.NelderMead)]
        [InlineData(DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(DilutionMethod.Exponential, SolverAlgorithm.NelderMead)]
        public void OneSetOfSitesFitsPublishedIntegratedHeatsWithFixedN(
            DilutionMethod dilutionMethod,
            SolverAlgorithm algorithm)
        {
            AppSettings.DilutionCalculationMethod = dilutionMethod;

            var experiment = LoadExperiment("elife2023-g223w-mn-onesite-first-run.DH");

            Assert.Equal(20, experiment.Injections.Count);
            Assert.Equal(25e-6, experiment.CellConcentration, 12);
            Assert.Equal(6e-3, experiment.SyringeConcentration, 12);
            Assert.Equal(203.9e-6, experiment.CellVolume, 12);
            Assert.False(experiment.Injections[0].Include);

            var model = new OneSetOfSites(experiment);
            model.InitializeParameters(experiment);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1.0, true);
            model.Parameters.AddOrUpdateParameter(
                ParameterType.Enthalpy1,
                Energy.ConvertToJoule(OriginEnthalpyCalPerMole, EnergyUnit.Cal));
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, Math.Log10(OriginAssociationConstant));
            // Origin's one-site fit has no displacement/offset term.
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0.0, true);

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
            Assert.True(model.Parameters.Table[ParameterType.Offset].IsLocked);

            AssertRelativeAgreement(
                "Ka",
                OriginAssociationConstant,
                Math.Pow(10.0, model.Parameters.Table[ParameterType.Affinity1].Value));
            AssertRelativeAgreement(
                "dH",
                Energy.ConvertToJoule(OriginEnthalpyCalPerMole, EnergyUnit.Cal),
                model.Parameters.Table[ParameterType.Enthalpy1].Value);
        }

        [Theory]
        [InlineData(DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt, SecondRunMicroCalAssociationConstant, SecondRunMicroCalEnthalpyCalPerMole)]
        [InlineData(DilutionMethod.MicroCal, SolverAlgorithm.NelderMead, SecondRunMicroCalAssociationConstant, SecondRunMicroCalEnthalpyCalPerMole)]
        [InlineData(DilutionMethod.Exponential, SolverAlgorithm.LevenbergMarquardt, SecondRunExponentialAssociationConstant, SecondRunExponentialEnthalpyCalPerMole)]
        [InlineData(DilutionMethod.Exponential, SolverAlgorithm.NelderMead, SecondRunExponentialAssociationConstant, SecondRunExponentialEnthalpyCalPerMole)]
        public void SecondRunIntegratedHeatsHaveStableFixedNRegression(
            DilutionMethod dilutionMethod,
            SolverAlgorithm algorithm,
            double expectedAssociationConstant,
            double expectedEnthalpyCalPerMole)
        {
            AppSettings.DilutionCalculationMethod = dilutionMethod;

            var experiment = LoadExperiment("elife2023-g223w-mn-onesite-second-run.DH");

            Assert.Equal(20, experiment.Injections.Count);
            Assert.Equal(25e-6, experiment.CellConcentration, 12);
            Assert.Equal(6e-3, experiment.SyringeConcentration, 12);
            Assert.Equal(203.9e-6, experiment.CellVolume, 12);
            Assert.False(experiment.Injections[0].Include);

            var model = new OneSetOfSites(experiment);
            model.InitializeParameters(experiment);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1.0, true);
            model.Parameters.AddOrUpdateParameter(
                ParameterType.Enthalpy1,
                Energy.ConvertToJoule(3048, EnergyUnit.Cal));
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, Math.Log10(2310));
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0.0, true);

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
            Assert.True(model.Parameters.Table[ParameterType.Offset].IsLocked);
            AssertRelativeAgreement(
                "second-run direct-DH Ka",
                expectedAssociationConstant,
                Math.Pow(10.0, model.Parameters.Table[ParameterType.Affinity1].Value),
                DirectHeatRegressionTolerance);
            AssertRelativeAgreement(
                "second-run direct-DH dH",
                Energy.ConvertToJoule(expectedEnthalpyCalPerMole, EnergyUnit.Cal),
                model.Parameters.Table[ParameterType.Enthalpy1].Value,
                DirectHeatRegressionTolerance);
        }

        static ExperimentData LoadExperiment(string fixtureName)
        {
            return IntegratedHeatReader.ReadFile(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PublishedBenchmarks",
                fixtureName));
        }

        static void AssertRelativeAgreement(string parameter, double expected, double actual)
        {
            AssertRelativeAgreement(parameter, expected, actual, RelativeTolerance);
        }

        static void AssertRelativeAgreement(string parameter, double expected, double actual, double tolerance)
        {
            var relativeDifference = Math.Abs((actual - expected) / expected);
            Assert.True(
                relativeDifference <= tolerance,
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
