using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    /// <summary>
    /// Full-fit checks against a small, deterministic reference generated from
    /// the mass balances in this file.  The reference deliberately does not
    /// call a production model while generating its heats.
    /// </summary>
    [Collection("Solver events")]
    public sealed class IndependentBindingReferenceTests : IDisposable
    {
        readonly PreferencesState originalPreferences = PreferencesState.FromSettings();

        public IndependentBindingReferenceTests() => PreferencesState.Defaults().ApplyToSettings();
        public void Dispose() => originalPreferences.ApplyToSettings();

        const double CellConcentration = 20e-6;
        const double SyringeConcentration = 1e-3;
        const double CellVolume = 1.4e-3;
        const double InjectionVolume = 1.5e-6;
        const int InjectionCount = 32;

        [Theory]
        [InlineData(SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(SolverAlgorithm.NelderMead)]
        public void TwoIndependentSiteReferenceRecoversThermodynamicsWithKnownStoichiometries(SolverAlgorithm algorithm)
        {
            const double n1 = 0.75;
            const double n2 = 1.45;
            const double logK1 = 7.20;
            const double logK2 = 5.10;
            const double h1 = -26000;
            const double h2 = 12000;
            const double offset = 0.0;

            var experiment = CreateExperiment(DilutionMethod.Exponential);
            SetConcentrations(experiment, DilutionMethod.Exponential);
            WriteTwoSiteReferenceHeats(experiment, n1, n2, logK1, logK2, h1, h2, offset);

            var model = new TwoSetsOfSites(experiment);
            model.InitializeParameters(experiment);
            model.Parameters.Table[ParameterType.Nvalue1].Update(n1, true);
            model.Parameters.Table[ParameterType.Nvalue2].Update(n2, true);
            model.Parameters.Table[ParameterType.Affinity1].Update(algorithm == SolverAlgorithm.NelderMead ? 7.10 : 6.85);
            model.Parameters.Table[ParameterType.Affinity2].Update(algorithm == SolverAlgorithm.NelderMead ? 5.20 : 5.45);
            model.Parameters.Table[ParameterType.Enthalpy1].Update(algorithm == SolverAlgorithm.NelderMead ? -25000 : -22000);
            model.Parameters.Table[ParameterType.Enthalpy2].Update(algorithm == SolverAlgorithm.NelderMead ? 11000 : 9000);
            model.Parameters.Table[ParameterType.Offset].Update(offset, true);

            var convergence = Solve(model, algorithm, weighted: true);

            Assert.True(convergence.Success, convergence.Message);
            AssertClose(n1, model.Parameters.Table[ParameterType.Nvalue1].Value, 3e-4);
            AssertClose(n2, model.Parameters.Table[ParameterType.Nvalue2].Value, 3e-4);
            AssertClose(logK1, model.Parameters.Table[ParameterType.Affinity1].Value, 3e-4);
            AssertClose(logK2, model.Parameters.Table[ParameterType.Affinity2].Value, 3e-4);
            AssertRelative(h1, model.Parameters.Table[ParameterType.Enthalpy1].Value, 3e-4);
            AssertRelative(h2, model.Parameters.Table[ParameterType.Enthalpy2].Value, 3e-4);
            AssertClose(offset, model.Parameters.Table[ParameterType.Offset].Value, 1e-3);
            Assert.Equal(0.0, model.LossFunction(model.Parameters.GetFittedParameterArray(), errorweighted: false), 12);
        }

        [Theory]
        [InlineData(SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(SolverAlgorithm.NelderMead)]
        public void SaturatedTwoIndependentSiteReferenceRecoversFullFit(SolverAlgorithm algorithm)
        {
            const double n1 = 0.65;
            const double n2 = 1.55;
            const double logK1 = 7.35;
            const double logK2 = 5.15;
            const double h1 = -25000;
            const double h2 = 13000;
            const double offset = 1800;

            var experiment = CreateExperiment(DilutionMethod.Exponential, 240, InjectionVolume);
            SetConcentrations(experiment, DilutionMethod.Exponential);
            WriteTwoSiteReferenceHeats(experiment, n1, n2, logK1, logK2, h1, h2, offset);

            var model = new TwoSetsOfSites(experiment);
            model.InitializeParameters(experiment);
            model.Parameters.Table[ParameterType.Nvalue1].Update(0.70);
            model.Parameters.Table[ParameterType.Nvalue2].Update(1.50);
            model.Parameters.Table[ParameterType.Affinity1].Update(7.30);
            model.Parameters.Table[ParameterType.Affinity2].Update(5.20);
            model.Parameters.Table[ParameterType.Enthalpy1].Update(-24500);
            model.Parameters.Table[ParameterType.Enthalpy2].Update(13000);
            model.Parameters.Table[ParameterType.Offset].Update(1700.0);

            var convergence = Solve(model, algorithm, weighted: true);

            Assert.True(convergence.Success, convergence.Message);
            var fittedN1 = model.Parameters.Table[ParameterType.Nvalue1].Value;
            var fittedN2 = model.Parameters.Table[ParameterType.Nvalue2].Value;
            var fittedLogK1 = model.Parameters.Table[ParameterType.Affinity1].Value;
            var fittedLogK2 = model.Parameters.Table[ParameterType.Affinity2].Value;
            var fittedH1 = model.Parameters.Table[ParameterType.Enthalpy1].Value;
            var fittedH2 = model.Parameters.Table[ParameterType.Enthalpy2].Value;
            var direct = Close(n1, fittedN1, 2e-3)
                && Close(n2, fittedN2, 2e-3)
                && Close(logK1, fittedLogK1, 3e-3)
                && Close(logK2, fittedLogK2, 3e-3)
                && RelativeClose(h1, fittedH1, 3e-3)
                && RelativeClose(h2, fittedH2, 3e-3);
            var swapped = Close(n1, fittedN2, 2e-3)
                && Close(n2, fittedN1, 2e-3)
                && Close(logK1, fittedLogK2, 3e-3)
                && Close(logK2, fittedLogK1, 3e-3)
                && RelativeClose(h1, fittedH2, 3e-3)
                && RelativeClose(h2, fittedH1, 3e-3);
            Assert.True(direct || swapped,
                $"Two-site fit was neither direct nor label-swapped: N=({fittedN1:G17},{fittedN2:G17}), logK=({fittedLogK1:G17},{fittedLogK2:G17}), H=({fittedH1:G17},{fittedH2:G17}).");
            AssertClose(offset, model.Parameters.Table[ParameterType.Offset].Value, 2.0);
            Assert.Equal(0.0, model.LossFunction(model.Parameters.GetFittedParameterArray(), errorweighted: false), 12);
        }

        [Theory]
        [InlineData(SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(SolverAlgorithm.NelderMead)]
        public void CompetitiveReferenceRecoversTargetFromPreEquilibratedCompetitor(SolverAlgorithm algorithm)
        {
            const double n = 1.15;
            const double logKTarget = 7.00;
            const double targetEnthalpy = -18000;
            const double offset = 0.0;
            const double competitorConcentration = 12e-6;
            const double competitorLogK = 6.20;
            const double competitorEnthalpy = -8000;

            var experiment = CreateExperiment(DilutionMethod.MicroCal);
            SetConcentrations(experiment, DilutionMethod.MicroCal);
            WriteCompetitiveReferenceHeats(
                experiment, n, logKTarget, targetEnthalpy, offset,
                competitorConcentration, competitorLogK, competitorEnthalpy);

            var model = new CompetitiveBinding(experiment);
            model.InitializeParameters(experiment);
            model.Parameters.Table[ParameterType.Nvalue1].Update(n, true);
            model.Parameters.Table[ParameterType.Affinity1].Update(6.70);
            model.Parameters.Table[ParameterType.Enthalpy1].Update(-15000);
            model.Parameters.Table[ParameterType.Offset].Update(offset, true);
            model.ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue =
                new FloatWithError(competitorConcentration);
            model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue =
                new FloatWithError(competitorLogK);
            model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue =
                new FloatWithError(competitorEnthalpy);

            var convergence = Solve(model, algorithm, weighted: true);

            Assert.True(convergence.Success, convergence.Message);
            AssertClose(n, model.Parameters.Table[ParameterType.Nvalue1].Value, 4e-4);
            AssertClose(logKTarget, model.Parameters.Table[ParameterType.Affinity1].Value, 1e-3);
            AssertRelative(targetEnthalpy, model.Parameters.Table[ParameterType.Enthalpy1].Value, 4e-4);
            AssertClose(offset, model.Parameters.Table[ParameterType.Offset].Value, 1e-3);
            Assert.Equal(0.0, model.LossFunction(model.Parameters.GetFittedParameterArray(), errorweighted: false), 12);
        }

        [Theory]
        [InlineData(SolverAlgorithm.LevenbergMarquardt)]
        [InlineData(SolverAlgorithm.NelderMead)]
        public async Task SaturatedCompetitiveReferenceRecoversFullTargetFit(SolverAlgorithm algorithm)
        {
            const double n = 1.15;
            const double logKTarget = 7.00;
            const double targetEnthalpy = -18000;
            const double offset = 1800;
            const double competitorConcentration = 12e-6;
            const double competitorLogK = 6.20;
            const double competitorEnthalpy = -8000;

            var experiment = CreateExperiment(DilutionMethod.Exponential, 240, InjectionVolume);
            SetConcentrations(experiment, DilutionMethod.Exponential);
            WriteCompetitiveReferenceHeats(
                experiment, n, logKTarget, targetEnthalpy, offset,
                competitorConcentration, competitorLogK, competitorEnthalpy);

            var model = new CompetitiveBinding(experiment);
            model.InitializeParameters(experiment);
            model.Parameters.Table[ParameterType.Nvalue1].Update(0.95);
            model.Parameters.Table[ParameterType.Affinity1].Update(6.70);
            model.Parameters.Table[ParameterType.Enthalpy1].Update(-15000);
            model.Parameters.Table[ParameterType.Offset].Update(0.0);
            model.ModelOptions[AttributeKey.PreboundLigandConc].ParameterValue =
                new FloatWithError(competitorConcentration);
            model.ModelOptions[AttributeKey.PreboundLigandAffinity].ParameterValue =
                new FloatWithError(competitorLogK);
            model.ModelOptions[AttributeKey.PreboundLigandEnthalpy].ParameterValue =
                new FloatWithError(competitorEnthalpy);
            experiment.Model = model;

            var convergence = Solve(model, algorithm, weighted: true);

            Assert.True(convergence.Success, convergence.Message);
            AssertClose(n, model.Parameters.Table[ParameterType.Nvalue1].Value, 2e-3);
            AssertClose(logKTarget, model.Parameters.Table[ParameterType.Affinity1].Value, 3e-3);
            AssertRelative(targetEnthalpy, model.Parameters.Table[ParameterType.Enthalpy1].Value, 3e-3);
            AssertClose(offset, model.Parameters.Table[ParameterType.Offset].Value, 2.0);
            Assert.Equal(0.0, model.LossFunction(model.Parameters.GetFittedParameterArray(), errorweighted: false), 12);

            using var package = new MemoryStream();
            await FTXTCWriter.WriteStream(package, new[] { experiment });
            package.Position = 0;
            var restored = Assert.Single((await FTXTCReader.ReadStream(package)).OfType<ExperimentData>());
            var restoredSolution = Assert.IsType<CompetitiveBinding.ModelSolution>(restored.Solution);
            Assert.Equal(AnalysisModel.CompetitiveBinding, restoredSolution.Model.ModelType);
            AssertClose(model.Parameters.Table[ParameterType.Nvalue1].Value,
                restoredSolution.Parameters[ParameterType.Nvalue1].Value, 1e-12);
            AssertClose(model.Parameters.Table[ParameterType.Affinity1].Value,
                restoredSolution.Parameters[ParameterType.Affinity1].Value, 1e-12);
            AssertRelative(model.Parameters.Table[ParameterType.Enthalpy1].Value,
                restoredSolution.Parameters[ParameterType.Enthalpy1].Value, 1e-12);
        }

        static ExperimentData CreateExperiment(
            DilutionMethod dilutionMethod,
            int injectionCount = InjectionCount,
            double injectionVolume = InjectionVolume)
        {
            var experiment = new ExperimentData($"independent-reference-{dilutionMethod}.itc")
            {
                CellConcentration = new FloatWithError(CellConcentration),
                SyringeConcentration = new FloatWithError(SyringeConcentration),
                CellVolume = CellVolume,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };

            for (var i = 0; i < injectionCount; i++)
            {
                var injection = new InjectionData(
                    experiment,
                    i,
                    injectionVolume,
                    SyringeConcentration * injectionVolume,
                    include: true);
                experiment.Injections.Add(injection);
            }

            return experiment;
        }

        static void SetConcentrations(ExperimentData experiment, DilutionMethod method)
        {
            var cumulative = 0.0;
            foreach (var injection in experiment.Injections)
            {
                cumulative += injection.Volume;
                var u = cumulative / CellVolume;
                var retention = method == DilutionMethod.Exponential
                    ? Math.Exp(-u)
                    : (1.0 - u / 2.0) / (1.0 + u / 2.0);
                var titrant = method == DilutionMethod.Exponential
                    ? SyringeConcentration * (1.0 - retention)
                    : SyringeConcentration * u / (1.0 + u / 2.0);
                injection.ActualCellConcentration = CellConcentration * retention;
                injection.ActualTitrantConcentration = titrant;
                injection.Ratio = titrant / injection.ActualCellConcentration;
            }
        }

        static void WriteTwoSiteReferenceHeats(
            ExperimentData experiment,
            double n1, double n2, double logK1, double logK2,
            double h1, double h2, double offset)
        {
            var kd1 = 1.0 / Math.Pow(10, logK1);
            var kd2 = 1.0 / Math.Pow(10, logK2);
            double previous = HeatContentTwoSites(CellConcentration, 0, n1, n2, kd1, kd2, h1, h2);
            foreach (var injection in experiment.Injections)
            {
                var current = HeatContentTwoSites(
                    injection.ActualCellConcentration,
                    injection.ActualTitrantConcentration,
                    n1, n2, kd1, kd2, h1, h2);
                var heat = current + injection.Volume / CellVolume * (current + previous) / 2.0 - previous;
                injection.SetPeakArea(new FloatWithError(heat + offset * injection.InjectionMass, 1e-10));
                previous = current;
            }
        }

        static void WriteCompetitiveReferenceHeats(
            ExperimentData experiment,
            double n, double logKTarget, double targetEnthalpy, double offset,
            double competitorConcentration, double competitorLogK, double competitorEnthalpy)
        {
            var targetK = Math.Pow(10, logKTarget);
            var competitorK = Math.Pow(10, competitorLogK);
            double previous = HeatContentCompetitive(
                CellConcentration, 0, n, targetK, targetEnthalpy,
                competitorConcentration, competitorK, competitorEnthalpy);
            foreach (var injection in experiment.Injections)
            {
                var current = HeatContentCompetitive(
                    injection.ActualCellConcentration,
                    injection.ActualTitrantConcentration,
                    n, targetK, targetEnthalpy,
                    competitorConcentration, competitorK, competitorEnthalpy);
                var heat = current + injection.Volume / CellVolume * (current + previous) / 2.0 - previous;
                injection.SetPeakArea(new FloatWithError(heat + offset * injection.InjectionMass, 1e-10));
                previous = current;
            }
        }

        static double HeatContentTwoSites(
            double cell, double titrant, double n1, double n2,
            double kd1, double kd2, double h1, double h2)
        {
            var free = FreeLigand(cell, titrant, kd1, kd2, n1, n2);
            var theta1 = free / (kd1 + free);
            var theta2 = free / (kd2 + free);
            return cell * CellVolume * (n1 * theta1 * h1 + n2 * theta2 * h2);
        }

        static double HeatContentCompetitive(
            double cell, double titrant, double n, double targetK, double targetH,
            double competitorTotal, double competitorK, double competitorH)
        {
            var sites = n * cell;
            var ratioA = titrant / sites;
            var ratioB = competitorTotal / (n * CellConcentration);
            var x = FreeSiteFraction(ratioA, ratioB, targetK * sites, competitorK * sites);
            var boundA = ratioA * x / (1.0 / (targetK * sites) + x);
            var boundB = ratioB * x / (1.0 / (competitorK * sites) + x);
            return CellVolume * sites * (targetH * boundA + competitorH * boundB);
        }

        static double FreeLigand(double cell, double titrant, double kd1, double kd2, double n1, double n2)
        {
            var lower = 0.0;
            var upper = titrant;
            for (var i = 0; i < 240; i++)
            {
                var free = lower + (upper - lower) / 2.0;
                var bound = cell * (n1 * free / (kd1 + free) + n2 * free / (kd2 + free));
                if (free + bound < titrant) lower = free;
                else upper = free;
            }
            return (lower + upper) / 2.0;
        }

        static double FreeSiteFraction(double ratioA, double ratioB, double cA, double cB)
        {
            var lower = 0.0;
            var upper = 1.0;
            for (var i = 0; i < 240; i++)
            {
                var x = lower + (upper - lower) / 2.0;
                var boundA = ratioA * x / (1.0 / cA + x);
                var boundB = ratioB * x / (1.0 / cB + x);
                var residual = x + boundA + boundB - 1.0;
                if (residual < 0) lower = x;
                else upper = x;
            }
            return (lower + upper) / 2.0;
        }

        static SolverConvergence Solve(Model model, SolverAlgorithm algorithm, bool weighted)
        {
            var solver = new Solver
            {
                Model = model,
                SolverAlgorithm = algorithm,
                ErrorEstimationMethod = ErrorEstimationMethod.None,
                UseErrorWeightedFitting = weighted,
                MaxOptimizerIterations = 12000,
                Silent = true,
            };
            return solver.Solve();
        }

        static void AssertClose(double expected, double actual, double tolerance) =>
            Assert.InRange(Math.Abs(actual - expected), 0, tolerance);

        static void AssertRelative(double expected, double actual, double tolerance)
        {
            var scale = Math.Max(Math.Abs(expected), 1e-20);
            Assert.InRange(Math.Abs(actual - expected) / scale, 0, tolerance);
        }

        static bool Close(double expected, double actual, double tolerance) =>
            Math.Abs(actual - expected) <= tolerance;

        static bool RelativeClose(double expected, double actual, double tolerance)
        {
            var scale = Math.Max(Math.Abs(expected), 1e-20);
            return Math.Abs(actual - expected) / scale <= tolerance;
        }
    }
}
