using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Platform;
using Xunit;
using Xunit.Abstractions;

namespace AnalysisITC.Core.Tests;

[Collection("Buffer subtraction real data")]
public sealed class NullComparisonDatasetTests : IDisposable
{
    readonly ITestOutputHelper output;
    readonly DilutionMethod originalDilutionMethod = AppSettings.DilutionCalculationMethod;

    public NullComparisonDatasetTests(ITestOutputHelper output)
    {
        this.output = output;
        AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
        IntegratedHeatReader.BeginImportQueue();
        PlatformServices.RegisterImportPromptService(new MicrocalPrompt());
    }

    public void Dispose()
    {
        IntegratedHeatReader.EndImportQueue();
        PlatformServices.RegisterImportPromptService(null);
        AppSettings.DilutionCalculationMethod = originalDilutionMethod;
    }

    [Theory]
    [InlineData("hepes-blank.DH", BindingAssessmentOutcome.NoBindingDetected)]
    [InlineData("hepes-01.DH", BindingAssessmentOutcome.BindingDetected)]
    public void RealHepesFixturesProduceIndependentOffsetAndBindingEvidence(
        string fixture, BindingAssessmentOutcome expectedAssessment)
    {
        FitRealHepes(ReadHepes(fixture), fixture, expectedAssessment);
    }

    [Fact]
    public void BufferCorrectedHepesBindingFixtureIsUsedByTheComparison()
    {
        var reference = ReadHepes("hepes-blank.DH");
        var experiment = ReadHepes("hepes-01.DH");
        var subtraction = BufferSubtractionCalculator.BuildModel(reference,
            new BufferSubtractionSettings(reference.UniqueID, BufferSubtractionMethod.MatchedInjection));
        foreach (var injection in experiment.Injections)
            injection.UpdateCorrectedPeakArea(subtraction);

        FitRealHepes(experiment, "hepes-01.DH matched-blank corrected",
            BindingAssessmentOutcome.BindingDetected);
    }

    void FitRealHepes(ExperimentData experiment, string label, BindingAssessmentOutcome expectedAssessment)
    {
        var model = new OneSetOfSites(experiment);
        model.InitializeParameters(experiment);
        model.Parameters.Table[ParameterType.Nvalue1].SetValue(1, true);
        model.Parameters.Table[ParameterType.Affinity1].SetValue(6.0, false);
        model.Parameters.Table[ParameterType.Enthalpy1].SetValue(-20_000, false);
        model.Parameters.Table[ParameterType.Offset].SetValue(0, false);

        var convergence = new Solver
        {
            Model = model,
            SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = false,
            MaxOptimizerIterations = 20_000,
            Silent = true,
        }.Solve();

        Assert.NotNull(model.Solution);
        NullModelComparisonCalculator.Calculate(model.Solution, false,
            SolverAlgorithm.LevenbergMarquardt, 5_000, 1);
        var comparison = Assert.IsType<NullModelComparison>(model.Solution.NullComparison);
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = model }));
        Assert.Equal(expectedAssessment, result.BindingAssessment.AutomaticOutcome);
        Assert.True(comparison.NullFitSucceeded, comparison.NullFitReason);
        Assert.Equal(convergence.Success, comparison.BindingFitSucceeded);
        AssertNullEvidenceMatchesCapturedPrediction(comparison);
        AssertOffsetMatchesAnalyticalSolution(comparison, weighted: false);
        if (comparison.DeltaAicc.HasValue)
            Assert.Equal(comparison.NullInformationCriteria.Aicc.Value - comparison.BindingInformationCriteria.Aicc.Value,
                comparison.DeltaAicc.Value, 10);
        WriteEvidence(label, comparison);
    }

    [Theory]
    [InlineData("binding-plus-offset", -22_000.0, 1_250.0, 0.13e-6, BindingAssessmentOutcome.BindingDetected)]
    [InlineData("weak-signal", -250.0, -680.0, 0.09e-6, BindingAssessmentOutcome.BindingDetected)]
    [InlineData("noise-level-signal", -1.0, -680.0, 0.09e-6, BindingAssessmentOutcome.NoBindingDetected)]
    public void DeterministicBindingAndWeakSignalsExerciseTheComparison(
        string label, double enthalpy, double injectedOffset, double noise,
        BindingAssessmentOutcome expectedAssessment)
    {
        var experiment = ReadHepes("hepes-01.DH");
        var generated = new OneSetOfSites(experiment);
        generated.InitializeParameters(experiment);
        generated.Parameters.Table[ParameterType.Nvalue1].SetValue(1, true);
        generated.Parameters.Table[ParameterType.Affinity1].SetValue(6.4, true);
        generated.Parameters.Table[ParameterType.Enthalpy1].SetValue(enthalpy, true);
        generated.Parameters.Table[ParameterType.Offset].SetValue(injectedOffset, true);

        foreach (var injection in experiment.Injections)
        {
            var deterministicNoise = ((injection.ID % 5) - 2) * noise;
            injection.SetPeakArea(new FloatWithError(
                generated.Evaluate(injection.ID) + deterministicNoise,
                0.3e-6 + injection.ID * 0.002e-6));
        }

        var fit = new OneSetOfSites(experiment);
        fit.InitializeParameters(experiment);
        fit.Parameters.Table[ParameterType.Nvalue1].SetValue(1, true);
        fit.Parameters.Table[ParameterType.Affinity1].SetValue(6.2, false);
        fit.Parameters.Table[ParameterType.Enthalpy1].SetValue(enthalpy * 0.9, false);
        fit.Parameters.Table[ParameterType.Offset].SetValue(0, false);
        var convergence = new Solver
        {
            Model = fit,
            SolverAlgorithm = SolverAlgorithm.LevenbergMarquardt,
            ErrorEstimationMethod = ErrorEstimationMethod.None,
            UseErrorWeightedFitting = true,
            MaxOptimizerIterations = 20_000,
            Silent = true,
        }.Solve();

        Assert.True(convergence.Success, convergence.Message);
        Assert.NotNull(fit.Solution);
        NullModelComparisonCalculator.Calculate(fit.Solution, true,
            SolverAlgorithm.LevenbergMarquardt, 5_000, 1);
        var comparison = Assert.IsType<NullModelComparison>(fit.Solution.NullComparison);
        var result = new AnalysisResult(GlobalSolution.FromSingleExperimentSolver(new Solver { Model = fit }));
        Assert.Equal(expectedAssessment, result.BindingAssessment.AutomaticOutcome);
        Assert.True(comparison.NullFitSucceeded, comparison.NullFitReason);
        Assert.True(comparison.BindingInformationCriteria?.IsAiccAvailable == true,
            comparison.BindingInformationCriteria?.AiccUnavailableReason);
        Assert.True(comparison.NullInformationCriteria?.IsAiccAvailable == true,
            comparison.NullInformationCriteria?.AiccUnavailableReason);
        AssertNullEvidenceMatchesCapturedPrediction(comparison);
        AssertOffsetMatchesAnalyticalSolution(comparison, weighted: true);
        Assert.Equal(comparison.NullInformationCriteria.Aicc.Value - comparison.BindingInformationCriteria.Aicc.Value,
            comparison.DeltaAicc.Value, 10);
        WriteEvidence(label, comparison);
    }

    [Fact]
    public async Task SavedJorsGlobalProjectProvidesPooledComparisonEvidence()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "FileTypeTests", "JORS Example Project.ftxtc"));
        var result = (await FTXTCReader.ReadStream(stream)).OfType<AnalysisResult>()
            .First(candidate => candidate.Solution.Solutions.Count > 1);

        NullModelComparisonCalculator.Calculate(result.Solution, result.Solution.UseWeightedFitting,
            SolverAlgorithm.LevenbergMarquardt, 5_000, 1);

        var comparison = Assert.IsType<NullModelComparison>(result.Solution.NullComparison);
        result.UpdateSolution(result.Solution);
        Assert.Equal(BindingAssessmentOutcome.BindingDetected, result.CollectionAssessmentOutcome);
        Assert.True(comparison.NullFitSucceeded, comparison.NullFitReason);
        Assert.Equal(result.Solution.Solutions.Count, comparison.Members.Count);
        Assert.Equal(comparison.Members.Sum(member => member.Points.Count(point => point.Included)),
            comparison.NullInformationCriteria.ObservationCount);
        AssertNullEvidenceMatchesCapturedPrediction(comparison);
        AssertOffsetMatchesAnalyticalSolution(comparison, result.Solution.UseWeightedFitting);
        Assert.Equal(comparison.NullInformationCriteria.Aicc.Value - comparison.BindingInformationCriteria.Aicc.Value,
            comparison.DeltaAicc.Value, 10);
        WriteEvidence("JORS global", comparison);
    }

    static void AssertNullEvidenceMatchesCapturedPrediction(NullModelComparison comparison)
    {
        var capturedResidualSum = comparison.Members.SelectMany(member => member.Points)
            .Where(point => point.Included)
            .Sum(point => Math.Pow(point.ObservedHeatJoules - point.PredictedHeatJoules, 2));
        var savedResidualSum = comparison.NullInformationCriteria.RawResidualSumOfSquares;
        Assert.InRange(Math.Abs(capturedResidualSum - savedResidualSum), 0,
            Math.Max(1e-28, Math.Abs(capturedResidualSum) * 1e-10));
    }

    static void AssertOffsetMatchesAnalyticalSolution(NullModelComparison comparison, bool weighted)
    {
        var nullModels = comparison.NullSolutions.Select(solution => solution.Model).ToArray();
        Assert.Equal(comparison.Members.Count, nullModels.Length);
        foreach (var member in comparison.Members)
        {
            var memberModels = member.Scope == "shared" ? nullModels : nullModels
                .Where(model => model.Data.UniqueID == member.ExperimentId).ToArray();
            var injections = memberModels.SelectMany(model => model.Data.Injections)
                .Where(injection => injection.Include).ToArray();
            var expected = injections.Sum(injection =>
                    injection.InjectionMass * injection.PeakArea / Weight(injection, weighted))
                / injections.Sum(injection => injection.InjectionMass * injection.InjectionMass / Weight(injection, weighted));
            // 1 mJ/mol: far below any change the fitting objective can resolve.
            Assert.Equal(expected, member.Offset, 3);
        }

        static double Weight(InjectionData injection, bool useWeights) =>
            useWeights && injection.PeakAreaError > 0
                ? injection.PeakAreaError * injection.PeakAreaError
                : 1.0;
    }

    void WriteEvidence(string label, NullModelComparison comparison)
    {
        static string Criterion(FitInformationCriteria value) => value?.IsAiccAvailable == true
            ? value.Aicc.Value.ToString("G10", System.Globalization.CultureInfo.InvariantCulture)
            : value?.AiccUnavailableReason ?? "not calculated";

        static double ResidualSum(NullModelComparison value) => value.Members
            .SelectMany(member => member.Points.Where(point => point.Included))
            .Sum(point => Math.Pow(point.ObservedHeatJoules - point.PredictedHeatJoules, 2));

        output.WriteLine(
            $"{label}: binding={comparison.BindingFitSucceeded} ({comparison.BindingFitReason}); " +
            $"null={comparison.NullFitSucceeded} ({comparison.NullFitReason}); " +
            $"offsets={string.Join(",", comparison.Members.Select(member => $"{member.Offset:G10} J/mol [{member.Scope}]"))}; " +
            $"AICc binding/null={Criterion(comparison.BindingInformationCriteria)}/{Criterion(comparison.NullInformationCriteria)}; " +
            $"delta={comparison.DeltaAicc?.ToString("G10", System.Globalization.CultureInfo.InvariantCulture) ?? comparison.ComparisonUnavailableReason}; " +
            $"likelihood={comparison.BindingInformationCriteria?.LikelihoodMode ?? comparison.NullInformationCriteria?.LikelihoodMode}; " +
            $"n={comparison.BindingInformationCriteria?.ObservationCount ?? comparison.NullInformationCriteria?.ObservationCount}; " +
            $"p/K binding={comparison.BindingInformationCriteria?.FittedParameterCount}/{comparison.BindingInformationCriteria?.LikelihoodParameterCount}, " +
            $"null={comparison.NullInformationCriteria?.FittedParameterCount}/{comparison.NullInformationCriteria?.LikelihoodParameterCount}; " +
            $"RSS binding={comparison.BindingInformationCriteria?.RawResidualSumOfSquares:G10}, " +
            $"null={comparison.NullInformationCriteria?.RawResidualSumOfSquares:G10}, " +
            $"captured-null-RSS={ResidualSum(comparison):G10} J²");
    }

    static ExperimentData ReadHepes(string fileName) => IntegratedHeatReader.ReadFile(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "BufferSubtraction", fileName));

    sealed class MicrocalPrompt : IImportPromptService
    {
        public EnergyUnitPromptResult AskForEnergyUnit(string fileName, string encounteredValue, bool allowQueueReuse) =>
            new(EnergyUnit.MicroCal, useForRemainingFilesInQueue: false, isCancelled: false);
    }
}
