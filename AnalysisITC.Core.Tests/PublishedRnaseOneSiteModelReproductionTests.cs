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
    public sealed class ArchivedRnaseIntegratedHeatRegressionTests : IDisposable
    {
        const double SourceN = 1.02;
        const double SourceAssociationConstant = 5.59e4;
        const double SourceEnthalpyCalPerMole = -1.354e4;
        const double RelativeTolerance = 0.03;

        // The archived Data_NDH table retains these pre-fit concentration and
        // heat-normalization values. The adjacent fitting model uses the
        // concentrations declared below instead.
        const double ExportCellConcentrationMicroM = 61;
        const double ExportSyringeConcentrationMicroM = 2250;
        const double FitCellConcentrationMicroM = 651;
        const double FitSyringeConcentrationMicroM = 21160;

        readonly DilutionMethod originalDilutionMethod;

        public ArchivedRnaseIntegratedHeatRegressionTests()
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
        public void OneSetOfSitesFitsArchivedRnaseIntegratedHeatsNearSourceFit(
            DilutionMethod dilutionMethod,
            SolverAlgorithm algorithm)
        {
            AppSettings.DilutionCalculationMethod = dilutionMethod;
            var experiment = LoadExperiment();
            var model = new OneSetOfSites(experiment);
            model.InitializeParameters(experiment);

            // Fit.txt reports these values for its unavailable Data1_NDH
            // input. Use them only as an initial point for this archived-data
            // regression, not as a claim of exact source-data reproduction.
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, SourceN);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, Energy.ConvertToJoule(SourceEnthalpyCalPerMole, EnergyUnit.Cal));
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, Math.Log10(SourceAssociationConstant));
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0.0);

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
            AssertRelativeAgreement("N", SourceN, model.Parameters.Table[ParameterType.Nvalue1].Value);
            AssertRelativeAgreement(
                "Ka",
                SourceAssociationConstant,
                Math.Pow(10.0, model.Parameters.Table[ParameterType.Affinity1].Value));
            AssertRelativeAgreement(
                "dH",
                Energy.ConvertToJoule(SourceEnthalpyCalPerMole, EnergyUnit.Cal),
                model.Parameters.Table[ParameterType.Enthalpy1].Value);
        }

        static ExperimentData LoadExperiment()
        {
            var experiment = new ExperimentData("bbr2020-rnase.ndh")
            {
                CellConcentration = new(FitCellConcentrationMicroM * 1e-6),
                SyringeConcentration = new(FitSyringeConcentrationMicroM * 1e-6),
                CellVolume = 1345e-6,
                TargetTemperature = 25,
            };

            var lines = File.ReadAllLines(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "PublishedBenchmarks",
                "bbr2020-rnase.ndh"));

            foreach (var line in lines.Skip(1))
            {
                var columns = line.Split('\t')
                    .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
                    .ToArray();
                var injection = new InjectionData(experiment, columns[3] * 1e-6);

                // Data_NDH divides the integrated heat by its stale 2.25 mM
                // syringe value. Restore that normalization before fitting at
                // the 21.16 mM concentration used in model_func.m.
                injection.SetPeakArea(new FloatWithError(
                    Energy.ConvertToJoule(columns[7], EnergyUnit.Cal)
                    * injection.InjectionMass
                    * (ExportSyringeConcentrationMicroM / FitSyringeConcentrationMicroM),
                    0));
                experiment.Injections.Add(injection);
            }

            // Generate the injection-state concentrations using the selected
            // FT-ITC dilution convention. The source's exported trace agrees
            // with the MicroCal convention after its concentrations are scaled.
            RawDataReader.ProcessInjections(experiment);
            Assert.Equal(21, experiment.Injections.Count);
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
