using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Published model reproduction")]
public sealed class PublishedElifeTwoSiteSourceDataTests : IDisposable
{
    private const double OptimizerAgreementFraction = 0.01;
    private static readonly string[] Metals = { "mn", "cd" };
    private static readonly string[] Runs = { "first", "second", "third" };
    private readonly DilutionMethod originalDilutionMethod;
    private readonly bool originalReprocessSetting;

    public PublishedElifeTwoSiteSourceDataTests()
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

    public static IEnumerable<object[]> FixtureAndDilutionCases()
    {
        foreach (var fixture in FixtureNames())
        {
            yield return new object[] { fixture, DilutionMethod.MicroCal };
            yield return new object[] { fixture, DilutionMethod.Exponential };
        }
    }

    [Theory]
    [MemberData(nameof(FixtureAndDilutionCases))]
    public void PublishedEnthalpyLockedAffinityFitsConvergeAndOptimizersAgree(
        string fixtureName,
        DilutionMethod dilutionMethod)
    {
        // These low-c direct-DH fixtures do not identify all four free A/H
        // coordinates (covered explicitly below). Holding the worksheet's published
        // step enthalpies makes this an affinity-only source-data diagnostic.
        var lm = FitSingle(fixtureName, dilutionMethod, SolverAlgorithm.LevenbergMarquardt, lockEnthalpies: true);
        var nm = FitSingle(fixtureName, dilutionMethod, SolverAlgorithm.NelderMead, lockEnthalpies: true);

        AssertSuccessfulOrderedInteriorAffinityFit(lm);
        AssertSuccessfulOrderedInteriorAffinityFit(nm);
        Assert.InRange(RelativeDifference(lm.Kd1Micromolar, nm.Kd1Micromolar), 0, OptimizerAgreementFraction);
        Assert.InRange(RelativeDifference(lm.Kd2Micromolar, nm.Kd2Micromolar), 0, OptimizerAgreementFraction);

        Console.WriteLine(
            $"{fixtureName} {dilutionMethod}: " +
            $"LM Kd={lm.Kd1Micromolar:G8}/{lm.Kd2Micromolar:G8} uM; " +
            $"NM Kd={nm.Kd1Micromolar:G8}/{nm.Kd2Micromolar:G8} uM");
    }

    [Fact]
    public void AllFourFreeCoordinatesExposePublishedFixtureUnderidentification()
    {
        var lmBoundaryFits = 0;
        var optimizerDisagreementObserved = false;

        foreach (var fixture in FixtureNames())
        {
            var lm = FitSingle(fixture, DilutionMethod.MicroCal, SolverAlgorithm.LevenbergMarquardt, lockEnthalpies: false);
            var nm = FitSingle(fixture, DilutionMethod.MicroCal, SolverAlgorithm.NelderMead, lockEnthalpies: false);

            Assert.True(lm.Success, lm.Message);
            Assert.True(nm.Success, nm.Message);
            Assert.True(lm.Kd1Micromolar < lm.Kd2Micromolar);
            Assert.True(nm.Kd1Micromolar < nm.Kd2Micromolar);

            if (lm.HasFittedParameterAtBoundary) lmBoundaryFits++;
            optimizerDisagreementObserved |=
                RelativeDifference(lm.Kd1Micromolar, nm.Kd1Micromolar) > 0.10
                || RelativeDifference(lm.Kd2Micromolar, nm.Kd2Micromolar) > 0.10;
        }

        // LM reproducibly collapses multiple runs against an enthalpy bound, and
        // LM and NM select materially different affinity basins for at least one
        // run. NM boundary contact itself is not asserted: its termination point
        // can remain just inside the broad enthalpy bound without changing this
        // identifiability diagnosis.
        // This is why the positive diagnostic above states its locked-enthalpy
        // scope rather than hiding it.
        Assert.True(lmBoundaryFits >= 4, $"Expected at least four LM boundary fits, observed {lmBoundaryFits}.");
        Assert.True(optimizerDisagreementObserved);
    }

    [Fact]
    public void LockedEnthalpyTriplicateMeansMatchCadmiumButNotManganesePublishedRanges()
    {
        var manganese = FitMetalTriplicateIndividually("mn");
        var cadmium = FitMetalTriplicateIndividually("cd");

        Assert.InRange(cadmium.Kd1Micromolar, 40, 70);
        Assert.InRange(cadmium.Kd2Micromolar, 200, 240);

        Assert.InRange(manganese.Kd1Micromolar, 145, 170);
        Assert.True(manganese.Kd1Micromolar < 160);
        Assert.True(manganese.Kd2Micromolar > 2490);

        Console.WriteLine(
            $"Locked-H triplicate means: Mn Kd={manganese.Kd1Micromolar:G8}/{manganese.Kd2Micromolar:G8} uM; " +
            $"Cd Kd={cadmium.Kd1Micromolar:G8}/{cadmium.Kd2Micromolar:G8} uM");
    }

    [Theory]
    [InlineData("mn")]
    [InlineData("cd")]
    public void PublishedEnthalpyLockedGlobalAffinityFamilyFitIsStableAndDocumentsTargetComparison(string metal)
    {
        var lm = FitGlobalTriplicate(metal, SolverAlgorithm.LevenbergMarquardt);
        var nm = FitGlobalTriplicate(metal, SolverAlgorithm.NelderMead);

        AssertSuccessfulOrderedInteriorAffinityFit(lm);
        AssertSuccessfulOrderedInteriorAffinityFit(nm);
        Assert.InRange(RelativeDifference(lm.Kd1Micromolar, nm.Kd1Micromolar), 0, OptimizerAgreementFraction);
        Assert.InRange(RelativeDifference(lm.Kd2Micromolar, nm.Kd2Micromolar), 0, OptimizerAgreementFraction);

        if (metal == "mn")
        {
            Assert.InRange(lm.Kd1Micromolar, 160, 220);
            Assert.True(lm.Kd2Micromolar > 2490);
        }
        else
        {
            Assert.InRange(lm.Kd1Micromolar, 40, 70);
            Assert.True(lm.Kd2Micromolar < 200);
        }

        Console.WriteLine(
            $"Global locked-H {metal}: LM Kd={lm.Kd1Micromolar:G8}/{lm.Kd2Micromolar:G8} uM; " +
            $"NM Kd={nm.Kd1Micromolar:G8}/{nm.Kd2Micromolar:G8} uM");
    }

    [Fact]
    public void SedphatPublishedTwoSiteTableRemainsAnOrientationDiagnostic()
    {
        AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
        var experiment = LoadExperiment("sedphat-itc-two-site.DH");

        Assert.Equal(21, experiment.Injections.Count);
        Assert.Equal(4.5e-6, experiment.CellConcentration, 12);
        Assert.Equal(50e-6, experiment.SyringeConcentration, 12);
        Assert.Equal(1414.1e-6, experiment.CellVolume, 12);
        Assert.False(experiment.Injections[0].Include);
        Assert.Equal(2e-6, experiment.Injections[0].Volume, 12);
        Assert.Equal(8e-6, experiment.Injections[1].Volume, 12);

        // The source has the macromolecular dimer in the syringe, which is outside
        // SequentialBindingSites' required cell-macromolecule orientation.
        Assert.True(experiment.SyringeConcentration > experiment.CellConcentration);
    }

    private static FitResult FitSingle(
        string fixtureName,
        DilutionMethod dilutionMethod,
        SolverAlgorithm solverAlgorithm,
        bool lockEnthalpies)
    {
        AppSettings.DilutionCalculationMethod = dilutionMethod;
        var experiment = LoadExperiment(fixtureName);
        AssertPublishedExperimentShape(experiment);

        var source = SourceFit(fixtureName);
        var model = new SequentialBindingSites(experiment);
        model.InitializeParameters(experiment);
        Assert.Equal(2, model.SiteCount);
        Assert.DoesNotContain(ParameterType.Nvalue1, model.Parameters.Table.Keys);
        Assert.DoesNotContain(ParameterType.Nvalue2, model.Parameters.Table.Keys);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, Math.Log10(1.0e6 / source.Kd1Micromolar));
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity2, Math.Log10(1.0e6 / source.Kd2Micromolar));
        model.Parameters.AddOrUpdateParameter(
            ParameterType.Enthalpy1,
            Energy.ConvertToJoule(source.Enthalpy1Kcal * 1000.0, EnergyUnit.Cal),
            lockEnthalpies);
        model.Parameters.AddOrUpdateParameter(
            ParameterType.Enthalpy2,
            Energy.ConvertToJoule(source.Enthalpy2Kcal * 1000.0, EnergyUnit.Cal),
            lockEnthalpies);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0.0, true);

        var solver = new Solver
        {
            Model = model,
            SolverAlgorithm = solverAlgorithm,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = false,
            MaxOptimizerIterations = 20000,
            Silent = true,
        };
        var convergence = solver.Solve();
        return FitResult.From(model.Parameters.Table, convergence.Success, convergence.Message, convergence.Loss);
    }

    private static FitResult FitGlobalTriplicate(string metal, SolverAlgorithm solverAlgorithm)
    {
        AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
        var experiments = Runs
            .Select(run => LoadExperiment(FixtureName(metal, run)))
            .ToArray();
        Assert.All(experiments, AssertPublishedExperimentShape);

        var session = AnalysisSessionState.CreateDefault();
        session.ModelType = AnalysisModel.SequentialBindingSites;
        session.IsGlobal = true;
        session.Global.ModelOptions[AttributeKey.SequentialSiteCount] =
            ExperimentAttribute.Int(AttributeKey.SequentialSiteCount, "Steps", 2);
        session.Global.SetSequentialConstraintFamily(ParameterType.Affinity1, VariableConstraint.SameForAll, 2);
        session.Global.SetSequentialConstraintFamily(ParameterType.Enthalpy1, VariableConstraint.None, 2);
        session.Global.ParameterOverrides[
            new ParameterOverrideKey(AnalysisModel.SequentialBindingSites, ParameterType.Affinity1)] =
            new ParameterOverride { Value = Math.Log10(metal == "mn" ? 1.0e6 / 190.0 : 1.0e6 / 55.0) };
        session.Global.ParameterOverrides[
            new ParameterOverrideKey(AnalysisModel.SequentialBindingSites, ParameterType.Affinity2)] =
            new ParameterOverride { Value = Math.Log10(metal == "mn" ? 1.0e6 / 1970.0 : 1.0e6 / 220.0) };

        var context = AnalysisBuilder.Build(session, experiments, reuseAttachedSolutionInitialValues: false);
        Assert.Equal(2, context.GlobalModelParameters.GlobalTable.Count);
        Assert.Equal(VariableConstraint.SameForAll,
            context.GlobalModelParameters.GetConstraintForParameter(ParameterType.Affinity1));
        Assert.Equal(VariableConstraint.SameForAll,
            context.GlobalModelParameters.GetConstraintForParameter(ParameterType.Affinity2));
        Assert.Equal(VariableConstraint.None,
            context.GlobalModelParameters.GetConstraintForParameter(ParameterType.Enthalpy1));
        Assert.Equal(VariableConstraint.None,
            context.GlobalModelParameters.GetConstraintForParameter(ParameterType.Enthalpy2));

        for (var index = 0; index < context.GlobalModel.Models.Count; index++)
        {
            var model = Assert.IsType<SequentialBindingSites>(context.GlobalModel.Models[index]);
            Assert.Equal(2, model.SiteCount);
            var source = SourceFit(FixtureName(metal, Runs[index]));
            model.Parameters.AddOrUpdateParameter(
                ParameterType.Enthalpy1,
                Energy.ConvertToJoule(source.Enthalpy1Kcal * 1000.0, EnergyUnit.Cal),
                true);
            model.Parameters.AddOrUpdateParameter(
                ParameterType.Enthalpy2,
                Energy.ConvertToJoule(source.Enthalpy2Kcal * 1000.0, EnergyUnit.Cal),
                true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0.0, true);
        }

        context.FinalizeForSolver();
        Assert.All(context.GlobalModel.Models, model =>
        {
            Assert.True(model.Parameters.Table[ParameterType.Enthalpy1].IsLocked);
            Assert.True(model.Parameters.Table[ParameterType.Enthalpy2].IsLocked);
            Assert.True(model.Parameters.Table[ParameterType.Offset].IsLocked);
            Assert.Equal(0, model.Parameters.Table[ParameterType.Offset].Value, 12);
        });

        var solver = Assert.IsType<GlobalSolver>(context.CreateSolver());
        solver.SolverAlgorithm = solverAlgorithm;
        solver.ErrorEstimationMethod = ErrorEstimationMethod.None;
        solver.UseErrorWeightedFitting = false;
        solver.MaxOptimizerIterations = 20000;
        solver.Silent = true;
        var convergence = solver.Solve();
        return FitResult.From(
            context.GlobalModelParameters.GlobalTable,
            convergence.Success,
            convergence.Message,
            convergence.Loss);
    }

    private static (double Kd1Micromolar, double Kd2Micromolar) FitMetalTriplicateIndividually(string metal)
    {
        var fits = Runs.Select(run => FitSingle(
            FixtureName(metal, run),
            DilutionMethod.MicroCal,
            SolverAlgorithm.LevenbergMarquardt,
            lockEnthalpies: true)).ToArray();
        Assert.All(fits, AssertSuccessfulOrderedInteriorAffinityFit);
        return (fits.Average(fit => fit.Kd1Micromolar), fits.Average(fit => fit.Kd2Micromolar));
    }

    private static void AssertSuccessfulOrderedInteriorAffinityFit(FitResult fit)
    {
        Assert.True(fit.Success, fit.Message);
        Assert.True(double.IsFinite(fit.Kd1Micromolar));
        Assert.True(double.IsFinite(fit.Kd2Micromolar));
        Assert.True(fit.Kd1Micromolar < fit.Kd2Micromolar,
            $"Expected Kd1 < Kd2, observed {fit.Kd1Micromolar:G8} and {fit.Kd2Micromolar:G8} uM.");
        Assert.True(fit.AffinitiesAreInterior);
    }

    private static void AssertPublishedExperimentShape(ExperimentData experiment)
    {
        Assert.Equal(20, experiment.Injections.Count);
        Assert.Equal(25e-6, experiment.CellConcentration, 12);
        Assert.Equal(6e-3, experiment.SyringeConcentration, 12);
        Assert.Equal(203.9e-6, experiment.CellVolume, 12);
        Assert.False(experiment.Injections[0].Include);
        Assert.Equal(19, experiment.Injections.Count(injection => injection.Include));
    }

    private static IEnumerable<string> FixtureNames() =>
        Metals.SelectMany(metal => Runs.Select(run => FixtureName(metal, run)));

    private static string FixtureName(string metal, string run) =>
        $"elife2023-wt-{metal}-twosite-{run}-run.DH";

    private static double RelativeDifference(double first, double second) =>
        Math.Abs(first - second) / ((Math.Abs(first) + Math.Abs(second)) * 0.5);

    private static ExperimentData LoadExperiment(string fixtureName) => IntegratedHeatReader.ReadFile(Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "PublishedBenchmarks",
        fixtureName));

    private static SourceParameters SourceFit(string fixtureName) => fixtureName switch
    {
        "elife2023-wt-mn-twosite-first-run.DH" => new SourceParameters(220, 3.2, 961, 5.4),
        "elife2023-wt-mn-twosite-second-run.DH" => new SourceParameters(125, 5.7, 2700, 17.1),
        "elife2023-wt-mn-twosite-third-run.DH" => new SourceParameters(220, 8.0, 2250, 13.7),
        "elife2023-wt-cd-twosite-first-run.DH" => new SourceParameters(85, -4.5, 260, -2.6),
        "elife2023-wt-cd-twosite-second-run.DH" => new SourceParameters(50, -4.7, 220, -4.1),
        "elife2023-wt-cd-twosite-third-run.DH" => new SourceParameters(30, -2.9, 180, -4.7),
        _ => throw new ArgumentOutOfRangeException(nameof(fixtureName), fixtureName, "Unknown published fixture."),
    };

    private sealed class SourceParameters
    {
        public SourceParameters(double kd1Micromolar, double enthalpy1Kcal, double kd2Micromolar, double enthalpy2Kcal)
        {
            Kd1Micromolar = kd1Micromolar;
            Enthalpy1Kcal = enthalpy1Kcal;
            Kd2Micromolar = kd2Micromolar;
            Enthalpy2Kcal = enthalpy2Kcal;
        }

        public double Kd1Micromolar { get; }
        public double Enthalpy1Kcal { get; }
        public double Kd2Micromolar { get; }
        public double Enthalpy2Kcal { get; }
    }

    private sealed class FitResult
    {
        public bool Success { get; private set; }
        public string Message { get; private set; }
        public double Loss { get; private set; }
        public double Kd1Micromolar { get; private set; }
        public double Kd2Micromolar { get; private set; }
        public bool AffinitiesAreInterior { get; private set; }
        public bool HasFittedParameterAtBoundary { get; private set; }

        public static FitResult From(
            IReadOnlyDictionary<ParameterType, Parameter> table,
            bool success,
            string message,
            double loss)
        {
            var affinity1 = table[ParameterType.Affinity1];
            var affinity2 = table[ParameterType.Affinity2];
            return new FitResult
            {
                Success = success,
                Message = message,
                Loss = loss,
                Kd1Micromolar = 1.0e6 / Math.Pow(10, affinity1.Value),
                Kd2Micromolar = 1.0e6 / Math.Pow(10, affinity2.Value),
                AffinitiesAreInterior = IsInterior(affinity1) && IsInterior(affinity2),
                HasFittedParameterAtBoundary = table.Values
                    .Where(parameter => parameter.IsFitted)
                    .Any(parameter => !IsInterior(parameter)),
            };
        }

        private static bool IsInterior(Parameter parameter)
        {
            if (parameter.Limits == null || parameter.Limits.Length != 2) return true;
            var margin = Math.Max(1e-10, Math.Abs(parameter.Limits[1] - parameter.Limits[0]) * 1e-7);
            return parameter.Value > parameter.Limits[0] + margin
                && parameter.Value < parameter.Limits[1] - margin;
        }
    }

    private sealed class FixedEnergyUnitPromptService : IImportPromptService
    {
        private readonly EnergyUnit unit;

        public FixedEnergyUnitPromptService(EnergyUnit unit) => this.unit = unit;

        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse) =>
            new(unit, useForRemainingFilesInQueue: false, isCancelled: false);
    }
}
